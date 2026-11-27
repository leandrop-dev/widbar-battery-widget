using System.Text;
using System.Text.RegularExpressions;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery.Logitech;

/// <summary>Raised when a request is not on the read-only list, before anything is written.</summary>
internal sealed class HidppSafetyException(string message) : Exception(message);

/// <summary>One HID++ report as received. For an error, <see cref="FeatureIndex"/> is the one of the failed request.</summary>
internal sealed record HidppReply(
    byte ReportId, int DeviceIndex, int FeatureIndex, int Function, int SwId, byte[] Params, int? Error = null, string? ErrorKind = null);

/// <summary>The HID++ interface of one Logitech device or receiver: its short and long report collections.</summary>
internal sealed record HidppGroup(GroupKey Key, int Vid, int Pid, string Product, HidInfo? Short, HidInfo? Long);

/// <summary>What makes two HID collections the same physical interface (the &amp;colNN collections of one interface share it).</summary>
internal sealed record GroupKey(int Vid, int Pid, string Interface, string Parent);

/// <summary>
/// Logitech HID++ for battery reading: encoding, decoding and the read-only list of requests. Ported from
/// taskbar_battery_widget/hidpp.py of Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md). Only queries can
/// be built: the list below is checked before any request is written. Not tested on real hardware.
/// </summary>
internal static class Hidpp
{
    public const int LogitechVid = 0x046D;
    public const byte ShortId = 0x10, LongId = 0x11;
    public const int ShortLen = 7, LongLen = 20;
    public const int SwId = 0x0B;
    public const int HidppPage = 0xFF00, UsageShort = 0x0001, UsageLong = 0x0002;

    public const int FRoot = 0x0000, FDeviceName = 0x0005;
    public const int FBatteryStatus = 0x1000, FBatteryVoltage = 0x1001, FUnifiedBattery = 0x1004, FAdc = 0x1F20;
    public static readonly int[] BatteryFeatures = [FUnifiedBattery, FBatteryStatus, FBatteryVoltage, FAdc];

    /// <summary>Feature id to the functions this widget may ever send. Anything else is refused.</summary>
    private static readonly Dictionary<int, int[]> AllowedFunctions = new()
    {
        [FRoot] = [0, 1],            // getFeature, ping
        [FDeviceName] = [0, 1, 2],   // count, name chunk, type
        [FUnifiedBattery] = [1],     // get_status
        [FBatteryStatus] = [0],
        [FBatteryVoltage] = [0],
        [FAdc] = [0],
    };

    // HID++ 1.0: the only request ever sent is GET_REGISTER, for the two battery registers.
    public const byte Hidpp1GetRegister = 0x81;
    public const int Hidpp1RegBatteryStatus = 0x07, Hidpp1RegBatteryCharge = 0x0D;
    private static readonly int[] AllowedHidpp1Registers = [Hidpp1RegBatteryStatus, Hidpp1RegBatteryCharge];
    public const int Hidpp1ErrInvalidSubId = 0x01;   // what a HID++ 1.0 device answers to a HID++ 2.0 ping
    public const int MaxFeatureIndex = 0x7F;         // indexes from 0x80 collide with the error markers

    public static readonly HashSet<int> KnownReceivers =
    [
        0xC52B, 0xC532, 0xC534, 0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545,
        0xC547, 0xC548, 0xC54D, 0xC52F, 0xC531, 0xC70E, 0xC713,
    ];

    private static bool ValidDeviceIndex(int i) => i is >= 0 and <= 6 or 0xFF;

    // Li-ion discharge curve, millivolts to percent.
    private static readonly (int Mv, int Percent)[] VoltCurve =
        [(3500, 0), (3600, 8), (3700, 25), (3750, 40), (3800, 55), (3900, 70), (4000, 85), (4100, 95), (4200, 100)];

    // ---- encode and parse

