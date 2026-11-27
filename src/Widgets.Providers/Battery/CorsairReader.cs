using Widgets.Providers.Battery.Corsair;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of Corsair wireless headsets through their USB receiver (Void, Virtuoso XT and SE) and, with experimental readers on,
/// the V2 family (Void Wireless V2, Virtuoso Max, HS80 Max). Only read queries are ever sent. A headset shows as off only on an
/// explicit answer (a Void "no headset" status, an error status from the paired-headset target); silence or a failed write gives
/// no reading, so the last level stays. Ported from taskbar_battery_widget/corsair.py of Gabrielfch/taskbar-battery-widget (MIT,
/// see THIRD_PARTY_NOTICES.md). Not tested on real hardware.
/// </summary>
public sealed class CorsairReader : IBatteryReader
{
    private const int ProbeMs = 300, V2Attempts = 3;

    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;
    private readonly Func<bool> _experimental;

    public CorsairReader(IHidSystem hid, TimeSpan? budget = null, Func<bool>? experimental = null)
    {
        _hid = hid;
        _budget = budget ?? TimeSpan.FromSeconds(1.5);
        _experimental = experimental ?? (() => false);
    }

    /// <summary>True for a Corsair headset receiver this reader knows (experimental ones included).</summary>
    public static bool Supports(int vendorId, int productId) => vendorId == CorsairProtocol.Vid && CorsairProtocol.Models.ContainsKey(productId);

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(CorsairProtocol.Vid); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        var beta = _experimental();
        var done = new HashSet<int>();
        foreach (var info in infos.Where(i => CorsairProtocol.Models.ContainsKey(i.Pid)).OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var model = CorsairProtocol.Models[info.Pid];
            if (done.Contains(info.Pid) || (model.Beta && !beta) || !CorsairProtocol.CollectionOk(model, info)) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_budget);
                using var h = _hid.Open(info);
                var reading = await ReadOneAsync(info, model, h, budget.Token).ConfigureAwait(false);
                if (reading is null) continue;
                done.Add(info.Pid);
                result.Add(reading);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private static Task<BatteryReading?> ReadOneAsync(HidInfo info, CorsairModel model, IHidHandle h, CancellationToken ct)
    {
        var product = DeviceNaming.Sanitize(info.Product);
        var name = product.Length > 0 ? product : model.Name;
        return model.Family switch
        {
            CorsairFamily.Void => ReadVoidAsync(info, name, h, ct),
            CorsairFamily.Xt => ReadXtAsync(info, name, model, h, ct),
            _ => ReadV2Async(info, name, h, ct),
        };
    }

    private static string Id(HidInfo info) => $"corsair:{info.Pid:x4}";
    private static BatteryReading Off(HidInfo info, string name) => new(Id(info), name, DeviceKind.Headset, null, false, false);

    // ---- transport

