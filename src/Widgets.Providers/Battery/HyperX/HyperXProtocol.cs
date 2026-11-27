using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery.HyperX;

internal enum HyperXFamily { Cloud2, Cloud2K, Alpha, Alpha2 }

/// <param name="Beta">The least certain protocol: read only when experimental readers are on.</param>
internal sealed record HyperXModel(HyperXFamily Family, string Name, int Vid, int[] Pids, int Page, int Usage, int InLen, int OutLen, bool Beta = false);

/// <summary>
/// HyperX wireless headset protocols (USB dongle, HID). Ported from taskbar_battery_widget/hyperx.py of
/// Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md). The Cloud Flight reports its cell voltage; the other
/// models answer a level and a charge query. Every request is a fixed, read-only query from a table, and anything else is refused.
/// </summary>
internal static class HyperXProtocol
{
    public const int HyperXVid = 0x0951, HpVid = 0x03F0;
    public static readonly int[] Vendors = [HyperXVid, HpVid];

    // ---- Cloud Flight Wireless: the original reader. A cell voltage on a vendor collection of 20-byte reports.

    public static readonly int[] FlightPids = [0x16C4, 0x1723];
    public const int FlightPage = 0xFF53, FlightLength = 20;
    public const string FlightName = "HyperX Cloud Flight Wireless";
    private static readonly byte[] FlightHead = [0x21, 0xFF, 0x05];

    /// <summary>Above this the cell is being charged, and the percentage is unknown.</summary>
    public const int FlightChargingMv = 0x100B;

    // Knee points (millivolts, percent) of a single-cell Li-ion discharge curve, piecewise linear.
    private static readonly (int Mv, int Percent)[] FlightCurve = [(3300, 0), (3500, 5), (3650, 15), (3750, 35), (3850, 60), (3925, 85), (3975, 100)];

    public static byte[] FlightRequest() => [.. FlightHead, .. new byte[FlightLength - FlightHead.Length]];

    /// <summary>True only for the one request the Cloud Flight reader may send.</summary>
    public static bool IsFlightRequest(byte[] frame) => frame.SequenceEqual(FlightRequest());

    /// <summary>The voltage in mV when the report is a <c>21 FF 05 HI LO</c> answer; null otherwise.</summary>
    public static int? ParseFlightReply(byte[]? b) =>
        b is { Length: >= 5 } && b.AsSpan(0, 3).SequenceEqual(FlightHead) ? (b[3] << 8) | b[4] : null;

    public static int FlightPercent(int mv)
    {
        if (mv <= FlightCurve[0].Mv) return 0;
        if (mv >= FlightCurve[^1].Mv) return 100;
        var i = Array.FindIndex(FlightCurve, p => p.Mv > mv);                       // bisect right
        var (x0, y0) = FlightCurve[i - 1];
        var (x1, y1) = FlightCurve[i];
        return Math.Clamp((int)Math.Round(y0 + (y1 - y0) * (mv - x0) / (double)(x1 - x0)), 0, 100);
    }

    // ---- the other models

    public static readonly HyperXModel[] Models =
    [
        new(HyperXFamily.Cloud2, "HyperX Cloud II Wireless", HpVid, [0x0696, 0x018B], 0xFF90, 0x0303, 20, 52),
        new(HyperXFamily.Cloud2K, "HyperX Cloud II Wireless", HyperXVid, [0x1718], 0xFF00, 0x0001, 64, 62, Beta: true),
        new(HyperXFamily.Alpha, "HyperX Cloud Alpha Wireless", HpVid, [0x098D], 0xFF00, 0x0001, 31, 31, Beta: true),
        new(HyperXFamily.Alpha2, "HyperX Cloud Alpha 2 Wireless", HpVid, [0x08BE], 0xFF13, 0xFF00, 64, 64),
        new(HyperXFamily.Alpha2, "HyperX Cloud Flight 2 Wireless", HpVid, [0x0AC1], 0xFF90, 0xFF00, 64, 64),
    ];

