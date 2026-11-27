using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.Logitech;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of Logitech wireless mice and keyboards, over HID++ through a Unifying, Bolt, Lightspeed or Nano receiver, or
/// directly over a cable. Only fixed read-only queries are sent: a ping, feature lookups, the device's name and type, and its
/// battery (see <see cref="Hidpp"/>). A device that answers the HID++ 2.0 ping with a HID++ 1.0 error is read through its two
/// HID++ 1.0 battery registers.
/// <para>
/// Ported from taskbar_battery_widget/hidpp.py of Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md), without its
/// merging of a device seen through its receiver and over its cable: a mouse plugged in to charge may show under a second entry
/// until the first one is hidden. Not tested on real hardware. Headsets use other protocols and are not read here.
/// </para>
/// </summary>
public sealed class LogitechReader : IBatteryReader
{
    private static readonly TimeSpan GroupBudget = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan InfoRetry = TimeSpan.FromMinutes(10); // a name or type that could not be read is not asked again for this long

    private readonly IHidSystem _hid;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ping;
    private readonly TimeSpan _query;
    private readonly ConcurrentDictionary<(GroupKey Group, int Index), SlotInfo> _slots = new();

    /// <param name="pingTimeout">How long a ping waits for a slot to answer. A slot that stays silent is skipped.</param>
    /// <param name="queryTimeout">How long every other query waits.</param>
    public LogitechReader(IHidSystem hid, TimeProvider? time = null, TimeSpan? pingTimeout = null, TimeSpan? queryTimeout = null)
    {
        _hid = hid;
        _time = time ?? TimeProvider.System;
        _ping = pingTimeout ?? TimeSpan.FromSeconds(1.6);
        _query = queryTimeout ?? TimeSpan.FromSeconds(0.8);
    }

    /// <summary>True for a receiver this reader knows. A cabled device has other product ids and is read all the same when it offers HID++.</summary>
    public static bool Supports(int vendorId, int productId) => vendorId == Hidpp.LogitechVid && Hidpp.KnownReceivers.Contains(productId);

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(Hidpp.LogitechVid); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return []; }

        var groups = Hidpp.FindGroups(infos);
        if (groups.Count == 0) return [];

        // Two interfaces of the same product (two receivers, say) need ids that tell them apart.
        var perProduct = groups.GroupBy(g => (g.Vid, g.Pid)).ToDictionary(x => x.Key, x => x.Count());
        var scans = groups.Select(g => ScanSafeAsync(g, perProduct[(g.Vid, g.Pid)] > 1 ? ":" + PathHash(g) : "", ct));
        return [.. (await Task.WhenAll(scans).ConfigureAwait(false)).SelectMany(r => r)];
    }

    private static string PathHash(HidppGroup g) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{g.Key.Interface}|{g.Key.Parent}")))[..6].ToLowerInvariant();

    private Task<List<BatteryReading>> ScanSafeAsync(HidppGroup group, string suffix, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(GroupBudget);
                return await ScanGroupAsync(group, suffix, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return []; }
            catch (Exception ex) when (ex is not OperationCanceledException) { return []; }
        }, CancellationToken.None);

    private async Task<List<BatteryReading>> ScanGroupAsync(HidppGroup group, string suffix, CancellationToken ct)
    {
        var readings = new List<BatteryReading>();
        var receiver = Hidpp.IsReceiver(group);
        var indexes = receiver ? Enumerable.Range(1, 6) : [0xFF];

        using var channel = new HidppChannel(_hid, group);
        var misses = 0;
        foreach (var idx in indexes)
        {
            if (ct.IsCancellationRequested || channel.Broken) break;
            var state = await channel.PingAsync(idx, _ping, ct).ConfigureAwait(false);
            var slotKey = (group.Key, idx);
            var id = $"logitech:{group.Pid:x4}:{idx:x2}{suffix}";

            if (state == PingState.Timeout)
            {
                if (receiver && ++misses >= 2) break; // two silent slots in a row: nothing paired further on
                continue;
            }
            if (state == PingState.Hidpp1 && channel.LastErrorKind == "hidpp1" && channel.LastError == Hidpp.Hidpp1ErrInvalidSubId)
            {
                // A HID++ 1.0 device: its battery registers, and nothing else.
                if (await channel.ReadBatteryHidpp1Async(idx, _query, ct).ConfigureAwait(false) is { } legacy)
                {
                    var name = DeviceNaming.Sanitize(group.Product);
                    if (name.Length == 0 || name.Contains("receiver", StringComparison.OrdinalIgnoreCase)) name = "Logitech wireless device";
                    readings.Add(new BatteryReading(id, name, DeviceNaming.KindFromName(name), legacy.Percent, legacy.Charging, true));
                }
                continue;
            }
            if (state != PingState.Ok)
            {
                if (state == PingState.Absent) _slots.TryRemove(slotKey, out _); // the slot may be paired with another device later
                continue;
            }

            var info = _slots.GetValueOrDefault(slotKey) ?? new SlotInfo();
            var now = _time.GetUtcNow();
            if ((string.IsNullOrEmpty(info.Name) || info.Type is null) && (info.ProbedAt is null || now - info.ProbedAt > InfoRetry))
            {
                info.ProbedAt = now; // a failure is remembered for the retry time
                info.Name = await channel.ReadNameAsync(idx, _query, ct).ConfigureAwait(false) ?? info.Name;
                info.Type = await channel.ReadTypeAsync(idx, _query, ct).ConfigureAwait(false);
            }

            var reading = await channel.ReadBatteryAsync(idx, info, _query, ct).ConfigureAwait(false);
            var deviceName = DeviceNaming.Sanitize(string.IsNullOrEmpty(info.Name) ? group.Product : info.Name);
            if (deviceName.Length == 0) deviceName = "Logitech device";
            var kind = Hidpp.KindFromDeviceType(info.Type) ?? DeviceNaming.KindFromName(deviceName);

            if (reading is null && ++info.BatteryFailures >= 2) _slots.TryRemove(slotKey, out _); // the identity may be out of date: probe again next time
            else
            {
                if (reading is not null) info.BatteryFailures = 0;
                _slots[slotKey] = info;
            }
            readings.Add(new BatteryReading(id, deviceName, kind, reading?.Percent, reading?.Charging ?? false, true));
        }
        return readings;
    }
}
