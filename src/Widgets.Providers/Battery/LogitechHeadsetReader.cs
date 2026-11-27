using System.Runtime.CompilerServices;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.Logitech;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of Logitech and Astro wireless headsets, read from the base station's vendor interface: the older G533, G535,
/// G633 to G935 and G PRO (HID++ voltage), the Astro A50 Gen 5, and the G PRO X 2, G PRO X 3 and G522 (Centurion). Every request
/// is a fixed read-only query (see <see cref="HeadsetProtocols"/>). A headset that does not answer gives no reading, since
/// silence is not "off": only an explicit error or a "not connected" answer is. Ported from
/// taskbar_battery_widget/logitech_headsets.py of Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md). Not tested
/// on real hardware.
/// </summary>
public sealed class LogitechHeadsetReader : IBatteryReader
{
    private static readonly TimeSpan DeviceBudget = TimeSpan.FromSeconds(1.5);

    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;
    private readonly Func<bool> _experimental;

    /// <param name="experimental">Asked on every read. The models upstream marks beta (Centurion) are only read when it is true.</param>
    public LogitechHeadsetReader(IHidSystem hid, TimeSpan? budget = null, Func<bool>? experimental = null)
    {
        _hid = hid;
        _budget = budget ?? DeviceBudget;
        _experimental = experimental ?? (() => false);
    }

    /// <summary>True for a headset base this reader knows, the experimental ones included.</summary>
    public static bool Supports(int vendorId, int productId) => vendorId == HeadsetProtocols.LogitechVid && HeadsetProtocols.ForPid(productId) is not null;

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(HeadsetProtocols.LogitechVid); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        var experimental = _experimental();
        var done = new HashSet<int>();
        foreach (var info in infos.Where(i => HeadsetProtocols.ForPid(i.Pid) is not null).OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var model = HeadsetProtocols.ForPid(info.Pid)!;
            if (done.Contains(info.Pid) || (model.Experimental && !experimental) || !Matches(info, model)) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_budget);
                using var h = _hid.Open(info);
                if (await ReadOneAsync(info, model, h, budget.Token).ConfigureAwait(false) is not { } reading) continue;
                done.Add(info.Pid);
                result.Add(reading);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private static bool Matches(HidInfo i, HeadsetModel model)
    {
        if (!model.Collections.Contains((i.UsagePage, i.Usage))) return false;
        return model.Protocol == HeadsetProtocol.Legacy
            ? i.OutputReportLength == HeadsetProtocols.LegacyLength && i.InputReportLength >= HeadsetProtocols.LegacyLength
            : i.OutputReportLength == 64 && i.InputReportLength == 64;
    }

    private static async Task<BatteryReading?> ReadOneAsync(HidInfo info, HeadsetModel model, IHidHandle h, CancellationToken ct)
    {
        var name = DeviceNaming.Sanitize(info.Product);
        if (name.Length == 0) name = model.Name;
        var id = $"logitech:{info.Pid:x4}";

        int? percent = null;
        var charging = false;
        var connected = true;
        switch (model.Protocol)
        {
            case HeadsetProtocol.Legacy:
                switch (await LegacyAsync(model, h, ct).ConfigureAwait(false))
                {
                    case null: return null;                 // silent or unresolved: keep the last value
                    case { Off: true }: connected = false; break;
                    case { } ok: (percent, charging) = (ok.Percent, ok.Charging); break;
                }
                break;
            case HeadsetProtocol.Astro5:
                if (await ExchangeAsync(h, HeadsetProtocols.AstroRequest, HeadsetProtocols.ParseAstroReply, ct).ConfigureAwait(false) is not { } astro) return null;
                (percent, charging) = astro;
                break;
            default:
                var (state, battery) = await CenturionBatteryAsync(h, model, ct).ConfigureAwait(false);
                if (state == "ok") (percent, charging) = battery!.Value;
                else if (state == "off") connected = false;
                else return null;                           // no usable answer: do not guess
                break;
        }
        return new BatteryReading(id, name, DeviceKind.Headset, percent, charging, connected);
    }

    // ---- shared transport

    /// <summary>Writes one request, then reads until the parser accepts a report or the token fires.</summary>
    private static async Task<T?> ExchangeAsync<T>(IHidHandle h, byte[] request, Func<byte[]?, T?> parser, CancellationToken ct) where T : struct
    {
        if (ct.IsCancellationRequested) return null;
        await h.DrainAsync().ConfigureAwait(false);
        if (!await WriteAsync(h, request, ct).ConfigureAwait(false)) return null;
        while (!ct.IsCancellationRequested)
        {
            if (parser(await ReadAsync(h, TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false)) is { } result) return result;
            await Task.Yield();
        }
        return null;
    }