    /// <summary>HID++ 2.0 request: report id, device index, feature index, function and software id, then the parameters.</summary>
    public static byte[] EncodeRequest(int deviceIndex, int featureIndex, int function, ReadOnlySpan<byte> prms, int swId = SwId, bool useLong = true)
    {
        var size = useLong ? LongLen : ShortLen;
        if (4 + prms.Length > size) throw new ArgumentException("HID++ params too long");
        var report = new byte[size];
        report[0] = useLong ? LongId : ShortId;
        report[1] = (byte)deviceIndex;
        report[2] = (byte)featureIndex;
        report[3] = (byte)(((function & 0x0F) << 4) | (swId & 0x0F));
        prms.CopyTo(report.AsSpan(4));
        return report;
    }

    public static HidppReply? ParseReport(byte[]? data)
    {
        if (data is null || data.Length < ShortLen || data[0] is not (ShortId or LongId)) return null;
        byte rid = data[0], dev = data[1], b2 = data[2], b3 = data[3], b4 = data[4];
        if (b2 == 0x8F) return new HidppReply(rid, dev, b3, b4 >> 4, b4 & 0x0F, data[6..], data[5], "hidpp1"); // [10 dev 8F sub addr err 00]
        if (b2 == 0xFF) return new HidppReply(rid, dev, b3, b4 >> 4, b4 & 0x0F, data[6..], data[5], "hidpp2"); // [11 dev FF feat fn|sw err]
        return new HidppReply(rid, dev, b2, b3 >> 4, b3 & 0x0F, data[4..]);
    }

    /// <summary>Checks a request against the read-only list, then encodes it.</summary>
    public static byte[] BuildGuardedRequest(int deviceIndex, int featureIndex, int featureId, int function,
        ReadOnlySpan<byte> prms, int swId = SwId, bool useLong = true)
    {
        if (!AllowedFunctions.TryGetValue(featureId, out var allowed) || !allowed.Contains(function))
            throw new HidppSafetyException($"HID++ feature 0x{featureId:X4} fn {function} is not on the read-only list");
        if (!ValidDeviceIndex(deviceIndex)) throw new HidppSafetyException($"bad HID++ device index {deviceIndex}");
        if (featureId == FRoot)
        {
            if (featureIndex != 0) throw new HidppSafetyException("root feature must use index 0");
            if (function == 0 && (prms.Length < 2 || !AllowedFunctions.ContainsKey((prms[0] << 8) | prms[1])))
                throw new HidppSafetyException("root getFeature for a feature that is not on the read-only list");
        }
        else if (featureIndex is < 1 or > MaxFeatureIndex)
        {
            throw new HidppSafetyException($"bad HID++ feature index {featureIndex}");
        }
        if (swId is < 1 or > 15) throw new HidppSafetyException("bad software id");
        return EncodeRequest(deviceIndex, featureIndex, function, prms, swId, useLong);
    }

    public static byte[] BuildHidpp1GetRegister(int deviceIndex, int register)
    {
        if (!AllowedHidpp1Registers.Contains(register)) throw new HidppSafetyException($"HID++ 1.0 register 0x{register:X2} is not on the read-only list");
        if (!ValidDeviceIndex(deviceIndex)) throw new HidppSafetyException($"bad HID++ device index {deviceIndex}");
        return [ShortId, (byte)deviceIndex, Hidpp1GetRegister, (byte)register, 0, 0, 0];
    }

    /// <summary>A GET_REGISTER answer, or its HID++ 1.0 error, echoes the sub id and the register address.</summary>
    public static bool Hidpp1ReplyMatches(HidppReply r, int deviceIndex, int register) =>
        r.ReportId == ShortId && r.DeviceIndex == deviceIndex && r.FeatureIndex == Hidpp1GetRegister
        && ((r.Function << 4) | r.SwId) == register;

