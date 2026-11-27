using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery.Corsair;

internal enum CorsairFamily { Void, Xt, V2 }

/// <param name="Beta">The least certain protocol: read only when experimental readers are on.</param>
internal sealed record CorsairModel(CorsairFamily Family, string Name, bool Wired = false, bool Beta = false);

/// <summary>
/// Corsair wireless headset protocols, all read-only. Ported from taskbar_battery_widget/corsair.py of
/// Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md). Void: a two-byte output report and a five-byte input
/// report. Bragi (Virtuoso XT and SE): 64-byte reports, a GET of the battery and of the charge state. V2: the same framing on
/// a collection that may have no report ids, after a firmware and heartbeat query. Only GETs of four properties, to two targets,
/// can be built; nothing is ever set.
/// </summary>
internal static class CorsairProtocol
{
    public const int Vid = 0x1B1C;

    // ---- Void

    public const int VoidPage = 0xFFC5;
    public static readonly byte[] VoidRequestBytes = [0xC9, 0x64];
    public const byte VoidReplyId = 0x64, VoidMicFlag = 0x80;

    // Copied as the original lists them (the first entry is the vendor id, which the original has among the pids).
    private static readonly int[] VoidPids =
    [
        0x1B1C, 0x1B27, 0x0A14, 0x0A16, 0x0A17, 0x0A1D, 0x0A1A, 0x1B2A, 0x1B23, 0x1B29,
        0x0A55, 0x0A51, 0x0A52, 0x0A38, 0x0A4F, 0x0A2B, 0x0A75, 0x0A56,
    ];

    // ---- Bragi

    public const int BragiLength = 64, XtPage = 0xFF42;
    public const byte BragiOutId = 0x02, BragiInId = 0x01, TargetSelf = 0x08, TargetHeadset = 0x09, BragiGet = 0x02;
    public const byte PropBattery = 0x0F, PropCharge = 0x10, PropHeartbeat = 0x12, PropFirmware = 0x13, StatusOk = 0x00;
    public const int Charging = 1, BatteryMax = 1000;                               // tenths of a percent
    private static readonly byte[] AllowedProps = [PropBattery, PropCharge, PropHeartbeat, PropFirmware];
    private static readonly byte[] AllowedTargets = [TargetSelf, TargetHeadset];

    public static readonly Dictionary<int, CorsairModel> Models = BuildModels();

    private static Dictionary<int, CorsairModel> BuildModels()
    {
        var models = VoidPids.ToDictionary(pid => pid, _ => new CorsairModel(CorsairFamily.Void, "Corsair Void"));
        models[0x0A64] = new(CorsairFamily.Xt, "Corsair Virtuoso XT");
        models[0x0A62] = new(CorsairFamily.Xt, "Corsair Virtuoso XT", Wired: true);
        models[0x0A3E] = new(CorsairFamily.Xt, "Corsair Virtuoso SE");
        models[0x0A3D] = new(CorsairFamily.Xt, "Corsair Virtuoso SE", Wired: true);
        models[0x2A08] = new(CorsairFamily.V2, "Corsair Void Wireless V2", Beta: true);
        models[0x2A02] = new(CorsairFamily.V2, "Corsair Virtuoso Max Wireless", Beta: true);
        models[0x0A97] = new(CorsairFamily.V2, "Corsair HS80 Max Wireless", Beta: true);
        return models;
    }

    // ---- requests

    public static byte[] VoidRequest(int outputLength)
    {
        var frame = new byte[Math.Max(2, outputLength)];
        VoidRequestBytes.CopyTo(frame, 0);
        return frame;
    }

    /// <summary>True when the frame, with its zero padding removed, is exactly the Void battery request.</summary>
    public static bool IsVoidRequest(byte[] frame) => frame.AsSpan().TrimEnd((byte)0).SequenceEqual(VoidRequestBytes);