    private static async Task<bool> WriteAsync(IHidHandle h, byte[] frame, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sending.CancelAfter(TimeSpan.FromSeconds(1));
        return await h.WriteAsync(frame, sending.Token).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadAsync(IHidHandle h, TimeSpan slice, CancellationToken ct)
    {
        using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
        one.CancelAfter(slice);
        return await h.ReadAsync(one.Token).ConfigureAwait(false);
    }

    // ---- legacy HID++

    private sealed record LegacyResult(bool Off, int Percent, bool Charging);

    private static async Task<LegacyResult?> LegacyAsync(HeadsetModel model, IHidHandle h, CancellationToken ct)
    {
        var sw = model.Sw;
        int? index = null;
        var featureId = 0;
        foreach (var feature in HeadsetProtocols.LegacyFeatures)
        {
            var lookup = await ExchangeAsync(h, HeadsetProtocols.LegacyLookup(feature, sw), b => HeadsetProtocols.ParseLegacyLookup(b, sw), ct).ConfigureAwait(false);
            if (lookup is not { } found) return null;       // silent: do not guess
            if (found.State == "ok") { index = found.Index; featureId = feature; break; }
        }
        if (index is not { } idx) return null;              // neither voltage feature exists

        var reply = await ExchangeAsync(h, HeadsetProtocols.LegacyRequest(idx, featureId, sw), b => HeadsetProtocols.ParseLegacyReply(b, idx, sw), ct).ConfigureAwait(false);
        if (reply is not { } r) return null;
        return r.State == "off"
            ? new LegacyResult(true, 0, false)
            : new LegacyResult(false, HeadsetProtocols.Percent(r.Millivolts, model.Curve!), r.ChargeState == HeadsetProtocols.LegacyChargingState);
    }

    // ---- Centurion

    /// <summary>Decoded payloads until the budget runs out, at most 8 frames.</summary>
    private static async IAsyncEnumerable<byte[]> FramesAsync(IHidHandle h, byte[] prefix, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < 8 && !ct.IsCancellationRequested; i++)
        {
            var raw = await ReadAsync(h, TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
            if (raw is null) { await Task.Yield(); continue; }
            if (HeadsetProtocols.ExtractCenturionPayload(raw, prefix) is { Length: >= 2 } payload) yield return payload;
        }
    }

    private static async Task<byte[]?> CenturionDirectAsync(IHidHandle h, byte[] prefix, byte[] frame, int index, bool matchSw, CancellationToken ct)
    {
        await h.DrainAsync().ConfigureAwait(false);
        if (!await WriteAsync(h, frame, ct).ConfigureAwait(false)) return null;
        await foreach (var pl in FramesAsync(h, prefix, ct).ConfigureAwait(false))
            if (pl[0] == index && (!matchSw || pl[1] == HeadsetProtocols.CenturionSw)) return pl[2..];
        return null;
    }

    /// <summary>"on", "off" or "fail". "fail" also covers a reply of the wrong shape, which means the index is not a bridge. Only a clear "none connected" is "off".</summary>
    private static async Task<string> BridgeStateAsync(IHidHandle h, byte[] prefix, int bridge, CancellationToken ct)
    {
        var p = await CenturionDirectAsync(h, prefix, HeadsetProtocols.CenturionStatus(prefix, bridge), bridge, true, ct).ConfigureAwait(false);
        if (p is null || p.Length < 4) return "fail";
        return p[2] == 0 && p[3] == 0 ? "off" : "on";
    }

    private static async Task<(string State, byte[]? Params)> ProbeAsync(IHidHandle h, byte[] prefix, int bridge, CancellationToken ct) =>
        await BridgeStateAsync(h, prefix, bridge, ct).ConfigureAwait(false) == "off" ? ("off", null) : ("fail", null);

    private static async Task<(string State, byte[]? Params)> CenturionBridgedAsync(
        IHidHandle h, byte[] prefix, byte[] frame, int bridge, int subIndex, CancellationToken ct)
    {
        await h.DrainAsync().ConfigureAwait(false);
        if (!await WriteAsync(h, frame, ct).ConfigureAwait(false)) return ("fail", null);

        var acked = false;
        await foreach (var pl in FramesAsync(h, prefix, ct).ConfigureAwait(false))
        {
            if (pl[0] == 0xFF && pl.Length >= 3 && pl[1] == bridge && pl[2] == 0x11)    // the request was rejected
                return await ProbeAsync(h, prefix, bridge, ct).ConfigureAwait(false);
            if (pl[0] != bridge) continue;
            if (pl[1] == 0x00)                                                          // a connection event
            {
                if (pl.Length >= 4 && pl[3] == 0x00) return ("off", null);
                continue;
            }
            if (pl[1] >> 4 != 1) continue;
            if ((pl[1] & 0x0F) == HeadsetProtocols.CenturionSw) { acked = true; continue; }
            if ((pl[1] & 0x0F) != 0 || pl.Length < 6 || pl[4] != 0) continue;
            if (pl[5] == 0xFF && pl.Length >= 7 && pl[6] == subIndex) return ("fail", null);
            if (pl[5] == subIndex && pl.Length >= 7) return ("ok", pl[7..]);
        }
        return acked ? await ProbeAsync(h, prefix, bridge, ct).ConfigureAwait(false) : ("fail", null);
    }

    /// <summary>
    /// "ok" with the battery, "off", or "fail". The bridge and battery feature indexes come from the device, and each is checked
    /// (a getter on it must answer with the expected shape) before anything else is sent to it.
    /// </summary>
    private static async Task<(string State, (int Percent, bool Charging)? Battery)> CenturionBatteryAsync(IHidHandle h, HeadsetModel model, CancellationToken ct)
    {
        var prefix = model.Prefix!;
        var p = await CenturionDirectAsync(h, prefix, HeadsetProtocols.CenturionBridgeLookup(prefix), 0x00, false, ct).ConfigureAwait(false);
        if (p is null || p.Length == 0 || p[0] is < 1 or > 0x7F) return ("fail", null);
        int bridge = p[0];

        var state = await BridgeStateAsync(h, prefix, bridge, ct).ConfigureAwait(false);
        if (state != "on") return (state, null);                    // "fail": not a bridge; "off": no headset

        (state, p) = await CenturionBridgedAsync(h, prefix, HeadsetProtocols.CenturionBatteryLookup(prefix, bridge), bridge, 0x00, ct).ConfigureAwait(false);
        if (state != "ok") return (state, null);
        if (p is null || p.Length == 0 || p[0] is < 1 or > 0x7F || p[0] == bridge) return ("fail", null);
        int sub = p[0];

        (state, p) = await CenturionBridgedAsync(h, prefix, HeadsetProtocols.CenturionBatteryGet(prefix, bridge, sub), bridge, sub, ct).ConfigureAwait(false);
        if (state != "ok") return (state, null);
        return HeadsetProtocols.ParseCenturionBattery(p) is { } battery ? ("ok", battery) : ("fail", null);
    }
}

/// <summary>
/// The Logitech mouse and keyboard reader and the headset reader as one. A headset base can also show up as a HID++ receiver; this
/// keeps one entry for it. For each base with HID++ entries (<c>logitech:pid:slot</c>) and a headset entry (<c>logitech:pid</c>): the
/// headset entry goes, unless none of the HID++ entries has a reading and the headset one has, in which case they go instead.
/// </summary>
public sealed class LogitechCombinedReader(LogitechReader hidpp, LogitechHeadsetReader headsets) : IBatteryReader
{
    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        // Each reader alone never throws for a device; one failing for another reason must not hide the other's devices.
        var results = await Task.WhenAll(Safe(hidpp, ct), Safe(headsets, ct)).ConfigureAwait(false);
        return Dedupe([.. results[0], .. results[1]]);
    }

    private static async Task<IReadOnlyList<BatteryReading>> Safe(IBatteryReader reader, CancellationToken ct)
    {
        try { return await reader.ReadAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return []; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return []; }
    }

    internal static IReadOnlyList<BatteryReading> Dedupe(IReadOnlyList<BatteryReading> readings)
    {
        static string[] Parts(BatteryReading r) => r.Id.Split(':');
        var slots = readings.Where(r => Parts(r) is ["logitech", _, _, ..]).GroupBy(r => Parts(r)[1]).ToDictionary(g => g.Key, g => g.ToList());
        if (slots.Count == 0) return readings;

        var headsetWins = slots
            .Where(kv => kv.Value.All(p => p.Percent is null) && readings.Any(r => r.Id == $"logitech:{kv.Key}" && r.Percent is not null))
            .Select(kv => kv.Key)
            .ToHashSet();

        return [.. readings.Where(r => Parts(r) switch
        {
            ["logitech", var pid] when slots.ContainsKey(pid) && !headsetWins.Contains(pid) => false,   // the headset entry of a base HID++ lists
            ["logitech", var pid, _, ..] when headsetWins.Contains(pid) => false,                        // the HID++ entries of a base the headset reader reads
            _ => true,
        })];
    }
}
