using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery;

public enum DeviceKind { Headset, Mouse, Keyboard, Gamepad, Other }

/// <summary>Battery state of one device. <see cref="Percent"/> is null when the device is off or gave no usable reply.</summary>
/// <param name="Note">Short text shown instead of a percentage when the device is connected but has no battery level to report (for example "USB").</param>
public sealed record BatteryReading(string Id, string Name, DeviceKind Kind, int? Percent, bool Charging, bool Connected, string? Note = null);

/// <summary>
/// Battery of SteelSeries wireless headsets (Arctis 7+, Nova 3, Nova 5, Nova 7, Nova Pro Wireless and GameBuds) and the Rival 3 Wireless
/// mouse, asked over their USB dongle's vendor-defined HID interface. Protocols are ported from
/// Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md). Only the fixed read-only status requests
/// below are ever sent. Other SteelSeries families that the original marks "beta" are not ported yet.
/// </summary>
public sealed class SteelSeriesReader : IBatteryReader
{
    public const int VendorId = 0x1038;

    private const int VendorUsagePage = 0xFFC0, VendorUsage = 0x1;
    private const byte NovaStatus = 0xB0;
    private const int MaxUnrelatedReplies = 8;
    private const int Attempts = 3;

    // Requests start with the HID report id; zero padding up to the interface's output report length is implied.
    private static readonly byte[] ReqNovaStatus = [0x00, 0xB0];
    private static readonly byte[] ReqRival3 = [0x00, 0xAA, 0x01];
    private static readonly byte[] ReqNovaPro = [0x06, 0xB0]; // report id 6, then the status command

    private sealed record Family(
        IReadOnlyDictionary<int, string> Models,
        byte[] Request,
        int MinReplyLength,
        Func<byte[], int, Parsed?> Parse,
        byte? FirstByte = null,
        DeviceKind Kind = DeviceKind.Headset,
        Func<HidInfo, bool>? Accepts = null);

    private readonly record struct Parsed(int? Percent, bool Charging, bool Connected);

    private static int Clamp(double v) => (int)Math.Max(0, Math.Min(100, Math.Round(v, MidpointRounding.AwayFromZero)));
    private static int Scale(int raw, int lo, int hi) => Clamp((raw - lo) * 100.0 / (hi - lo));
    private static readonly Parsed Offline = new(null, false, false);

    // Nova 7 variants that report a coarse 0..4 level instead of a percentage.
    private static readonly HashSet<int> Nova7Discrete = [0x2202, 0x2206, 0x220A, 0x223A, 0x227A, 0x22A4];

    private static Parsed? ParseNova7Plus(byte[] d, int pid) =>
        d[1] == 0x01 ? Offline : new Parsed(Scale(d[2], 0, 4), d[3] == 0x01, true);

    private static Parsed? ParseNova3Or5(byte[] d, int pid) =>
        d[1] == 0x02 ? Offline : new Parsed(Clamp(d[3]), d[4] == 0x01, true);

    private static Parsed? ParseNova7(byte[] d, int pid)
    {
        if (d[3] == 0x00) return Offline;
        var pct = Nova7Discrete.Contains(pid) ? Scale(d[2], 0, 4) : Clamp(d[2]);
        return new Parsed(pct, d[3] is 0x01 or 0x02, true);
    }

    // Arctis Nova Pro Wireless (UNVERIFIED: ported from the reference project's beta reader, no device to test on).
    // Byte 6 is a 0..8 level, byte 15 is 1 when the headset is off and 2 while it charges.
    private static Parsed? ParseNovaPro(byte[] d, int pid) =>
        d[15] == 0x01 ? Offline : new Parsed(Scale(d[6], 0, 8), d[15] == 0x02, true);

    // The base station exposes several vendor-defined interfaces; the status one is interface 4 (path contains mi_04).
    private static bool NovaProInterface(HidInfo i) =>
        i.UsagePage >= 0xFF00 && i.Path.Contains("mi_04", StringComparison.OrdinalIgnoreCase);

    private static Parsed? ParseGameBuds(byte[] d, int pid)
    {
        int? lowest = null;
        foreach (var (state, level) in new[] { (3, 5), (4, 6) })
            if (d[state] == 0x03) lowest = Math.Min(lowest ?? 100, Math.Min(100, (int)d[level]));
        return lowest is { } v ? new Parsed(v, false, true) : Offline; // both buds in the case
    }

    // Level 0 is "no reading" (a sleeping mouse), never a 0 % alarm. The mouse takes replaceable batteries,
    // so the charging byte in the reference notes is ignored.
    private static Parsed? ParseRival3(byte[] d, int pid) =>
        d[0] is 0 or > 100 ? null : new Parsed(d[0], false, true);