    private static readonly byte[] KingstonHead = Convert.FromHexString("060002009A0000684A8E0A000000BB");

    public const byte CmdLevel = 0x02, CmdCharge = 0x03;                             // Cloud II (both revisions)
    public const byte AlphaConnection = 0x03, AlphaCharge = 0x0C, AlphaLevel = 0x0B;
    public static readonly byte[] Alpha2Connection = [0x52, 0x01], Alpha2Battery = [0x50, 0x02];

    public static int ProtocolLength(HyperXFamily family) => family switch
    {
        HyperXFamily.Cloud2 => 52,
        HyperXFamily.Cloud2K => 62,
        HyperXFamily.Alpha => 31,
        _ => 64,
    };

    private static byte[] Raw(HyperXFamily family, byte[] command)
    {
        var head = family switch
        {
            HyperXFamily.Cloud2 => new byte[] { 0x06, 0xFF, 0xBB, command[0] },
            HyperXFamily.Cloud2K => [.. KingstonHead, command[0]],
            HyperXFamily.Alpha => [0x21, 0xBB, command[0]],
            _ => command,
        };
        return [.. head, .. new byte[ProtocolLength(family) - head.Length]];
    }

    private static readonly HashSet<string> AllowedModelRequests =
    [
        .. new[] { HyperXFamily.Cloud2, HyperXFamily.Cloud2K }.SelectMany(f => new[] { CmdLevel, CmdCharge }.Select(c => Hex(Raw(f, [c])))),
        .. new[] { AlphaConnection, AlphaCharge, AlphaLevel }.Select(c => Hex(Raw(HyperXFamily.Alpha, [c]))),
        Hex(Raw(HyperXFamily.Alpha2, Alpha2Connection)),
        Hex(Raw(HyperXFamily.Alpha2, Alpha2Battery)),
    ];

    private static string Hex(byte[] b) => Convert.ToHexString(b);

    /// <summary>The output report for a whitelisted query; refuses anything else.</summary>
    public static byte[] ModelRequest(HyperXFamily family, params byte[] command)
    {
        byte[] request;
        try { request = Raw(family, command); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException) { throw new InvalidOperationException("Request is not allowed."); }
        if (!IsAllowedModelRequest(request)) throw new InvalidOperationException("Request is not allowed.");
        return request;
    }

    public static bool IsAllowedModelRequest(byte[] frame) => AllowedModelRequests.Contains(Hex(frame));

    /// <summary>The report when it answers <paramref name="command"/> for this family; null for any other or a short report.</summary>
    public static byte[]? MatchReply(HyperXFamily family, byte[]? b, params byte[] command)
    {
        if (b is null || b.Length < 8) return null;
        var ok = family switch
        {
            HyperXFamily.Cloud2 => b[0] == 0x06 && b[1] == 0xFF && b[2] == 0xBB && b[3] == command[0],
            HyperXFamily.Cloud2K => b[0] == 0x0B && b[2] == 0xBB && b[3] == command[0],
            HyperXFamily.Alpha => b[0] == 0x21 && b[1] == 0xBB && b[2] == command[0],
            _ => b[0] == command[0] + 1 && b[1] == command[1],
        };
        return ok ? b : null;
    }

    public static HyperXModel? ModelFor(HidInfo i) => Models.FirstOrDefault(m => i.Vid == m.Vid && m.Pids.Contains(i.Pid));

    public static bool CollectionOk(HyperXModel m, HidInfo i) =>
        i.UsagePage == m.Page && i.Usage == m.Usage && i.InputReportLength == m.InLen && i.OutputReportLength == m.OutLen;

    public static bool IsFlight(HidInfo i) => i.Vid == HyperXVid && FlightPids.Contains(i.Pid);

    public static bool FlightCollectionOk(HidInfo i) => i.UsagePage == FlightPage && i.InputReportLength == FlightLength && i.OutputReportLength == FlightLength;
}
