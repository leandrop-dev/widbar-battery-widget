namespace Widgets.Providers.Battery.Razer;

/// <param name="TransactionId">The transaction id the device answers to, tried first.</param>
internal sealed record KnownRazer(string Name, byte TransactionId);

internal sealed record RazerResponse(byte Status, byte TransactionId, byte DataSize, byte CommandClass, byte CommandId, byte[] Args, bool CrcOk);

/// <summary>
/// Razer battery protocols: the standard 90-byte feature report, and the two 64-byte report protocols of the ODESSA headset
/// dongles. Ported from taskbar_battery_widget/razer.py and razer_odessa.py of Gabrielfch/taskbar-battery-widget (MIT, see
/// THIRD_PARTY_NOTICES.md); the first says its protocol facts come from public documentation (openrazer) in an independent
/// implementation, the second that it was verified against live hardware. Only the read queries below can be built; the one
/// state-changing wake frame of the original is deliberately not ported.
/// </summary>
internal static class RazerProtocol
{
    public const int Vid = 0x1532;

    // ---- the standard 90-byte feature report

    public const int ReportLength = 90, FeatureBufferLength = ReportLength + 1;     // plus report id 0
    public const byte ClassPower = 0x07, CmdGetBattery = 0x80, CmdGetCharging = 0x84, StatusSuccess = 0x02;
    public static readonly byte[] TransactionIds = [0x1F, 0x3F, 0xFF, 0x9F];
    public const int VendorPageMin = 0xFF00, GenericDesktop = 0x0001;
    public static readonly int[] GenericUsages = [0x0002, 0x0006];                  // mouse, keyboard: some devices take the report there

    /// <summary>Razer mouse mats (PIDs 0x0C00 to 0x0CFF) have no battery; some answer the query with 0% and would show up as a mouse.</summary>
    public static bool HasNoBattery(int pid) => pid is >= 0x0C00 and <= 0x0CFF;

    public static readonly Dictionary<int, KnownRazer> Known = new()
    {
        [0x00A4] = new("Razer Mouse Dock Pro", 0x1F),
        [0x00AA] = new("Razer Basilisk V3 Pro Wired", 0x1F),
        [0x00AB] = new("Razer Basilisk V3 Pro Wireless", 0x1F),
        [0x00B9] = new("Razer Basilisk V3 X HyperSpeed", 0x1F),
        [0x007C] = new("Razer DeathAdder V2 Pro Wired", 0x3F),
        [0x007D] = new("Razer DeathAdder V2 Pro Wireless", 0x3F),
        [0x009C] = new("Razer DeathAdder V2 X HyperSpeed", 0x1F),
        [0x00B3] = new("Razer HyperPolling Wireless Dongle", 0x1F),
        [0x00B6] = new("Razer DeathAdder V3 Pro Wired", 0x1F),
        [0x00B7] = new("Razer DeathAdder V3 Pro Wireless", 0x1F),
        [0x0083] = new("Razer Basilisk X HyperSpeed", 0x1F),
        [0x0086] = new("Razer Basilisk Ultimate", 0x1F),
        [0x0088] = new("Razer Basilisk Ultimate Dongle", 0x1F),
        [0x008F] = new("Razer Naga V2 Pro Wired", 0x1F),
        [0x0090] = new("Razer Naga V2 Pro Wireless", 0x1F),
        [0x00A5] = new("Razer Viper V2 Pro Wired", 0x1F),
        [0x00A6] = new("Razer Viper V2 Pro Wireless", 0x1F),
        [0x007B] = new("Razer Viper Ultimate Wired", 0x3F),
        [0x0078] = new("Razer Viper Ultimate Wireless", 0x3F),
        [0x007A] = new("Razer Viper Ultimate Dongle", 0x3F),
        [0x0528] = new("Razer BlackShark V2 Pro (RZ04-0322)", 0x3F),
        [0x00AF] = new("Razer Cobra Pro Wired", 0x1F),
        [0x00B0] = new("Razer Cobra Pro Wireless", 0x1F),
    };

    /// <summary>The device's own transaction id first when it is known, then the usual order.</summary>
    public static byte[] TransactionOrder(int pid, byte? preferred = null)
    {
        var first = preferred ?? (Known.TryGetValue(pid, out var known) ? known.TransactionId : (byte?)null);
        return first is { } f ? [f, .. TransactionIds.Where(t => t != f)] : TransactionIds;
    }

    /// <summary>XOR of bytes 2 to 87 of the 90-byte report.</summary>
    public static byte Crc(ReadOnlySpan<byte> report)
    {
        byte c = 0;
        for (var i = 2; i < 88; i++) c ^= report[i];
        return c;
    }