    private static readonly Family[] Families =
    [
        new(new Dictionary<int, string>
        {
            [0x220E] = "SteelSeries Arctis 7+", [0x2212] = "SteelSeries Arctis 7+ PlayStation",
            [0x2216] = "SteelSeries Arctis 7+ Xbox", [0x2236] = "SteelSeries Arctis 7+ Destiny",
        }, ReqNovaStatus, 4, ParseNova7Plus, NovaStatus),
        new(new Dictionary<int, string> { [0x230A] = "SteelSeries Arctis GameBuds" },
            ReqNovaStatus, 7, ParseGameBuds, NovaStatus),
        new(new Dictionary<int, string>
        {
            [0x2269] = "SteelSeries Arctis Nova 3P Wireless", [0x226D] = "SteelSeries Arctis Nova 3X Wireless",
        }, ReqNovaStatus, 5, ParseNova3Or5, NovaStatus),
        new(new Dictionary<int, string>
        {
            [0x2232] = "SteelSeries Arctis Nova 5", [0x2253] = "SteelSeries Arctis Nova 5X",
        }, ReqNovaStatus, 16, ParseNova3Or5, NovaStatus),
        new(new Dictionary<int, string>
        {
            [0x2202] = "SteelSeries Arctis Nova 7", [0x22A1] = "SteelSeries Arctis Nova 7",
            [0x227E] = "SteelSeries Arctis Nova 7 Gen 2", [0x2206] = "SteelSeries Arctis Nova 7X",
            [0x2258] = "SteelSeries Arctis Nova 7X", [0x229E] = "SteelSeries Arctis Nova 7X",
            [0x22AD] = "SteelSeries Arctis Nova 7X", [0x22A4] = "SteelSeries Arctis Nova 7X",
            [0x22A5] = "SteelSeries Arctis Nova 7X", [0x223A] = "SteelSeries Arctis Nova 7 Diablo IV",
            [0x22A9] = "SteelSeries Arctis Nova 7 Diablo IV", [0x227A] = "SteelSeries Arctis Nova 7 WoW",
            [0x220A] = "SteelSeries Arctis Nova 7P", [0x22A7] = "SteelSeries Arctis Nova 7P",
            [0x2298] = "SteelSeries Arctis Nova 7P",
        }, ReqNovaStatus, 4, ParseNova7, NovaStatus),
        new(new Dictionary<int, string>
        {
            [0x12E0] = "SteelSeries Arctis Nova Pro Wireless", [0x12E5] = "SteelSeries Arctis Nova Pro Wireless X",
        }, ReqNovaPro, 16, ParseNovaPro, Accepts: NovaProInterface),
        new(new Dictionary<int, string> { [0x1830] = "SteelSeries Rival 3 Wireless" },
            ReqRival3, 3, ParseRival3, Kind: DeviceKind.Mouse),
    ];

    private static readonly Dictionary<int, Family> ByPid =
        Families.SelectMany(f => f.Models.Keys.Select(pid => (pid, f))).ToDictionary(x => x.pid, x => x.f);

    private readonly IHidSystem _hid;
    private readonly TimeSpan _perDevice;

    public SteelSeriesReader(IHidSystem hid, TimeSpan? perDeviceBudget = null)
    {
        _hid = hid;
        _perDevice = perDeviceBudget ?? TimeSpan.FromSeconds(1.5);
    }

    /// <summary>Reads every supported SteelSeries device. Never throws; a silent device is left out.</summary>
    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(VendorId); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        var done = new HashSet<int>();
        foreach (var info in infos.Where(i => ByPid.ContainsKey(i.Pid)).OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var family = ByPid[info.Pid];
            if (done.Contains(info.Pid) || !Matches(info, family)) continue;

            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_perDevice);
                if (await ReadOneAsync(info, family, budget.Token).ConfigureAwait(false) is { } reading)
                {
                    done.Add(info.Pid);
                    result.Add(reading);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private static bool Matches(HidInfo i, Family family) =>
        i is { InputReportLength: > 0, OutputReportLength: > 0 }
        && (family.Accepts?.Invoke(i) ?? (i.UsagePage == VendorUsagePage && i.Usage == VendorUsage));

    private async Task<BatteryReading?> ReadOneAsync(HidInfo info, Family family, CancellationToken ct)
    {
        using var h = _hid.Open(info);
        var data = await QueryAsync(h, family, info.OutputReportLength, _perDevice / Attempts, ct).ConfigureAwait(false);
        if (data is null || family.Parse(data, info.Pid) is not { } p) return null;

        var id = $"steelseries:{info.Pid:x4}";
        var name = family.Models[info.Pid];
        return new BatteryReading(id, name, family.Kind, p.Connected ? p.Percent : null, p.Connected && p.Charging, p.Connected);
    }

    /// <summary>
    /// Sends the request and waits for its reply. A dongle sometimes drops a request (the Rival 3 answered in about
    /// 110 ms on 8 of 10 reads and not at all on the other 2), so the request is repeated a few times, each attempt
    /// getting an equal share of the device's budget.
    /// </summary>
    private static async Task<byte[]?> QueryAsync(IHidHandle h, Family family, int outLen, TimeSpan attemptBudget, CancellationToken ct)
    {
        if (outLen < family.Request.Length) return null;
        var report = new byte[outLen];
        family.Request.CopyTo(report, 0);

        for (var attempt = 0; attempt < Attempts && !ct.IsCancellationRequested; attempt++)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(attemptBudget);

            await h.DrainAsync().ConfigureAwait(false);
            if (!await h.WriteAsync(report, window.Token).ConfigureAwait(false)) continue;

            for (var unrelated = 0; unrelated < MaxUnrelatedReplies; unrelated++)
            {
                var raw = await h.ReadAsync(window.Token).ConfigureAwait(false);
                if (raw is null) break; // this attempt's share is used up: ask again
                var data = Payload(raw);
                if (data.Length >= family.MinReplyLength && (family.FirstByte is null || data[0] == family.FirstByte))
                    return data;
            }
        }
        return null;
    }

    /// <summary>Windows prefixes input reports with their report id; an unnumbered report's leading 0 is dropped.</summary>
    internal static byte[] Payload(byte[] raw) => raw.Length > 0 && raw[0] == 0 ? raw[1..] : raw;
}
