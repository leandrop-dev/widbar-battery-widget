using System.Runtime.InteropServices;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of Xbox controllers through XInput. Windows reports only four coarse levels, so the percentage is
/// approximate (5, 30, 65 or 100). A wired pad has no battery: it is reported as connected with the note "USB".
/// </summary>
public sealed class XInputReader : IBatteryReader
{
    private const byte GamepadDevType = 0;
    private const byte BatteryDisconnected = 0, BatteryWired = 1, BatteryAlkaline = 2, BatteryNiMh = 3;
    private const int MaxUsers = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation { public byte Type; public byte Level; }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(uint user, byte devType, out BatteryInformation info);

    private readonly Func<int, (byte Type, byte Level)?> _query;

    public XInputReader() : this(QuerySlot) { }

    /// <summary>For tests: supplies the (battery type, level) of each user slot, or null for an empty slot.</summary>
    public XInputReader(Func<int, (byte Type, byte Level)?> query) => _query = query;

    private static (byte, byte)? QuerySlot(int slot)
    {
        try
        {
            return XInputGetBatteryInformation((uint)slot, GamepadDevType, out var info) == 0 ? (info.Type, info.Level) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        for (var slot = 0; slot < MaxUsers; slot++)
        {
            if (_query(slot) is not { } q) continue;
            var name = slot == 0 ? "Xbox Controller" : $"Xbox Controller {slot + 1}";
            if (Decode(q.Type, q.Level) is { } percent)
                result.Add(new BatteryReading($"xinput:{slot}", name, DeviceKind.Gamepad, percent, false, true));
            else if (q.Type == BatteryWired) // plugged in, no battery: still a connected pad, so it can replace an older one
                result.Add(new BatteryReading($"xinput:{slot}", name, DeviceKind.Gamepad, null, false, true, Note: "USB"));
        }
        return Task.FromResult<IReadOnlyList<BatteryReading>>(result);
    }

    /// <summary>Approximate percentage, or null for a wired, disconnected or unknown-battery pad.</summary>
    internal static int? Decode(byte type, byte level) =>
        type is BatteryAlkaline or BatteryNiMh
            ? level switch { 0 => 5, 1 => 30, 2 => 65, 3 => 100, _ => null }
            : null;
}
