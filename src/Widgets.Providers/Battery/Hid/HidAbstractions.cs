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
    string Product = "");

/// <summary>An open connection to one HID interface. Reports include the report id as their first byte.</summary>
public interface IHidHandle : IDisposable
{
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
    IHidHandle Open(HidInfo info);
}
