using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.Razer;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of Razer wireless mice, keyboards and headsets that answer the standard 90-byte feature report: a power-class "get
/// battery" and "get charging" query, sent with set-feature and answered with get-feature. The mouse and keyboard collections
/// are opened for feature reports only. Only those two read queries are ever sent. Ported from taskbar_battery_widget/razer.py of
/// Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md), except that the transaction id that answered is
/// remembered per device. Not tested on real hardware. The ODESSA headset dongles use another protocol, see
/// <see cref="RazerOdessaReader"/>.
/// </summary>
public sealed class RazerReader : IBatteryReader
{
    private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(2.5);
    private const int Retries = 2;

    // Product strings that say nothing about the device: a table name replaces them.
    private static readonly HashSet<string> GenericProducts =
        ["", "razer", "razer device", "razer hid", "hid", "hid device", "usb input device", "human interface device"];

    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;
    private readonly TimeSpan _settle;
    private readonly ConcurrentDictionary<string, byte> _working = new();      // device id to the transaction id that answered
    private readonly ConcurrentDictionary<string, int> _lastLevel = new();     // device id to its last level above zero

    /// <param name="settle">The pause after a set and after a get, which the device needs to prepare its answer.</param>
    public RazerReader(IHidSystem hid, TimeSpan? scanBudget = null, TimeSpan? settle = null)
    {
        _hid = hid;
        _budget = scanBudget ?? ScanBudget;
        _settle = settle ?? TimeSpan.FromMilliseconds(50);
    }

    /// <summary>True for a Razer product this widget knows by id. Other Razer devices that answer the same report are read all the same.</summary>
    public static bool Supports(int vendorId, int productId) =>
        vendorId == RazerProtocol.Vid && (RazerProtocol.Known.ContainsKey(productId) || RazerProtocol.OdessaDongles.ContainsKey(productId));

    private static bool IsVendor(HidInfo i) => i.UsagePage >= RazerProtocol.VendorPageMin;

