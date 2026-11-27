namespace Widgets.Providers.Battery.Logitech;

internal enum HeadsetProtocol { Legacy, Centurion, Astro5 }

/// <param name="Collections">The usage page and usage of the collection to open.</param>
/// <param name="Sw">Legacy: the software id of the requests (1 to 15).</param>
/// <param name="Prefix">Centurion: the frame prefix.</param>
/// <param name="Experimental">The least certain protocols: read only when experimental readers are on.</param>
internal sealed record HeadsetModel(
    string Name, int[] Pids, HeadsetProtocol Protocol, (int Page, int Usage)[] Collections,
    int Sw = 0, (int Mv, int Percent)[]? Curve = null, byte[]? Prefix = null, bool Experimental = false);

/// <summary>
/// Logitech and Astro headset bases whose battery the generic HID++ reader cannot read. Ported from
/// taskbar_battery_widget/logitech_headsets.py of Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md). That file
/// says its protocol facts come from public notes (HeadsetControl, Solaar) and that it is an independent implementation with no
/// copied code or calibration table; the curves below are upstream's own. Only fixed read-only requests can be built. Not tested
/// on real hardware.
/// </summary>
internal static class HeadsetProtocols
{
    public const int LogitechVid = 0x046D;

    // Single-cell Li-ion discharge curves, one per hardware family (millivolts, percent).
    public static readonly (int Mv, int Percent)[] CurveG533 =
        [(3300, 0), (3450, 3), (3600, 9), (3690, 16), (3750, 27), (3810, 42), (3870, 57), (3960, 73), (4060, 88), (4170, 100)];
    public static readonly (int Mv, int Percent)[] CurveG535 =
        [(3300, 0), (3500, 4), (3640, 12), (3720, 24), (3790, 38), (3850, 52), (3930, 70), (4030, 86), (4140, 100)];
    public static readonly (int Mv, int Percent)[] CurveG633 =
        [(3200, 0), (3350, 4), (3500, 10), (3650, 22), (3750, 38), (3850, 58), (3950, 77), (4050, 91), (4150, 100)];
    public static readonly (int Mv, int Percent)[] CurveGPro =
        [(3300, 0), (3460, 3), (3620, 9), (3700, 17), (3760, 28), (3820, 43), (3880, 58), (3970, 74), (4070, 89), (4160, 100)];

    private static readonly (int, int)[] LegacyCollection = [(0xFF43, 0x0202)];

    public static readonly HeadsetModel[] Models =
    [
        new("Logitech G533", [0x0A66], HeadsetProtocol.Legacy, LegacyCollection, 0x01, CurveG533),
        new("Logitech G535", [0x0AC4], HeadsetProtocol.Legacy, [.. LegacyCollection, (0x000C, 0x0001)], 0x0D, CurveG535),
        new("Logitech G633/G733/G933/G935", [0x0A5C, 0x0A89, 0x0A5B, 0x0A87, 0x0AB5, 0x0AFE, 0x0B1F], HeadsetProtocol.Legacy, LegacyCollection, 0x0A, CurveG633),
        new("Logitech G PRO Headset", [0x0AA7, 0x0AAA, 0x0ABA, 0x0AFB, 0x0AFC], HeadsetProtocol.Legacy, LegacyCollection, 0x0D, CurveGPro),
        new("Logitech G PRO X 2 LIGHTSPEED", [0x0AF7], HeadsetProtocol.Centurion, [(0xFFA0, 0x0001)], Prefix: [0x51], Experimental: true),
        new("Logitech G PRO X 3 LIGHTSPEED", [0x0B45], HeadsetProtocol.Centurion, [(0xFFA0, 0x0001)], Prefix: [0x50, 0x23], Experimental: true),
        new("Logitech G522 LIGHTSPEED", [0x0B18], HeadsetProtocol.Centurion, [(0xFFA0, 0x0001)], Prefix: [0x50, 0x23], Experimental: true),
        new("Logitech ASTRO A50 Gen 5", [0x0B1C], HeadsetProtocol.Astro5, [(0xFF32, 0x0074)]),
    ];

    public static HeadsetModel? ForPid(int pid) => Models.FirstOrDefault(m => m.Pids.Contains(pid));

    /// <summary>Millivolts to percent on a curve: piecewise linear, clamped to 0 to 100.</summary>
    public static int Percent(int mv, (int Mv, int Percent)[] curve)
    {
        if (mv <= curve[0].Mv) return 0;
        if (mv >= curve[^1].Mv) return 100;
        var i = Array.FindIndex(curve, p => p.Mv > mv); // the first point above: bisect_right
        var (x0, y0) = curve[i - 1];
        var (x1, y1) = curve[i];
        return Math.Clamp((int)Math.Round(y0 + (y1 - y0) * (double)(mv - x0) / (x1 - x0)), 0, 100);
    }

    // ---- legacy HID++: a 20-byte HID++ 2.0 report to device index 0xFF asks for the cell voltage

    public const int LegacyLength = 20, LegacyDeviceIndex = 0xFF, LegacyChargingState = 0x03;
    public static readonly int[] LegacyFeatures = [Hidpp.FAdc, Hidpp.FBatteryVoltage];   // 0x1F20, 0x1001: the voltage getters

    public static byte[] LegacyLookup(int featureId, int sw)
    {
        if (!LegacyFeatures.Contains(featureId)) throw new HidppSafetyException("legacy HID++ feature is not allowed");
        return Hidpp.BuildGuardedRequest(LegacyDeviceIndex, 0, Hidpp.FRoot, 0, [(byte)(featureId >> 8), (byte)(featureId & 0xFF)], sw);
    }