    public static bool ReplyMatches(HidppReply r, int deviceIndex, int featureIndex, int function, int swId = SwId) =>
        r.DeviceIndex == deviceIndex && r.FeatureIndex == featureIndex && r.Function == function && r.SwId == swId;

    /// <summary>HID++ 1.0 error 0x08 (unknown device) or 0x09 (resource error: an empty slot) answering a ping.</summary>
    public static bool IsNotConnected(HidppReply r) => r.ErrorKind == "hidpp1" && r.Error is 0x08 or 0x09;

    // ---- decoding

    /// <summary>Root getFeature reply to the feature's index, or null when it is unsupported or not a valid index.</summary>
    public static int? DecodeGetFeature(byte[] p) => p.Length == 0 || p[0] == 0 || p[0] > MaxFeatureIndex ? null : p[0];

    public static (int Major, int Minor)? DecodePing(byte[] p) => p.Length < 2 ? null : (p[0], p[1]);

    public static int VoltageToPercent(int mv)
    {
        if (mv <= VoltCurve[0].Mv) return 0;
        if (mv >= VoltCurve[^1].Mv) return 100;
        for (var i = 0; i < VoltCurve.Length - 1; i++)
        {
            var (v0, p0) = VoltCurve[i];
            var (v1, p1) = VoltCurve[i + 1];
            if (mv >= v0 && mv <= v1) return (int)Math.Round(p0 + (p1 - p0) * (double)(mv - v0) / (v1 - v0));
        }
        return 0;
    }

    private static readonly (int Flag, int Percent)[] LevelFlagPercent = [(8, 100), (4, 55), (2, 15), (1, 5)]; // full, good, low, critical

    /// <summary>0x1004 fn1 get_status: state of charge, level flags, charging status, external power.</summary>
    public static (int Percent, bool Charging)? DecodeUnifiedBattery(byte[] p)
    {
        if (p.Length < 3) return null;
        int soc = p[0], flags = p[1], status = p[2];
        if (soc == 0)
            foreach (var (flag, percent) in LevelFlagPercent)
                if ((flags & flag) != 0) { soc = percent; break; }
        if (soc > 100) return null;
        return (soc, status is 1 or 2); // 0 discharging, 1 charging, 2 slow, 3 full, 4 error
    }

    /// <summary>0x1000 fn0: level, next level, status (1 recharging, 2 almost full, 4 slow).</summary>
    public static (int Percent, bool Charging)? DecodeBatteryStatus(byte[] p)
    {
        if (p.Length < 3) return null;
        int level = p[0], status = p[2];
        return level > 100 ? null : (level, status is 1 or 2 or 4);
    }

    /// <summary>0x1001 fn0: millivolts and flags, charging when bit 7 is set.</summary>
    public static (int Percent, bool Charging)? DecodeVoltage(byte[] p)
    {
        if (p.Length < 3) return null;
        var mv = (p[0] << 8) | p[1];
        return mv is < 2500 or > 5000 ? null : (VoltageToPercent(mv), (p[2] & 0x80) != 0);
    }

    /// <summary>0x1F20 fn0: millivolts and flags, charging when bit 0 is set.</summary>
    public static (int Percent, bool Charging)? DecodeAdc(byte[] p)
    {
        if (p.Length < 3) return null;
        var mv = (p[0] << 8) | p[1];
        return mv is < 2500 or > 5000 ? null : (VoltageToPercent(mv), (p[2] & 0x01) != 0);
    }

    /// <summary>Battery feature id to its function and its decoder.</summary>
    public static readonly Dictionary<int, (int Function, Func<byte[], (int Percent, bool Charging)?> Decode)> BatteryReaders = new()
    {
        [FUnifiedBattery] = (1, DecodeUnifiedBattery),
        [FBatteryStatus] = (0, DecodeBatteryStatus),
        [FBatteryVoltage] = (0, DecodeVoltage),
        [FAdc] = (0, DecodeAdc),
    };

