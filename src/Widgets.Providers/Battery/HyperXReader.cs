using System.Collections.Concurrent;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.HyperX;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of HyperX wireless headsets through their USB dongle: Cloud Flight, Cloud II (HP revision), Cloud Alpha 2 and
/// Cloud Flight 2, and with experimental readers on the Cloud II Kingston revision and the Cloud Alpha. Only fixed read queries
/// are ever sent. A headset shows as off only on an explicit "not connected" answer; no answer gives no reading and the last level
/// stays. The one exception is the Cloud Flight, whose dongle stays silent while the headset is off, so there silence is off.
/// Ported from taskbar_battery_widget/hyperx.py of Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md).
/// Not tested on real hardware.
/// </summary>
public sealed class HyperXReader : IBatteryReader
{
    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;
    private readonly Func<bool> _experimental;
    private readonly ConcurrentDictionary<int, int> _lastLevel = new();               // pid to the last level seen while discharging

    public HyperXReader(IHidSystem hid, TimeSpan? budget = null, Func<bool>? experimental = null)
    {
        _hid = hid;
        _budget = budget ?? TimeSpan.FromSeconds(1.5);
        _experimental = experimental ?? (() => false);
    }

    /// <summary>True for a HyperX dongle this reader knows (experimental ones included).</summary>
    public static bool Supports(int vendorId, int productId) =>
        (vendorId == HyperXProtocol.HyperXVid && HyperXProtocol.FlightPids.Contains(productId))
        || HyperXProtocol.Models.Any(m => m.Vid == vendorId && m.Pids.Contains(productId));

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        var infos = new List<HidInfo>();
        foreach (var vendor in HyperXProtocol.Vendors)
        {
            try { infos.AddRange(_hid.Enumerate(vendor)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        var beta = _experimental();
        var done = new HashSet<(int, int)>();
        foreach (var info in infos.OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains((info.Vid, info.Pid))) continue;

            HyperXModel? model = null;
            if (HyperXProtocol.IsFlight(info))
            {
                if (!HyperXProtocol.FlightCollectionOk(info)) continue;
            }
            else
            {
                model = HyperXProtocol.ModelFor(info);
                if (model is null || (model.Beta && !beta) || !HyperXProtocol.CollectionOk(model, info)) continue;
            }

            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_budget);
                using var h = _hid.Open(info);
                var reading = model is null
                    ? await ReadFlightAsync(info, h, budget.Token).ConfigureAwait(false)
                    : await ReadModelAsync(model, info, h, budget.Token).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (reading is null) continue;
                done.Add((info.Vid, info.Pid));
                result.Add(reading);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private static string Id(HidInfo info) => $"hyperx:{info.Pid:x4}";
    private static string NameOf(HidInfo info, string fallback) => DeviceNaming.Sanitize(info.Product) is { Length: > 0 } n ? n : fallback;

    // ---- transport

    private static async Task<bool> WriteAsync(IHidHandle h, byte[] frame, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sending.CancelAfter(TimeSpan.FromSeconds(1));
        return await h.WriteAsync(frame, sending.Token).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadSliceAsync(IHidHandle h, CancellationToken ct)
    {
        using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
        one.CancelAfter(250);
        var raw = await h.ReadAsync(one.Token).ConfigureAwait(false);
        if (raw is null) await Task.Yield();
        return raw;
    }

    // ---- Cloud Flight

    private async Task<BatteryReading?> ReadFlightAsync(HidInfo info, IHidHandle h, CancellationToken ct)
    {
        var name = NameOf(info, HyperXProtocol.FlightName);
        var mv = await QueryFlightAsync(h, ct).ConfigureAwait(false);   // the budget ending is silence; the caller checks for a cancelled poll
        if (mv is not { } volts) return new BatteryReading(Id(info), name, DeviceKind.Headset, null, false, false);   // the dongle is silent while the headset is off
        if (volts > HyperXProtocol.FlightChargingMv)                                    // the percentage is unknown while charging
            return new BatteryReading(Id(info), name, DeviceKind.Headset, _lastLevel.TryGetValue(info.Pid, out var last) ? last : null, true, true);
        var percent = HyperXProtocol.FlightPercent(volts);
        _lastLevel[info.Pid] = percent;
        return new BatteryReading(Id(info), name, DeviceKind.Headset, percent, false, true);
    }

    private static async Task<int?> QueryFlightAsync(IHidHandle h, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;
        await h.DrainAsync().ConfigureAwait(false);
        var request = HyperXProtocol.FlightRequest();
        if (!HyperXProtocol.IsFlightRequest(request)) throw new InvalidOperationException("Request is not allowed.");
        if (!await WriteAsync(h, request, ct).ConfigureAwait(false)) return null;
        while (!ct.IsCancellationRequested)
            if (HyperXProtocol.ParseFlightReply(await ReadSliceAsync(h, ct).ConfigureAwait(false)) is { } mv) return mv;
        return null;
    }

    // ---- the other models

    private static async Task<byte[]?> AskAsync(IHidHandle h, HyperXFamily family, int outputLength, CancellationToken ct, params byte[] command)
    {
        if (ct.IsCancellationRequested) return null;
        await h.DrainAsync().ConfigureAwait(false);
        var request = HyperXProtocol.ModelRequest(family, command);
        byte[] frame = [.. request, .. new byte[Math.Max(0, outputLength - request.Length)]];     // padded up to the output report length
        if (!await WriteAsync(h, frame, ct).ConfigureAwait(false)) return null;
        while (!ct.IsCancellationRequested)
            if (HyperXProtocol.MatchReply(family, await ReadSliceAsync(h, ct).ConfigureAwait(false), command) is { } reply) return reply;
        return null;
    }

    private BatteryReading Device(HidInfo info, string name, int? percent, bool connected, bool charging = false) =>
        new(Id(info), name, DeviceKind.Headset, percent, charging, connected);

    private async Task<BatteryReading?> ReadModelAsync(HyperXModel m, HidInfo info, IHidHandle h, CancellationToken ct)
    {
        var name = NameOf(info, m.Name);
        var n = info.OutputReportLength;

        switch (m.Family)
        {
            case HyperXFamily.Cloud2:
            case HyperXFamily.Cloud2K:
                if (m.Family == HyperXFamily.Cloud2K)
                    await h.GetInputReportAsync(0x06, 64, ct).ConfigureAwait(false);   // a harmless pre-read that some firmware expects
                var level = await AskAsync(h, m.Family, n, ct, HyperXProtocol.CmdLevel).ConfigureAwait(false);
                if (level is null) return null;                                         // silent: unknown, not "off"
                var charge = await AskAsync(h, m.Family, n, ct, HyperXProtocol.CmdCharge).ConfigureAwait(false);
                if (level[7] > 100) return null;
                return Device(info, name, level[7], true, charge is not null && charge[4] == 1);

            case HyperXFamily.Alpha:
                var connection = await AskAsync(h, m.Family, n, ct, HyperXProtocol.AlphaConnection).ConfigureAwait(false);
                if (connection is null) return null;                                    // silent: unknown, not "off"
                if (connection[3] == 0x01) return Device(info, name, null, false);      // explicit: the headset is not linked
                var charging = await AskAsync(h, m.Family, n, ct, HyperXProtocol.AlphaCharge).ConfigureAwait(false);
                if (charging is not null && charging[3] == 0x01)                         // the percentage is unknown while charging
                    return Device(info, name, _lastLevel.TryGetValue(info.Pid, out var last) ? last : null, true, true);
                var alphaLevel = await AskAsync(h, m.Family, n, ct, HyperXProtocol.AlphaLevel).ConfigureAwait(false);
                if (alphaLevel is null || alphaLevel[3] > 100) return null;
                _lastLevel[info.Pid] = alphaLevel[3];
                return Device(info, name, alphaLevel[3], true);

            default:
                var linked = await AskAsync(h, m.Family, n, ct, HyperXProtocol.Alpha2Connection).ConfigureAwait(false);
                if (linked is null) return null;
                if (linked[2] == 0x00) return Device(info, name, null, false);
                var battery = await AskAsync(h, m.Family, n, ct, HyperXProtocol.Alpha2Battery).ConfigureAwait(false);
                if (battery is null || battery[2] > 100) return null;
                return Device(info, name, battery[2], true, battery[3] == 0x01);
        }
    }
}
