using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery;

/// <summary>
/// Battery of a PlayStation 4 DualShock 4 (both revisions) and of Sony's USB wireless adapter for it, read from the pad's
/// input report. Nothing is ever written. Ported from Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md),
/// <c>sony.py</c>. Not tested on real hardware.
/// <para>
/// By USB the status is in report 0x01 at index 30. By Bluetooth a pad starts out sending a basic report with no battery and
/// sends the extended report 0x11 (status at index 32) once the host reads its calibration feature report 0x02. That is a read
/// request, but it leaves the pad in extended mode until it reconnects, so Bluetooth is only read when the caller allows it.
/// </para>
/// </summary>
public sealed class DualShock4Reader : IBatteryReader
{
    public const int SonyVendorId = 0x054C;
    public const int AdapterPid = 0x0BA0;
    private static readonly HashSet<int> PadPids = [0x05C4, 0x09CC]; // DualShock 4, first and second revision

    private const int GamePadUsagePage = 0x01, GamePadUsage = 0x05;
    private const byte UsbInputReportId = 0x01, BluetoothInputReportId = 0x11;
    private const int UsbReportLength = 64;
    private const int UsbStatusIndex = 30, BluetoothStatusIndex = 32;  // include the leading report id; Bluetooth has two header bytes more
    private const byte CalibrationFeatureReport = 0x02;
    private const byte AdapterNoPad = 0x04;                            // second status byte: the adapter has no controller (upstream: uncertain)

    /// <summary>True for a vendor and product id this reader knows how to read.</summary>
    public static bool Supports(int vendorId, int productId) =>
        vendorId == SonyVendorId && (PadPids.Contains(productId) || productId == AdapterPid);

    private readonly IHidSystem _hid;
    private readonly TimeSpan _budget;
    private readonly Func<bool> _readBluetooth;

    /// <param name="readBluetooth">Asked on every read. False (the default) leaves a pad that is connected by Bluetooth alone.</param>
    public DualShock4Reader(IHidSystem hid, TimeSpan? budget = null, Func<bool>? readBluetooth = null)
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
        // A cable first: it needs no mode change.
        foreach (var info in infos.Where(Matches).OrderBy(IsBluetooth).ThenBy(i => i.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var overBluetooth = IsBluetooth(info);
            if (done.Contains(info.Pid) || (overBluetooth && !bluetooth)) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(_budget);
                using var h = _hid.Open(info);
                var reading = await ReadPadAsync(h, info.Pid, overBluetooth, budget.Token).ConfigureAwait(false);
                if (reading is not null) { done.Add(info.Pid); result.Add(reading); }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return result;
    }

    // Upstream's rule: a Bluetooth path, or an input report longer than the USB one, means Bluetooth; exactly 64 bytes means USB.
    private static bool IsBluetooth(HidInfo i) =>
        i.IsBluetooth || i.Path.Contains("bthenum", StringComparison.OrdinalIgnoreCase) || i.InputReportLength > UsbReportLength;

    private static bool Matches(HidInfo i) =>
        (PadPids.Contains(i.Pid) || i.Pid == AdapterPid) && i.UsagePage == GamePadUsagePage && i.Usage == GamePadUsage
        && (IsBluetooth(i) ? i.InputReportLength > BluetoothStatusIndex + 1 : i.InputReportLength == UsbReportLength);

    private static async Task<BatteryReading?> ReadPadAsync(IHidHandle h, int pid, bool bluetooth, CancellationToken ct)
    {
        if (!bluetooth)
        {
            for (var i = 0; i < 8; i++) // a few reports may be of another kind
            {
                var raw = await h.ReadAsync(ct).ConfigureAwait(false);
                if (raw is null) return null;
                if (Decode(raw, pid, bluetooth: false) is { } reading) return reading;
            }
            return null;
        }

        // Listen first: a pad that is already in extended mode needs no request. Only a pad that is sending something but
        // not the extended report is asked for its calibration report, once, and then listened to again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            for (var i = 0; i < 16; i++)
            {
                var raw = await h.ReadAsync(ct).ConfigureAwait(false);
                if (raw is null) return null;
                if (Decode(raw, pid, bluetooth: true) is { } reading) return reading;
            }
            if (attempt == 0)
            {
                if (await h.GetFeatureAsync(CalibrationFeatureReport, ct).ConfigureAwait(false) is null) return null;
                await h.DrainAsync().ConfigureAwait(false); // reports queued before the switch are still the basic kind
            }
        }
        return null;
    }

    internal static BatteryReading? Decode(byte[] report, int pid, bool bluetooth)
    {
        var (id, index) = bluetooth ? (BluetoothInputReportId, BluetoothStatusIndex) : (UsbInputReportId, UsbStatusIndex);
        if (report.Length <= index + 1 || report[0] != id) return null;

        var status = report[index];
        const string Name = "DualShock 4";
        var deviceId = $"dualshock4:{pid:x4}";

        // The adapter reports itself even with no controller paired to it; that is a pad that is off, not a pad at 0%.
        if (pid == AdapterPid && (report[index + 1] & AdapterNoPad) != 0)
            return new BatteryReading(deviceId, Name, DeviceKind.Gamepad, null, false, false);

        var level = status & 0x0F;
        if ((status & 0x10) != 0) // cable connected: 0 to 10 charging, 11 full
            return new BatteryReading(deviceId, Name, DeviceKind.Gamepad, level >= 10 ? 100 : level * 10 + 5, level <= 10, true);
        return new BatteryReading(deviceId, Name, DeviceKind.Gamepad, Math.Min(level * 10 + 5, 100), false, true);
    }
}