    public static byte[] LegacyRequest(int featureIndex, int featureId, int sw)
    {
        if (!LegacyFeatures.Contains(featureId)) throw new HidppSafetyException("legacy HID++ feature is not allowed");
        return Hidpp.BuildGuardedRequest(LegacyDeviceIndex, featureIndex, featureId, 0, [], sw);
    }

    /// <summary>"ok" with the feature's index, "unsupported", or null when the report is unrelated or short.</summary>
    public static (string State, int Index)? ParseLegacyLookup(byte[]? buffer, int sw)
    {
        var reply = Hidpp.ParseReport(buffer);
        if (reply is null || !Hidpp.ReplyMatches(reply, LegacyDeviceIndex, 0, 0, sw)) return null;
        if (reply.Error is not null) return ("unsupported", 0);
        return Hidpp.DecodeGetFeature(reply.Params) is { } index ? ("ok", index) : ("unsupported", 0);
    }

    /// <summary>"ok" with the voltage and state, "off" for an error reply, or null when unrelated, short or invalid.</summary>
    public static (string State, int Millivolts, int ChargeState)? ParseLegacyReply(byte[]? buffer, int featureIndex, int sw)
    {
        var reply = Hidpp.ParseReport(buffer);
        if (reply is null || !Hidpp.ReplyMatches(reply, LegacyDeviceIndex, featureIndex, 0, sw)) return null;
        if (reply.Error is not null) return ("off", 0, 0);              // the headset is off or out of reach
        var p = reply.Params;
        if (p.Length < 3) return null;
        var mv = (p[0] << 8) | p[1];
        return mv is < 2500 or > 5000 ? null : ("ok", mv, p[2]);
    }

    // ---- Astro A50 Gen 5: vendor report 0x02, command 0x06

    public const int AstroLength = 64;
    public static readonly byte[] AstroRequest = Pad([0x02, 0x0C, 0x03, 0x00, 0x06, 0x0C], AstroLength);

    /// <summary>The battery from a reply (02 0C len 00 06 xx percent ? dock), else null.</summary>
    public static (int Percent, bool Charging)? ParseAstroReply(byte[]? b)
    {
        if (b is null || b.Length < 9 || b[0] != 0x02 || b[1] != 0x0C || b[4] != 0x06 || b[2] <= 3) return null;
        return b[6] > 100 ? null : (b[6], b[8] != 0);
    }

    // ---- Centurion: 64-byte frames with a model prefix, a bridge to the headset and a bridged battery feature

    // The bridge is feature 0x0003 and the headset's battery feature 0x0104; the fixed requests below carry both ids.
    public const int CenturionLength = 64, CenturionSw = 0x01;

    private static readonly byte[] BridgeLookupPayload = [0x00, 0x01, 0x00, 0x03];                         // Root.getFeature(bridge)
    private static readonly byte[] BatteryLookupTail = [0x11, 0x00, 0x05, 0x00, 0x00, 0x01, 0x01, 0x04];    // bridged Root.getFeature(battery)
    private static readonly byte[] BatteryGetHead = [0x11, 0x00, 0x03, 0x00];                               // bridged battery getter, then the index and the software id

    public static bool IsAllowedCenturionPayload(byte[] p)
    {
        if (p.AsSpan().SequenceEqual(BridgeLookupPayload)) return true;
        if (p.Length == 2 && p[0] is >= 1 and <= 0x7F && p[1] == CenturionSw) return true;                  // bridge status
        if (p.Length == 9 && p[0] is >= 1 and <= 0x7F && p.AsSpan(1).SequenceEqual(BatteryLookupTail)) return true;
        return p.Length == 7 && p[0] is >= 1 and <= 0x7F && p.AsSpan(1, 4).SequenceEqual(BatteryGetHead)
               && p[5] is >= 1 and <= 0x7F && p[6] == CenturionSw;                                          // bridged battery getter
    }

    public static byte[] CenturionFrame(byte[] prefix, byte[] payload)
    {
        if (!IsAllowedCenturionPayload(payload)) throw new HidppSafetyException("Centurion request is not allowed");
        return Pad([.. prefix, (byte)(payload.Length + 1), 0x00, .. payload], CenturionLength);
    }

    public static byte[] CenturionBridgeLookup(byte[] prefix) => CenturionFrame(prefix, BridgeLookupPayload);
    public static byte[] CenturionStatus(byte[] prefix, int bridge) => CenturionFrame(prefix, [(byte)bridge, CenturionSw]);
    public static byte[] CenturionBatteryLookup(byte[] prefix, int bridge) => CenturionFrame(prefix, [(byte)bridge, .. BatteryLookupTail]);
    public static byte[] CenturionBatteryGet(byte[] prefix, int bridge, int subIndex) =>
        CenturionFrame(prefix, [(byte)bridge, .. BatteryGetHead, (byte)subIndex, CenturionSw]);

    /// <summary>The payload of a frame that starts with the model's prefix, else null.</summary>
    public static byte[]? ExtractCenturionPayload(byte[]? buffer, byte[] prefix)
    {
        var n = prefix.Length;
        if (buffer is null || buffer.Length < n + 3 || !buffer.AsSpan(0, n).SequenceEqual(prefix)) return null;
        int length = buffer[n];
        if (length <= 1 || length + n + 1 > buffer.Length) return null;
        return buffer[(n + 2)..(n + 1 + length)];
    }

    public static (int Percent, bool Charging)? ParseCenturionBattery(byte[]? p) =>
        p is null || p.Length == 0 || p[0] > 100 ? null : (p[0], p.Length >= 3 && p[2] is 1 or 2);

    private static byte[] Pad(byte[] bytes, int length)
    {
        var padded = new byte[length];
        bytes.CopyTo(padded, 0);
        return padded;
    }
}