    /// <summary>The 64-byte <c>02 target GET prop 00...</c> query; refuses anything but a whitelisted GET.</summary>
    public static byte[] BragiRequest(byte target, byte prop, byte command = BragiGet)
    {
        if (command != BragiGet || !AllowedTargets.Contains(target) || !AllowedProps.Contains(prop))
            throw new InvalidOperationException("Request is not allowed.");
        var frame = new byte[BragiLength];
        frame[0] = BragiOutId;
        frame[1] = target;
        frame[2] = command;
        frame[3] = prop;
        return frame;
    }

    /// <summary>
    /// The query as written to the wire: report id 2 first, or a leading 0 when the collection has no report ids. Padded up to
    /// the output report length when that is larger.
    /// </summary>
    public static byte[] BragiFrame(byte target, byte prop, bool reportIds, int outputLength)
    {
        var frame = BragiRequest(target, prop);
        if (!reportIds) frame = [0, .. frame];
        return outputLength > frame.Length ? [.. frame, .. new byte[outputLength - frame.Length]] : frame;
    }

    /// <summary>True for a frame that is a whitelisted GET and nothing else.</summary>
    public static bool IsAllowedWire(byte[] frame, bool reportIds)
    {
        if (!reportIds)
        {
            if (frame.Length == 0 || frame[0] != 0) return false;
            frame = frame[1..];
        }
        if (frame.Length < BragiLength) return false;
        var body = frame.AsSpan().TrimEnd((byte)0);
        return body.Length == 4 && body[0] == BragiOutId && AllowedTargets.Contains(body[1]) && body[2] == BragiGet && AllowedProps.Contains(body[3]);
    }

    // ---- replies

    public enum VoidState { Offline, Ok, Charging }

    /// <summary>A Void battery report: its state and level; null for any other report.</summary>
    public static (VoidState State, int? Level, bool MicUp)? ParseVoidReply(byte[]? b)
    {
        if (b is null || b.Length < 5 || b[0] != VoidReplyId) return null;
        var status = b[4];
        if (status == 0) return (VoidState.Offline, null, false);
        if (status is not (1 or 2 or 4 or 5)) return null;
        var raw = b[2];
        var level = raw & ~VoidMicFlag & 0xFF;
        if (level > 100) return null;
        return (status is 4 or 5 ? VoidState.Charging : VoidState.Ok, level, (raw & VoidMicFlag) != 0);
    }

    /// <summary>Strips the leading 0 report id some collections prepend (<c>00 01 ...</c> becomes <c>01 ...</c>).</summary>
    public static byte[]? NormalizeBragi(byte[]? b)
    {
        if (b is null || b.Length == 0) return null;
        return b.Length >= 2 && b[0] == 0 && b[1] == BragiInId ? b[1..] : b;
    }

    /// <summary>The GET reply from <paramref name="target"/> (<c>01 src 02 status data...</c>); null for any other report.</summary>
    public static byte[]? MatchBragi(byte[]? raw, byte target, bool strictSource = true)
    {
        var b = NormalizeBragi(raw);
        if (b is null || b.Length < 8 || b[0] != BragiInId || b[2] != BragiGet) return null;
        if (strictSource && b[1] != (target == TargetSelf ? 0 : 1)) return null;
        return b;
    }

    public static int BragiValue(byte[] reply, int width = 4)
    {
        var value = 0;
        for (var i = 0; i < width && 4 + i < reply.Length; i++) value |= reply[4 + i] << (8 * i);
        return value;
    }

    // ---- collections

    public static bool CollectionOk(CorsairModel m, HidInfo i) => m.Family switch
    {
        CorsairFamily.Void => i.UsagePage == VoidPage && i.Usage == 1 && i.InputReportLength >= 5 && i.OutputReportLength >= 2,
        CorsairFamily.Xt => i.UsagePage == XtPage && i.Usage == 1 && i.InputReportLength >= BragiLength && i.OutputReportLength >= BragiLength,
        _ => i.UsagePage >= 0xFF00 && i.InputReportLength is BragiLength or BragiLength + 1 && i.OutputReportLength is BragiLength or BragiLength + 1,
    };
}