    /// <summary>The 91-byte buffer for a set-feature call: report id 0, then the 90-byte packet.</summary>
    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var packet = new byte[ReportLength];
        packet[1] = transactionId;
        packet[5] = dataSize;
        packet[6] = commandClass;
        packet[7] = commandId;
        packet[88] = Crc(packet);
        return [0, .. packet];
    }

    /// <summary>Parses what a get-feature returned: 91 bytes with the report id, or a bare 90.</summary>
    public static RazerResponse? DecodeResponse(byte[]? buffer)
    {
        if (buffer is null) return null;
        if (buffer.Length == FeatureBufferLength) buffer = buffer[1..];
        if (buffer.Length != ReportLength) return null;
        return new RazerResponse(buffer[0], buffer[1], buffer[5], buffer[6], buffer[7], buffer[8..88], Crc(buffer) == buffer[88]);
    }

    /// <summary>Get-battery reply: args[1] runs from 0 to 255.</summary>
    public static int? BatteryPercent(byte[] args) =>
        args.Length < 2 ? null : Math.Clamp((int)Math.Round(args[1] * 100 / 255.0), 0, 100);

    public static bool IsCharging(byte[] args) => args.Length >= 2 && args[1] == 1;

    // ---- ODESSA headset dongles: 64-byte reports, id 2

    public const int OdessaLength = 64;
    public const byte OdessaReportId = 2;
    private const byte TransactionOffset = 0x60;
    public const byte CmdConnection = 0x20, CmdBattery = 0x21, CmdCharging = 0x2A;
    public static readonly byte[] AllowedCommands = [CmdConnection, CmdBattery, CmdCharging];
    public const int HyperSpeedVendorPage = 0xFF14, PaVendorPage = 0xFF00, PaUsage = 0x0001;
    public const byte StatusOk = 2;
    public static readonly byte[] StatusBusy = [1, 3];

    /// <summary>The dongle's product id to its name. 0x0565 is the HyperSpeed protocol, 0x0555 the 'PA' one.</summary>
    public static readonly Dictionary<int, string> OdessaDongles = new()
    {
        [0x0555] = "Razer BlackShark V2 Pro",
        [0x0565] = "Razer BlackShark V2 HyperSpeed",
    };

    public static bool IsPa(int pid) => pid == 0x0555;

    /// <summary>HyperSpeed request for one of the three read commands; <paramref name="remote"/> addresses the headset rather than the dongle.</summary>
    public static byte[] OdessaRequest(byte command, int transaction, bool remote)
    {
        if (!AllowedCommands.Contains(command)) throw new ArgumentException($"command 0x{command:x2} is not allowed");
        var b = new byte[OdessaLength];
        b[0] = OdessaReportId;
        b[2] = (byte)(transaction + TransactionOffset);
        b[6] = 4;                                   // the command and its two zero bytes, plus one
        b[9] = remote ? (byte)0x80 : (byte)0x00;
        b[10] = command;
        byte x = 0;
        for (var i = 0; i < OdessaLength - 2; i++) x ^= b[i];
        b[OdessaLength - 2] = x;
        return b;
    }

    // Replies are checksummed like requests, but without the status byte (index 1).
    private static byte ReplyChecksum(byte[] b)
    {
        byte x = 0;
        for (var i = 0; i < OdessaLength - 2; i++)
            if (i != 1) x ^= b[i];
        return x;
    }

    /// <summary>The status and data when the buffer answers this command and transaction; null for any other report.</summary>
    public static (byte Status, byte[] Data)? ParseOdessaReply(byte[]? b, byte command, int transaction)
    {
        if (b is null || b.Length != OdessaLength || b[OdessaLength - 2] != ReplyChecksum(b)) return null;
        if (b[0] != OdessaReportId || b[2] != (byte)(transaction + TransactionOffset)) return null;
        if (b[10] != command || b[11] != 1) return null;
        int n = b[12];
        return 13 + n > b.Length ? null : (b[1], b[13..(13 + n)]);
    }

    // ---- the 'PA' dongle (0x0555): GET requests framed with 'PA', replies framed with 'PI'

    private const byte PaDirectionOut = 0x80, PaReplyType = 0x08, PaGet = 0x03;

    public static byte[] PaRequest(byte command)
    {
        if (!AllowedCommands.Contains(command)) throw new ArgumentException($"command 0x{command:x2} is not allowed");
        var b = new byte[OdessaLength];
        b[0] = OdessaReportId;
        b[1] = PaDirectionOut;
        b[2] = 0x08;
        b[5] = (byte)'P';
        b[6] = (byte)'A';
        b[7] = 0x08;
        b[9] = PaGet;
        b[10] = command;
        return b;
    }

    /// <summary>The payload when the buffer is a 'PI' GET reply to this command; null for other reports.</summary>
    public static byte[]? ParsePaReply(byte[]? b, byte command)
    {
        if (b is null || b.Length != OdessaLength) return null;
        if (b[0] != OdessaReportId || b[2] != (byte)'P' || b[3] != (byte)'I' || b[4] != PaReplyType) return null;
        var end = 2 + b[1];
        if (end is < 15 or > OdessaLength) return null;
        if (b[12] != command || b[13] != 1) return null;
        int n = b[14];
        return 15 + n > end ? null : b[15..(15 + n)];
    }
}
