using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Widgets.Providers.Battery.Hid;

/// <summary>
/// HID access through hid.dll and cfgmgr32. Handles are opened with read and write sharing so the vendor's own
/// software keeps working. Only used to send read-only status queries.
/// </summary>
public sealed class WindowsHid : IHidSystem
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 3, OpenExisting = 3, FileFlagOverlapped = 0x40000000;
    private const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Guid32 { public uint A; public ushort B, C; public byte D0, D1, D2, D3, D4, D5, D6, D7; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes { public uint Size; public ushort VendorId, ProductId, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid32 guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HiddAttributes a);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out nint data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint data, ref HidpCaps caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder buf, uint length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, uint length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, uint length);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint size, ref Guid32 guid, string? id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid32 guid, string? id, char[] buffer, uint length, uint flags);

    public IReadOnlyList<HidInfo> Enumerate(int vendorId)
    {
        var result = new List<HidInfo>();
        try
        {
            foreach (var path in InterfacePaths())
            {
                // Cheap filter on the path first: opening every HID device just to read its vendor id is slow
                // and touches hardware that has nothing to do with us.
                if (!PathHasVendor(path, vendorId)) continue;
                if (Probe(path) is { } info && info.Vid == vendorId) result.Add(info);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException) { }
        return result;
    }

    public IReadOnlyList<HidInfo> EnumerateAll()
    {
        var result = new List<HidInfo>();
        try
        {
            foreach (var path in InterfacePaths())
                if (Probe(path) is { } info) result.Add(info);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException) { }
        return result;
    }

    public IHidHandle Open(HidInfo info) => new Handle(info);

    public IHidHandle OpenFeatureOnly(HidInfo info) => new FeatureHandle(info);

    // Both calls block, so they run off the caller's thread; the token only stops the wait, not the call.
    private static async Task<byte[]?> GetFeatureAsync(SafeFileHandle h, int length, byte reportId, CancellationToken ct)
    {
        if (length <= 0) return null;
        var buffer = new byte[length];
        buffer[0] = reportId;
        try
        {
            var ok = await Task.Run(() => HidD_GetFeature(h, buffer, (uint)buffer.Length), ct).WaitAsync(ct).ConfigureAwait(false);
            return ok ? buffer : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    private static async Task<bool> SetFeatureAsync(SafeFileHandle h, byte[] report, CancellationToken ct)
    {
        try { return await Task.Run(() => HidD_SetFeature(h, report, (uint)report.Length), ct).WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    /// <summary>
    /// An interface opened with no access: it answers feature reports only, which is all that mouse and keyboard collections allow.
    /// </summary>
    private sealed class FeatureHandle : IHidHandle
    {
        private readonly SafeFileHandle _handle;
        private readonly int _featureLen;

        public FeatureHandle(HidInfo info)
        {
            _handle = CreateFileW(info.Path, 0, FileShareReadWrite, 0, OpenExisting, 0, 0);
            if (_handle.IsInvalid) throw new IOException("Cannot open the HID interface.", Marshal.GetLastWin32Error());
            _featureLen = info.FeatureReportLength;
        }

        public Task<byte[]?> GetFeatureAsync(byte reportId, CancellationToken ct) => WindowsHid.GetFeatureAsync(_handle, _featureLen, reportId, ct);
        public Task<bool> SetFeatureAsync(byte[] report, CancellationToken ct) => WindowsHid.SetFeatureAsync(_handle, report, ct);
        public Task<bool> WriteAsync(byte[] report, CancellationToken ct) => Task.FromResult(false);
        public Task<byte[]?> ReadAsync(CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task DrainAsync() => Task.CompletedTask;
        public void Dispose() => _handle.Dispose();
    }

    /// <summary>
    /// Whether an interface path names the vendor. USB writes it as <c>vid_054c</c>; Bluetooth as <c>vid&amp;0002054c</c>, where
    /// 0002 says the id came from the USB vendor list (0001 would be the Bluetooth one).
    /// </summary>
    internal static bool PathHasVendor(string path, int vendorId) =>
        path.Contains($"vid_{vendorId:x4}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"vid&0002{vendorId:x4}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"vid&0001{vendorId:x4}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> InterfacePaths()
    {
        HidD_GetHidGuid(out var guid);
        if (CM_Get_Device_Interface_List_SizeW(out var size, ref guid, null, 0) != 0 || size <= 1) return [];
        var buffer = new char[size];
        if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, size, 0) != 0) return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static HidInfo? Probe(string path)
    {
        // Access 0 is enough to query attributes, and works even for collections Windows keeps for itself.
        using var h = CreateFileW(path, 0, FileShareReadWrite, 0, OpenExisting, 0, 0);
        if (h.IsInvalid) return null;

        var attr = new HiddAttributes { Size = (uint)Marshal.SizeOf<HiddAttributes>() };
        if (!HidD_GetAttributes(h, ref attr)) return null;

        int usagePage = 0, usage = 0, inLen = 0, outLen = 0, featureLen = 0;
        if (HidD_GetPreparsedData(h, out var pp))
        {
            try
            {
                var caps = new HidpCaps { Reserved = new ushort[17] };
                if (HidP_GetCaps(pp, ref caps) == HidpStatusSuccess)
                {
                    usagePage = caps.UsagePage; usage = caps.Usage;
                    inLen = caps.InputReportByteLength; outLen = caps.OutputReportByteLength;
                    featureLen = caps.FeatureReportByteLength;
                }
            }
            finally { HidD_FreePreparsedData(pp); }
        }

        var product = new StringBuilder(128);
        if (!HidD_GetProductString(h, product, 256)) product.Clear();
        return new HidInfo(path, attr.VendorId, attr.ProductId, usagePage, usage, inLen, outLen, product.ToString(), featureLen);
    }

    private sealed class Handle : IHidHandle
    {
        private readonly FileStream _stream;
        private readonly int _inLen;
        private readonly int _featureLen;

        public Handle(HidInfo info)
        {
            var h = CreateFileW(info.Path, GenericRead | GenericWrite, FileShareReadWrite, 0, OpenExisting, FileFlagOverlapped, 0);
            if (h.IsInvalid) throw new IOException("Cannot open the HID interface.", Marshal.GetLastWin32Error());
            _inLen = info.InputReportLength;
            _featureLen = info.FeatureReportLength;
            _stream = new FileStream(h, FileAccess.ReadWrite, 1, isAsync: true);
        }

        public Task<byte[]?> GetFeatureAsync(byte reportId, CancellationToken ct) =>
            WindowsHid.GetFeatureAsync(_stream.SafeFileHandle, _featureLen, reportId, ct);

        public Task<bool> SetFeatureAsync(byte[] report, CancellationToken ct) =>
            WindowsHid.SetFeatureAsync(_stream.SafeFileHandle, report, ct);

        public async Task<bool> WriteAsync(byte[] report, CancellationToken ct)
        {
            try { await _stream.WriteAsync(report, ct).ConfigureAwait(false); return true; }
            catch (OperationCanceledException) { return false; }
            catch (IOException) { return false; }
        }

        public async Task<byte[]?> ReadAsync(CancellationToken ct)
        {
            var buffer = new byte[_inLen];
            try
            {
                var n = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                return n > 0 ? buffer[..n] : null;
            }
            catch (OperationCanceledException) { return null; }
        }

        public async Task DrainAsync()
        {
            for (var i = 0; i < 64; i++)
            {
                using var quick = new CancellationTokenSource(TimeSpan.FromMilliseconds(15));
                if (await ReadAsync(quick.Token).ConfigureAwait(false) is null) break;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