    private static async Task<bool> WriteAsync(IHidHandle h, byte[] frame, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sending.CancelAfter(TimeSpan.FromSeconds(1));
        return await h.WriteAsync(frame, sending.Token).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadSliceAsync(IHidHandle h, int milliseconds, CancellationToken ct)
    {
        using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
        one.CancelAfter(milliseconds);
        var raw = await h.ReadAsync(one.Token).ConfigureAwait(false);
        if (raw is null) await Task.Yield();
        return raw;
    }

    /// <summary>Writes a Bragi frame after checking it against the whitelist; when the output length is known it must match exactly.</summary>
    private static async Task<bool> SendBragiAsync(IHidHandle h, byte[] frame, bool reportIds, int outputLength, CancellationToken ct)
    {
        if (!CorsairProtocol.IsAllowedWire(frame, reportIds)) throw new InvalidOperationException("Request is not allowed.");
        if (outputLength > 0 && frame.Length != outputLength) return false;
        return await WriteAsync(h, frame, ct).ConfigureAwait(false);
    }

    /// <summary>One GET; the reply when it carries status OK or an error status, null when nothing answered.</summary>
    private static async Task<byte[]?> BragiGetAsync(IHidHandle h, int outputLength, byte target, byte prop, bool reportIds, CancellationToken ct,
        bool strictSource = true, int? probeMs = null)
    {
        if (ct.IsCancellationRequested) return null;
        await h.DrainAsync().ConfigureAwait(false);
        var frame = CorsairProtocol.BragiFrame(target, prop, reportIds, outputLength);
        if (!await SendBragiAsync(h, frame, reportIds, outputLength, ct).ConfigureAwait(false)) return null;

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (probeMs is { } ms) window.CancelAfter(ms);
        while (!window.IsCancellationRequested)
        {
            var raw = await ReadSliceAsync(h, probeMs is null ? 250 : 100, window.Token).ConfigureAwait(false);
            if (CorsairProtocol.MatchBragi(raw, target, strictSource) is { } reply) return reply;
        }
        return null;
    }

    // ---- families

    private static async Task<BatteryReading?> ReadVoidAsync(HidInfo info, string name, IHidHandle h, CancellationToken ct)
    {
        await h.DrainAsync().ConfigureAwait(false);
        var frame = CorsairProtocol.VoidRequest(info.OutputReportLength);
        if (!CorsairProtocol.IsVoidRequest(frame)) throw new InvalidOperationException("Request is not allowed.");
        if (!await WriteAsync(h, frame, ct).ConfigureAwait(false)) return null;      // a failed write is unknown, not "off"

        while (!ct.IsCancellationRequested)
        {
            if (CorsairProtocol.ParseVoidReply(await ReadSliceAsync(h, 250, ct).ConfigureAwait(false)) is not { } parsed) continue;
            if (parsed.State == CorsairProtocol.VoidState.Offline) return Off(info, name);   // an explicit "no headset connected"
            return new BatteryReading(Id(info), name, DeviceKind.Headset, parsed.Level, parsed.State == CorsairProtocol.VoidState.Charging, true);
        }
        return null;                                                                  // silent
    }

    private static async Task<BatteryReading?> ReadXtAsync(HidInfo info, string name, CorsairModel model, IHidHandle h, CancellationToken ct)
    {
        byte[] order = model.Wired ? [CorsairProtocol.TargetSelf, CorsairProtocol.TargetHeadset] : [CorsairProtocol.TargetHeadset, CorsairProtocol.TargetSelf];
        var refused = false;
        foreach (var target in order)
        {
            var r = await BragiGetAsync(h, info.OutputReportLength, target, CorsairProtocol.PropBattery, true, ct, probeMs: ProbeMs).ConfigureAwait(false);
            if (r is null) continue;
            if (r[3] != CorsairProtocol.StatusOk)
            {
                refused |= target == order[0];                                        // an explicit error from the expected target
                continue;
            }
            var tenths = CorsairProtocol.BragiValue(r);
            if (tenths > CorsairProtocol.BatteryMax) continue;
            var c = await BragiGetAsync(h, info.OutputReportLength, target, CorsairProtocol.PropCharge, true, ct, probeMs: ProbeMs).ConfigureAwait(false);
            var charging = c is not null && c[3] == CorsairProtocol.StatusOk && CorsairProtocol.BragiValue(c) == CorsairProtocol.Charging;
            return new BatteryReading(Id(info), name, DeviceKind.Headset, tenths / 10, charging, true);
        }
        return refused ? Off(info, name) : null;
    }

    private static async Task<BatteryReading?> ReadV2Async(HidInfo info, string name, IHidHandle h, CancellationToken ct)
    {
        // A 64-byte collection carries the report id in the frame; a 65-byte one takes a leading 0 as its (absent) id.
        var ids = info.OutputReportLength == CorsairProtocol.BragiLength;
        var outLen = info.OutputReportLength;

        // Wake the receiver and check that the headset answers its heartbeat: read-only GETs.
        foreach (var prop in new[] { CorsairProtocol.PropFirmware, CorsairProtocol.PropHeartbeat })
        {
            if (ct.IsCancellationRequested) return null;
            var frame = CorsairProtocol.BragiFrame(CorsairProtocol.TargetSelf, prop, ids, outLen);
            using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
            one.CancelAfter(500);
            if (!await SendBragiAsync(h, frame, ids, outLen, one.Token).ConfigureAwait(false)) return null;
        }

        var heartbeat = await BragiGetAsync(h, outLen, CorsairProtocol.TargetHeadset, CorsairProtocol.PropHeartbeat, ids, ct, strictSource: false).ConfigureAwait(false);
        if (heartbeat is null) return null;                                           // silent: unknown, not "off"
        if (heartbeat[3] != CorsairProtocol.StatusOk) return Off(info, name);         // an explicit error: not paired or not on

        for (var attempt = 0; attempt < V2Attempts; attempt++)
        {
            var r = await BragiGetAsync(h, outLen, CorsairProtocol.TargetHeadset, CorsairProtocol.PropBattery, ids, ct, strictSource: false).ConfigureAwait(false);
            if (r is null) return null;
            if (r[3] != CorsairProtocol.StatusOk) continue;
            var tenths = CorsairProtocol.BragiValue(r, 2);
            if (tenths is > 0 and <= CorsairProtocol.BatteryMax)
                return new BatteryReading(Id(info), name, DeviceKind.Headset, tenths / 10, false, true);
        }
        return new BatteryReading(Id(info), name, DeviceKind.Headset, null, false, true);
    }
}
