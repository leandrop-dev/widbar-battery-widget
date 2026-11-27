namespace Widgets.Providers.Battery.Hid;

/// <summary>One HID interface (top-level collection) as Windows describes it.</summary>
public sealed record HidInfo(
    string Path,
    int Vid,
    int Pid,
    int UsagePage,
    int Usage,
    int InputReportLength,
    int OutputReportLength,
    string Product = "",
    int FeatureReportLength = 0)
{
    // HID over classic Bluetooth and over Bluetooth LE each have their own service id inside the interface path.
    private static readonly string[] BluetoothHidServices =
        ["{00001124-0000-1000-8000-00805f9b34fb}", "{00001812-0000-1000-8000-00805f9b34fb}"];

    /// <summary>True for an interface that Windows reached over Bluetooth rather than a cable or dongle.</summary>
    public bool IsBluetooth => BluetoothHidServices.Any(s => Path.Contains(s, StringComparison.OrdinalIgnoreCase));
}

/// <summary>An open connection to one HID interface. Reports include the report id as their first byte.</summary>
public interface IHidHandle : IDisposable
{
    /// <summary>
    /// Asks the device for a feature report (a read request: nothing is written). Null when it is not supported or failed.
    /// The result includes the report id as its first byte.
    /// </summary>
    Task<byte[]?> GetFeatureAsync(byte reportId, CancellationToken ct) => Task.FromResult<byte[]?>(null);

    /// <summary>
    /// Asks the device for its current input report by id, without waiting for it to send one (a read: nothing is written).
    /// Null when it is not supported or failed. The result includes the report id as its first byte.
    /// </summary>
    Task<byte[]?> GetInputReportAsync(byte reportId, int length, CancellationToken ct) => Task.FromResult<byte[]?>(null);

    /// <summary>
    /// Sends a feature report (the first byte is the report id). Used by devices that take a status request as a feature
    /// report and answer it in the next get. False on failure, on a cancelled wait, or when the handle cannot do it.
    /// </summary>
    Task<bool> SetFeatureAsync(byte[] report, CancellationToken ct) => Task.FromResult(false);

    /// <summary>Sends an output report. False on failure or when it did not complete within the token's lifetime.</summary>
    Task<bool> WriteAsync(byte[] report, CancellationToken ct);

    /// <summary>Waits for the next input report; null when the token fires first.</summary>
    Task<byte[]?> ReadAsync(CancellationToken ct);

    /// <summary>Throws away reports that are already queued, so the next read belongs to the next request.</summary>
    Task DrainAsync();
}

/// <summary>Enumerates and opens HID interfaces. Faked in tests.</summary>
public interface IHidSystem
{
    IReadOnlyList<HidInfo> Enumerate(int vendorId);

    /// <summary>Every HID interface on the PC, whatever the vendor. For the "request support for a device" report only.</summary>
    IReadOnlyList<HidInfo> EnumerateAll() => [];

    IHidHandle Open(HidInfo info);

    /// <summary>
    /// Opens an interface for feature reports only. Windows keeps mouse and keyboard collections to itself, refusing a read/write
    /// open, but still answers feature requests on a handle opened with no access. The handle cannot read or write input and
    /// output reports. Fakes with no such distinction use <see cref="Open"/>.
    /// </summary>
    IHidHandle OpenFeatureOnly(HidInfo info) => Open(info);
}
