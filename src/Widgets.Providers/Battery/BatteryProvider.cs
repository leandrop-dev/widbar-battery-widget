using Widgets.Core;

namespace Widgets.Providers.Battery;

/// <summary>One source of device batteries (SteelSeries HID, XInput, Bluetooth LE, ...).</summary>
public interface IBatteryReader
{
    Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct);
}

/// <summary>
/// Turns the readers' results into battery tiles. A reader that fails or stays silent keeps its devices' last known
/// value (marked stale); a device that reported itself off or disconnected keeps its tile with no value.
/// </summary>
public sealed class BatteryProvider : IProvider
{
    private sealed record Seen(BatteryReading Reading, DateTimeOffset At);

    private readonly IReadOnlyList<IBatteryReader> _readers;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Seen> _lastGood = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BatteryReading> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastConnected = new(StringComparer.Ordinal);

    private readonly BatteryCache? _cache;

    /// <param name="cache">Optional: remembers last known levels between runs, for devices that are asleep at startup.</param>
    public BatteryProvider(IReadOnlyList<IBatteryReader> readers, TimeSpan? interval = null, TimeProvider? time = null,
        BatteryCache? cache = null)
    {
        _readers = readers;
        Interval = interval ?? TimeSpan.FromSeconds(60);
        _time = time ?? TimeProvider.System;
        _cache = cache;

        // Remembered devices start out as "silent with a last value", which is shown dimmed until they report again.
        foreach (var c in cache?.Load(_time.GetUtcNow()) ?? [])
        {
            var reading = new BatteryReading(c.Id, c.Name, c.Kind, c.Percent, false, true);
            _known[c.Id] = reading;
            _lastGood[c.Id] = new Seen(reading, c.At);
            _lastConnected[c.Id] = c.At;
        }
    }

    public string Id => "battery";

    private long _intervalTicks;

    /// <summary>How often the scheduler polls. It reads this before every wait, so a new value applies from the next one.</summary>
    public TimeSpan Interval
    {
        get => TimeSpan.FromTicks(Volatile.Read(ref _intervalTicks));
        set => Volatile.Write(ref _intervalTicks, value.Ticks);
    }

    public async Task<IReadOnlyList<Metric>> PollAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var fresh = new Dictionary<string, BatteryReading>(StringComparer.Ordinal);
        foreach (var reader in _readers)
        {
            try
            {
                foreach (var r in await reader.ReadAsync(ct).ConfigureAwait(false)) fresh[r.Id] = r;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        var changed = false;
        foreach (var (id, r) in fresh)
        {
            _known[id] = r;
            // A device first met while switched off counts as disconnected from now, so it is not hidden the moment it appears.
            if (r.Connected) _lastConnected[id] = now;
            else _lastConnected.TryAdd(id, now);
            if (r.Percent is null) continue;
            changed |= !_lastGood.TryGetValue(id, out var before) || before.Reading.Percent != r.Percent;
            _lastGood[id] = new Seen(r, now);
        }
        // Only when a level actually changed, so a steady device does not rewrite the file every minute.
        if (changed)
            _cache?.Save(_lastGood.Values.Select(s => new CachedBattery(
                s.Reading.Id, s.Reading.Name, s.Reading.Kind, s.Reading.Percent!.Value, s.At)));

        // A game controller that is plugged in or paired makes the ones that are not step aside: you hold one at a time.
        // They stay in the list, flagged, so their label, colour and place are kept for when they come back; the widget
        // leaves them off the taskbar. Other kinds (a sleeping mouse) are not affected.
        var padInUse = fresh.Values.Any(r => r.Kind == DeviceKind.Gamepad && r.Connected);

        var metrics = new List<Metric>();
        foreach (var (id, known) in _known.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var live = fresh.TryGetValue(id, out var f) ? f : null;
            Metric? tile = null;
            if (live?.Percent is { } pct)
            {
                tile = Tile(live, pct, now, stale: false);
            }
            else if (live is { Connected: true, Note: { } note })
            {
                tile = Tile(live, null, now, stale: false) with { Text = note }; // connected with no battery, e.g. a wired pad
            }
            else if (live is { Connected: false })
            {
                tile = Tile(live, null, now, stale: false); // switched off: shown as "--", not as an old value
            }
            else if (_lastGood.TryGetValue(id, out var seen))
            {
                tile = Tile(seen.Reading, seen.Reading.Percent, seen.At, stale: true); // silent: keep the last value
            }
            if (tile is null) continue;

            var connected = live is { Connected: true };
            metrics.Add(tile with
            {
                OfflineSince = connected ? null : _lastConnected.GetValueOrDefault(id, now),
                Superseded = padInUse && known.Kind == DeviceKind.Gamepad && !connected,
            });
        }
        return metrics;
    }

    private Metric Tile(BatteryReading r, double? percent, DateTimeOffset at, bool stale) =>
        new($"battery:{r.Id}", ShortName(r), MetricKind.Battery, percent, "%", Id, at, stale, Charging: r.Charging && !stale && percent is not null,
            Icon: IconFor(r.Kind));

    private static string IconFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset => MetricIcons.Headset,
        DeviceKind.Mouse => MetricIcons.Mouse,
        DeviceKind.Keyboard => MetricIcons.Keyboard,
        DeviceKind.Gamepad => MetricIcons.Gamepad,
        _ => MetricIcons.Generic,
    };

    /// <summary>"SteelSeries Arctis Nova 7" becomes "Arctis Nova 7": the brand adds width, not information.</summary>
    internal static string ShortName(BatteryReading r) =>
        r.Name.StartsWith("SteelSeries ", StringComparison.Ordinal) ? r.Name["SteelSeries ".Length..] : r.Name;
}
