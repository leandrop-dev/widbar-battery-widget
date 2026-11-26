using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of a PlayStation DualSense / DualSense Edge connected by USB. The pad streams its state continuously as
/// input report 0x01; nothing is ever written to it. The status byte layout (battery level in the low nibble, charge
/// state in the high nibble) follows the Linux hid-playstation driver. Bluetooth connections use a different report
/// and are not read.
/// </summary>
public sealed class DualSenseReader : IBatteryReader
{
    public const int SonyVendorId = 0x054C;
    private static readonly HashSet<int> Pids = [0x0CE6, 0x0DF2]; // DualSense, DualSense Edge

    private const int GamePadUsagePage = 0x01, GamePadUsage = 0x05;
    private const byte UsbInputReportId = 0x01;
    private const int StatusIndex = 53;                  // payload offset 52, plus the leading report id

    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;

    public DualSenseReader(IHidSystem hid, TimeSpan? budget = null)
    {
        _hid = hid;
        _budget = budget ?? TimeSpan.FromSeconds(1.5);
    }

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(SonyVendorId); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        var done = new HashSet<int>();
        foreach (var info in infos.Where(Matches).OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(info.Pid)) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_budget);
                using var h = _hid.Open(info);
                for (var i = 0; i < 8; i++) // a few reports may be of another kind
                {
                    var raw = await h.ReadAsync(budget.Token).ConfigureAwait(false);
                    if (raw is null) break;
                    if (Decode(raw, info.Pid) is { } reading) { done.Add(info.Pid); result.Add(reading); break; }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private static bool Matches(HidInfo i) =>
        Pids.Contains(i.Pid) && i.UsagePage == GamePadUsagePage && i.Usage == GamePadUsage && i.InputReportLength > StatusIndex;

    internal static BatteryReading? Decode(byte[] report, int pid)
    {
        if (report.Length <= StatusIndex || report[0] != UsbInputReportId) return null;

        var status = report[StatusIndex];
        var level = status & 0x0F;
        int? percent = (status >> 4) switch
        {
            0x0 or 0x1 => Math.Min(level * 10 + 5, 100), // discharging, charging
            0x2 => 100,                                  // full
            _ => null,                                   // error or temperature fault: no usable level
        };
        var name = pid == 0x0DF2 ? "DualSense Edge" : "DualSense";
        return new BatteryReading($"dualsense:{pid:x4}", name, DeviceKind.Gamepad, percent, (status >> 4) == 0x1, true);
    }
}
