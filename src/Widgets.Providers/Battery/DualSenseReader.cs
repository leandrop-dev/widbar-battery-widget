using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of a PlayStation DualSense / DualSense Edge. The pad streams its state continuously as an input report; the
/// status byte (battery level in the low nibble, charge state in the high nibble) follows the Linux hid-playstation
/// driver. Nothing is ever written to it.
/// <para>
/// By USB the status is in report 0x01. By Bluetooth the pad starts in a basic mode whose reports carry no battery; it
/// moves to the extended report 0x31, which does, once the host asks for its calibration feature report (0x05). That is a
/// read request, but it leaves the pad in extended mode until it reconnects, so Bluetooth is only read when the caller
/// allows it.
/// </para>
/// </summary>
public sealed class DualSenseReader : IBatteryReader
{
    public const int SonyVendorId = 0x054C;
    private static readonly HashSet<int> Pids = [0x0CE6, 0x0DF2]; // DualSense, DualSense Edge

    private const int GamePadUsagePage = 0x01, GamePadUsage = 0x05;
    private const byte UsbInputReportId = 0x01;
    private const int StatusIndex = 53;                  // payload offset 52, plus the leading report id

    private const byte BluetoothInputReportId = 0x31;
    private const int BluetoothStatusIndex = 54;         // the same payload, one byte later: the Bluetooth report has a sequence byte
    private const byte CalibrationFeatureReport = 0x05;

    /// <summary>True for a vendor and product id this reader knows how to read.</summary>
    public static bool Supports(int vendorId, int productId) => vendorId == SonyVendorId && Pids.Contains(productId);

    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;
    private readonly Func<bool> _readBluetooth;

    /// <param name="readBluetooth">Asked on every read. False (the default) leaves a pad that is connected by Bluetooth alone.</param>
    public DualSenseReader(IHidSystem hid, TimeSpan? budget = null, Func<bool>? readBluetooth = null)
    {
        _hid = hid;
        _budget = budget ?? TimeSpan.FromSeconds(1.5);
        _readBluetooth = readBluetooth ?? (() => false);
    }

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        IReadOnlyList<HidInfo> infos;
        try { infos = _hid.Enumerate(SonyVendorId); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return result; }

        var bluetooth = _readBluetooth();
        var done = new HashSet<int>();
        // A cable first: it needs no mode change. Paths sort the rest into a steady order.
        foreach (var info in infos.Where(Matches).OrderBy(i => i.IsBluetooth).ThenBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(info.Pid) || (info.IsBluetooth && !bluetooth)) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_budget);
                using var h = _hid.Open(info);
                var reading = info.IsBluetooth
                    ? await ReadBluetoothAsync(h, info.Pid, budget.Token).ConfigureAwait(false)
                    : await ReadUsbAsync(h, info.Pid, budget.Token).ConfigureAwait(false);
                if (reading is not null) { done.Add(info.Pid); result.Add(reading); }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    private static bool Matches(HidInfo i) =>
        Pids.Contains(i.Pid) && i.UsagePage == GamePadUsagePage && i.Usage == GamePadUsage
        && i.InputReportLength > (i.IsBluetooth ? BluetoothStatusIndex : StatusIndex);

    private static async Task<BatteryReading?> ReadUsbAsync(IHidHandle h, int pid, CancellationToken ct)
    {
        for (var i = 0; i < 8; i++) // a few reports may be of another kind
        {
            var raw = await h.ReadAsync(ct).ConfigureAwait(false);
            if (raw is null) return null;
            if (Decode(raw, pid) is { } reading) return reading;
        }
        return null;
    }

    private static async Task<BatteryReading?> ReadBluetoothAsync(IHidHandle h, int pid, CancellationToken ct)
    {
        // Listen first: a pad that is already in extended mode needs no request. Only a pad still sending the basic
        // report is asked for its calibration report, once, and then listened to again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            for (var i = 0; i < 16; i++)
            {
                var raw = await h.ReadAsync(ct).ConfigureAwait(false);
                if (raw is null) return null;
                if (DecodeBluetooth(raw, pid) is { } reading) return reading;
            }
            if (attempt == 0)
            {
                if (await h.GetFeatureAsync(CalibrationFeatureReport, ct).ConfigureAwait(false) is null) return null;
                await h.DrainAsync().ConfigureAwait(false); // reports queued before the switch are still the basic kind
            }
        }
        return null;
    }

    internal static BatteryReading? Decode(byte[] report, int pid) =>
        report.Length <= StatusIndex || report[0] != UsbInputReportId ? null : FromStatus(report[StatusIndex], pid);

    internal static BatteryReading? DecodeBluetooth(byte[] report, int pid) =>
        report.Length <= BluetoothStatusIndex || report[0] != BluetoothInputReportId ? null : FromStatus(report[BluetoothStatusIndex], pid);

    private static BatteryReading FromStatus(byte status, int pid)
    {
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