    // A collection that declares the 90-byte feature report (and its report id) on a vendor page or on the mouse or keyboard page.
    private static bool SupportsFeatureReport(HidInfo i) =>
        i.FeatureReportLength >= RazerProtocol.FeatureBufferLength
        && (IsVendor(i) || (i.UsagePage == RazerProtocol.GenericDesktop && RazerProtocol.GenericUsages.Contains(i.Usage)));

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(RazerProtocol.Vid); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_budget);
        var seen = new HashSet<int>();
        // Vendor collections first (the classic devices), then the generic mouse and keyboard ones.
        foreach (var info in infos.OrderBy(i => !IsVendor(i)).ThenBy(i => i.Path, StringComparer.Ordinal))
        {
            if (budget.IsCancellationRequested) break;
            if (RazerProtocol.HasNoBattery(info.Pid) || !SupportsFeatureReport(info)) continue;
            if (seen.Contains(info.Pid) || RazerProtocol.OdessaDongles.ContainsKey(info.Pid)) continue; // the dongles have their own reader

            var id = $"razer:{info.Pid:x4}:{PathHash(info.Path)}";
            (int Percent, bool Charging)? reading;
            try
            {
                using var h = _hid.OpenFeatureOnly(info);
                reading = await ReadBatteryAsync(h, info.Pid, id, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { continue; }
            if (reading is not { } found) continue;

            seen.Add(info.Pid);
            var (name, kind) = NameAndKind(info);
            result.Add(new BatteryReading(id, name, kind, Plausible(id, found.Percent), found.Charging, true));
        }
        return result;
    }

    private static string PathHash(string path) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Regex.Replace(path.ToLowerInvariant(), @"&col\d+", "")))).ToLowerInvariant()[..6];

    private static (string Name, DeviceKind Kind) NameAndKind(HidInfo info)
    {
        var product = DeviceNaming.Sanitize(info.Product);
        if (RazerProtocol.Known.TryGetValue(info.Pid, out var known) && (product.Length == 0 || IsGeneric(info.Product)))
        {
            var name = DeviceNaming.Sanitize(known.Name);
            return (name.Length > 0 ? name : "Razer device", known.Name.Contains("blackshark", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Headset : DeviceKind.Mouse);
        }
        var deviceName = product.Length > 0 ? product : "Razer device";
        return (deviceName, KindFor(deviceName));
    }

    private static bool IsGeneric(string product) =>
        GenericProducts.Contains(string.Join(' ', (product ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

    private static DeviceKind KindFor(string name)
    {
        var n = name.ToLowerInvariant();
        if (new[] { "blackshark", "kraken", "barracuda", "headset", "nari", "hammerhead" }.Any(n.Contains)) return DeviceKind.Headset;
        if (new[] { "blackwidow", "huntsman", "ornata", "deathstalker", "keyboard" }.Any(n.Contains)) return DeviceKind.Keyboard;
        var kind = DeviceNaming.KindFromName(name);
        return kind == DeviceKind.Other ? DeviceKind.Mouse : kind;
    }

    /// <summary>
    /// Razer firmware sometimes answers 0% for a moment. A 0 right after a reading above zero is treated as no reading; a 0 is
    /// believed when nothing above zero was seen before.
    /// </summary>
    private int? Plausible(string id, int percent)
    {
        if (percent == 0 && _lastLevel.GetValueOrDefault(id) > 0) return null;
        _lastLevel[id] = percent;
        return percent;
    }

    private async Task<(int Percent, bool Charging)?> ReadBatteryAsync(IHidHandle h, int pid, string id, CancellationToken ct)
    {
        byte? cached = _working.TryGetValue(id, out var t) ? t : null;
        foreach (var txn in RazerProtocol.TransactionOrder(pid, cached))
        {
            var battery = await QueryAsync(h, txn, RazerProtocol.CmdGetBattery, ct).ConfigureAwait(false);
            if (battery is null || RazerProtocol.BatteryPercent(battery.Args) is not { } percent) continue;
            var charging = await QueryAsync(h, txn, RazerProtocol.CmdGetCharging, ct).ConfigureAwait(false);
            _working[id] = txn;
            return (percent, charging is not null && RazerProtocol.IsCharging(charging.Args));
        }
        _working.TryRemove(id, out _); // nothing answered: ask the whole list again next time
        return null;
    }

    private async Task<RazerResponse?> QueryAsync(IHidHandle h, byte transaction, byte command, CancellationToken ct)
    {
        var request = RazerProtocol.BuildRequest(transaction, RazerProtocol.ClassPower, command, 0x02);
        for (var attempt = 0; attempt < Retries; attempt++)
        {
            if (ct.IsCancellationRequested) return null;
            if (!await h.SetFeatureAsync(request, ct).ConfigureAwait(false)) return null;
            if (!await SettleAsync(ct).ConfigureAwait(false)) return null;
            var response = RazerProtocol.DecodeResponse(await h.GetFeatureAsync(0, ct).ConfigureAwait(false));
            if (response is { CrcOk: true, Status: RazerProtocol.StatusSuccess, CommandClass: RazerProtocol.ClassPower }
                && response.TransactionId == transaction && response.CommandId == command)
                return response;
            if (!await SettleAsync(ct).ConfigureAwait(false)) return null;
        }
        return null;
    }

    private async Task<bool> SettleAsync(CancellationToken ct)
    {
        if (_settle <= TimeSpan.Zero) return !ct.IsCancellationRequested;
        try { await Task.Delay(_settle, ct).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }
}

/// <summary>
/// Battery of the Razer BlackShark V2 headsets through their 2.4 GHz dongle (the ODESSA family): the V2 HyperSpeed (1532:0565) and
/// the V2 Pro 2023 (1532:0555). Both use 64-byte reports with id 2; only three read commands can be built: the wireless
/// connection, the battery and the charging state. Ported from taskbar_battery_widget/razer_odessa.py of
/// Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md).
/// <para>
/// The original can send one state-changing frame to wake a V2 Pro dongle that was plugged in cold and has not been started in the
/// current USB session (as Razer Synapse does). That is not ported: such a dongle that does not answer is skipped, and shows once
/// Synapse or the headset has started it. Not tested on real hardware.
/// </para>
/// </summary>
public sealed class RazerOdessaReader : IBatteryReader
{
    private const int MaxBusyRetries = 3;
    private const int PaReplyMs = 250;

    private readonly IHidSystem _hid;
    private readonly TimeSpan _hyperSpeedBudget;
    private readonly TimeSpan _paBudget;
    private readonly TimeSpan _busyPause;
    private int _transaction;

    public RazerOdessaReader(IHidSystem hid, TimeSpan? hyperSpeedBudget = null, TimeSpan? paBudget = null, TimeSpan? busyPause = null)
    {
        _hid = hid;
        _hyperSpeedBudget = hyperSpeedBudget ?? TimeSpan.FromSeconds(1.0);
        _paBudget = paBudget ?? TimeSpan.FromSeconds(1.2);
        _busyPause = busyPause ?? TimeSpan.FromMilliseconds(50);
    }

    /// <summary>True for a Razer dongle this reader knows.</summary>
    public static bool Supports(int vendorId, int productId) => vendorId == RazerProtocol.Vid && RazerProtocol.OdessaDongles.ContainsKey(productId);

    private static bool Matches(HidInfo i) =>
        i.InputReportLength == RazerProtocol.OdessaLength && i.OutputReportLength == RazerProtocol.OdessaLength
        && (RazerProtocol.IsPa(i.Pid)
            ? i.UsagePage == RazerProtocol.PaVendorPage && i.Usage == RazerProtocol.PaUsage
            : i.UsagePage == RazerProtocol.HyperSpeedVendorPage);

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(RazerProtocol.Vid); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        var done = new HashSet<int>();
        foreach (var info in infos.Where(i => RazerProtocol.OdessaDongles.ContainsKey(i.Pid)).OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(info.Pid) || !Matches(info)) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(RazerProtocol.IsPa(info.Pid) ? _paBudget : _hyperSpeedBudget);
                using var h = _hid.Open(info);
                var reading = await ReadOneAsync(info, h, budget.Token).ConfigureAwait(false);
                if (reading is null) continue;
                done.Add(info.Pid);
                result.Add(reading);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private async Task<BatteryReading?> ReadOneAsync(HidInfo info, IHidHandle h, CancellationToken ct)
    {
        var name = RazerProtocol.OdessaDongles[info.Pid];
        var id = $"razer:{info.Pid:x4}";
        var pa = RazerProtocol.IsPa(info.Pid);

        async Task<byte[]?> Ask(byte command, bool remote) =>
            pa ? await QueryPaAsync(h, command, ct).ConfigureAwait(false) : await QueryAsync(h, command, remote, ct).ConfigureAwait(false);

        if (await Ask(RazerProtocol.CmdConnection, false).ConfigureAwait(false) is not { Length: > 0 } connection) return null;
        if (connection[0] != 1) return new BatteryReading(id, name, DeviceKind.Headset, null, false, false); // the dongle is there, the headset is not

        var battery = await Ask(RazerProtocol.CmdBattery, true).ConfigureAwait(false);
        if (battery is null && pa) battery = await Ask(RazerProtocol.CmdBattery, true).ConfigureAwait(false); // one more try, as the original
        if (battery is not { Length: > 0 } || battery[0] > 100) return null;
        var charging = await Ask(RazerProtocol.CmdCharging, true).ConfigureAwait(false);
        return new BatteryReading(id, name, DeviceKind.Headset, battery[0], charging is { Length: > 0 } && charging[0] == 1, true);
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

    /// <summary>One HyperSpeed command, retried while the dongle says it is busy. The data bytes, or null.</summary>
    private async Task<byte[]?> QueryAsync(IHidHandle h, byte command, bool remote, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxBusyRetries; attempt++)
        {
            if (ct.IsCancellationRequested) return null;
            var transaction = NextTransaction();
            await h.DrainAsync().ConfigureAwait(false);
            if (!await WriteAsync(h, RazerProtocol.OdessaRequest(command, transaction, remote), ct).ConfigureAwait(false)) return null;

            (byte Status, byte[] Data)? reply = null;
            while (!ct.IsCancellationRequested && reply is null)
            {
                reply = RazerProtocol.ParseOdessaReply(await ReadAsync(h, TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false), command, transaction);
                if (reply is null) await Task.Yield();
            }
            if (reply is not { } r) return null;
            if (r.Status == RazerProtocol.StatusOk) return r.Data;
            if (!RazerProtocol.StatusBusy.Contains(r.Status)) return null;
            try { await Task.Delay(_busyPause, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private int NextTransaction() => Interlocked.Increment(ref _transaction) % 31 + 1;

    /// <summary>One 'PA' GET: it needs its reply within a single read, up to 8 reports of another kind skipped.</summary>
    private static async Task<byte[]?> QueryPaAsync(IHidHandle h, byte command, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;
        await h.DrainAsync().ConfigureAwait(false);
        if (!await WriteAsync(h, RazerProtocol.PaRequest(command), ct).ConfigureAwait(false)) return null;
        for (var i = 0; i < 8 && !ct.IsCancellationRequested; i++)
        {
            var raw = await ReadAsync(h, TimeSpan.FromMilliseconds(PaReplyMs), ct).ConfigureAwait(false);
            if (raw is null) return null;
            if (RazerProtocol.ParsePaReply(raw, command) is { } payload) return payload;
        }
        return null;
    }
}