    private static readonly Dictionary<int, int> Hidpp1LevelPercent = new() { [1] = 5, [3] = 20, [5] = 50, [7] = 90 }; // register 0x07: coarse levels

    /// <summary>HID++ 1.0 register 0x0D: charge percent, unknown, status (high nibble 0x30 discharging, 0x50 charging, 0x90 full).</summary>
    public static (int Percent, bool Charging)? DecodeHidpp1Charge(byte[] p) =>
        p.Length < 3 || p[0] > 100 ? null : (p[0], (p[2] & 0xF0) == 0x50);

    /// <summary>HID++ 1.0 register 0x07: coarse level code 1, 3, 5 or 7, and a state where 0x25 means full.</summary>
    public static (int Percent, bool Charging)? DecodeHidpp1Status(byte[] p)
    {
        if (p.Length < 2 || !Hidpp1LevelPercent.TryGetValue(p[0], out var percent)) return null;
        var state = p[1];
        if (state == 0x25) return (100, false);
        return (percent, (state & 0xF0) == 0x20 && (state & 0x0F) is >= 1 and <= 4);
    }

    // ---- name and kind

    public static int DecodeNameLength(byte[] p) => p.Length == 0 ? 0 : p[0];

    public static string AssembleName(IEnumerable<byte[]> chunks, int length)
    {
        var raw = chunks.SelectMany(c => c).Take(length).ToArray();
        var end = Array.IndexOf(raw, (byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? raw[..end] : raw).Trim();
    }

    /// <summary>The device type of feature 0x0005 fn2 as a kind. Remotes, speakers and microphones have none of ours.</summary>
    public static DeviceKind? KindFromDeviceType(int? code) => code switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 4 or 5 => DeviceKind.Mouse,
        8 => DeviceKind.Headset,
        10 or 11 or 12 => DeviceKind.Gamepad,
        1 or 6 or 14 or 15 => DeviceKind.Other,
        _ => null,
    };

    // ---- grouping

    private static readonly Regex ParentRe = new(@"#([0-9a-f]+&[0-9a-f]+)&\d+&", RegexOptions.IgnoreCase);
    private static readonly Regex InterfaceRe = new(@"&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase);
    private static readonly Regex ColRe = new(@"&col\d+", RegexOptions.IgnoreCase);

    public static GroupKey KeyOf(HidInfo info)
    {
        var parent = ParentRe.Match(info.Path);
        var mi = InterfaceRe.Match(info.Path);
        return new GroupKey(info.Vid, info.Pid, mi.Success ? mi.Groups[1].Value.ToLowerInvariant() : "",
            parent.Success ? parent.Groups[1].Value.ToLowerInvariant() : ColRe.Replace(info.Path, ""));
    }

    /// <summary>Groups the HID collections into HID++ interfaces, each with a short and/or a long report collection.</summary>
    public static List<HidppGroup> FindGroups(IEnumerable<HidInfo> infos)
    {
        var groups = new Dictionary<GroupKey, HidppGroup>();
        foreach (var i in infos)
        {
            if (i.Vid != LogitechVid || i.UsagePage != HidppPage) continue;
            bool isShort;
            if (i.Usage == UsageShort && i.OutputReportLength >= ShortLen) isShort = true;
            else if (i.Usage == UsageLong && i.OutputReportLength >= LongLen) isShort = false;
            else continue;

            var key = KeyOf(i);
            var g = groups.TryGetValue(key, out var existing) ? existing : new HidppGroup(key, i.Vid, i.Pid, i.Product, null, null);
            groups[key] = isShort ? g with { Short = i } : g with { Long = i };
        }
        return [.. groups.Values.Where(g => g.Short is not null || g.Long is not null)];
    }

    public static bool IsReceiver(HidppGroup g) =>
        g.Product.Contains("receiver", StringComparison.OrdinalIgnoreCase) || KnownReceivers.Contains(g.Pid);
}
