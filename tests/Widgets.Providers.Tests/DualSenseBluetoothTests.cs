using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Tests;

public class DualSenseBluetoothTests
{
    // The shapes seen on a real pad: the Bluetooth path spells the vendor "VID&0002054c" and carries the HID service id.
    private const string BluetoothPath = @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6#8&35058ce6&1&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string UsbPath = @"\\?\hid#vid_054c&pid_0ce6&mi_03#7&2c0e1a3b&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private static readonly HidInfo Bluetooth = new(BluetoothPath, 0x054C, 0x0CE6, 0x01, 0x05, 78, 0, "", 78);
    private static readonly HidInfo Usb = new(UsbPath, 0x054C, 0x0CE6, 0x01, 0x05, 64, 64);

    private static byte[] Basic()                                              // report 0x01, padded to the buffer: no battery in it
    {
        var r = new byte[78];
        r[0] = 0x01;
        return r;
    }
    private static byte[] Extended(byte status)
    {
        var r = new byte[78];
        r[0] = 0x31;
        r[54] = status;
        return r;
    }
    private static byte[] UsbReport(byte status)
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[53] = status;
        return r;
    }

    /// <summary>A pad that sends basic reports until it is asked for report 0x05, then extended ones.</summary>
    private sealed class PadHandle(bool alreadyExtended, byte status, bool featureWorks = true) : IHidHandle
    {
        private bool _extended = alreadyExtended;
        public List<byte> FeatureRequests { get; } = [];
        public bool Written { get; private set; }

        public Task<byte[]?> ReadAsync(CancellationToken ct) =>
            Task.FromResult<byte[]?>(_extended ? Extended(status) : Basic());

        public Task<byte[]?> GetFeatureAsync(byte reportId, CancellationToken ct)
        {
            FeatureRequests.Add(reportId);
            if (!featureWorks) return Task.FromResult<byte[]?>(null);
            _extended = true;
            return Task.FromResult<byte[]?>(new byte[41]);
        }

        public Task<bool> WriteAsync(byte[] report, CancellationToken ct) { Written = true; return Task.FromResult(true); }
        public Task DrainAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class PadSystem(IReadOnlyList<HidInfo> infos, params (string Path, IHidHandle Handle)[] handles) : IHidSystem
    {
        public List<string> Opened { get; } = [];
        public IReadOnlyList<HidInfo> Enumerate(int vendorId) => infos.Where(i => i.Vid == vendorId).ToList();
        public IHidHandle Open(HidInfo info)
        {
            Opened.Add(info.Path);
            return handles.First(h => h.Path == info.Path).Handle;
        }
    }

    private static DualSenseReader Reader(IHidSystem hid, bool bluetooth) => new(hid, readBluetooth: () => bluetooth);

    // ---- the path

    [Theory]
    [InlineData(BluetoothPath, true)]
    [InlineData(UsbPath, false)]
    [InlineData("p1", false)]
    [InlineData(@"\\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6#9&1&0000#{x}", true)] // HID over Bluetooth LE, any case
    public void IsBluetoothComesFromTheHidServiceInThePath(string path, bool expected) =>
        Assert.Equal(expected, new HidInfo(path, 0x054C, 0x0CE6, 1, 5, 78, 0).IsBluetooth);

    [Theory]
    [InlineData(UsbPath, 0x054C, true)]
    [InlineData(BluetoothPath, 0x054C, true)]
    [InlineData(@"\\?\hid#vid_1038&pid_22a1&mi_04#7&1&0000#{x}", 0x054C, false)]
    [InlineData(UsbPath, 0x1038, false)]
    public void ThePathFilterKnowsBothUsbAndBluetoothSpellings(string path, int vendor, bool expected) =>
        Assert.Equal(expected, WindowsHid.PathHasVendor(path, vendor));

    // ---- decoding

    [Theory]
    [InlineData(0x08, 85, false)]   // as read from a real pad: discharging, level 8
    [InlineData(0x00, 5, false)]
    [InlineData(0x15, 55, true)]    // charging
    [InlineData(0x2A, 100, false)]  // full
    public void DecodeBluetooth_ReadsTheStatusByteOneBeyondTheUsbOne(byte status, int percent, bool charging)
    {
        var r = DualSenseReader.DecodeBluetooth(Extended(status), 0x0CE6)!;
        Assert.Equal(percent, r.Percent);
        Assert.Equal(charging, r.Charging);
        Assert.Equal("dualsense:0ce6", r.Id); // the same device as over USB
    }

    [Fact]
    public void DecodeBluetooth_IgnoresTheBasicReportAndShortOnesAndFaults()
    {
        Assert.Null(DualSenseReader.DecodeBluetooth(Basic(), 0x0CE6));            // no battery in it
        Assert.Null(DualSenseReader.DecodeBluetooth(UsbReport(0x15), 0x0CE6));    // a USB-sized report
        Assert.Null(DualSenseReader.DecodeBluetooth(new byte[10], 0x0CE6));
        Assert.Null(DualSenseReader.DecodeBluetooth(Extended(0xF3), 0x0CE6)!.Percent);
    }

    // ---- the reader

    [Fact]
    public async Task ABluetoothPadIsLeftAloneUnlessTheSettingAllowsIt()
    {
        var pad = new PadHandle(alreadyExtended: false, 0x08);
        var system = new PadSystem([Bluetooth], (BluetoothPath, pad));

        Assert.Empty(await Reader(system, bluetooth: false).ReadAsync(default));
        Assert.Empty(system.Opened);            // not even opened
        Assert.Empty(pad.FeatureRequests);
        Assert.Empty(await new DualSenseReader(system).ReadAsync(default)); // and the default is off
    }

    [Fact]
    public async Task ABasicModePadIsAskedOnceForItsCalibrationReportThenRead()
    {
        var pad = new PadHandle(alreadyExtended: false, 0x08);
        var system = new PadSystem([Bluetooth], (BluetoothPath, pad));

        var r = Assert.Single(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Equal(85, r.Percent);
        Assert.Equal([(byte)0x05], pad.FeatureRequests);
        Assert.False(pad.Written);              // a feature request is a read: nothing is ever written
    }

    [Fact]
    public async Task APadAlreadyInExtendedModeIsNotAskedAnything()
    {
        var pad = new PadHandle(alreadyExtended: true, 0x15);
        var system = new PadSystem([Bluetooth], (BluetoothPath, pad));

        var r = Assert.Single(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Equal(55, r.Percent);
        Assert.True(r.Charging);
        Assert.Empty(pad.FeatureRequests);
    }

    [Fact]
    public async Task APadThatRefusesTheRequestGivesNoReadingAndNoError()
    {
        var pad = new PadHandle(alreadyExtended: false, 0x08, featureWorks: false);
        var system = new PadSystem([Bluetooth], (BluetoothPath, pad));

        Assert.Empty(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Single(pad.FeatureRequests);     // asked once, not in a loop
    }

    [Fact]
    public async Task ACableIsPreferredAndTheBluetoothLinkIsNotTouched()
    {
        var cable = new UsbHandle(UsbReport(0x2A));
        var bluetoothPad = new PadHandle(alreadyExtended: false, 0x08);
        var system = new PadSystem([Bluetooth, Usb], (UsbPath, cable), (BluetoothPath, bluetoothPad));

        var r = Assert.Single(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Equal(100, r.Percent);
        Assert.Equal([UsbPath], system.Opened);
        Assert.Empty(bluetoothPad.FeatureRequests);
    }

    [Fact]
    public async Task TheSettingIsReadOnEveryReadSoTurningItOnWorksWithoutARestart()
    {
        var pad = new PadHandle(alreadyExtended: false, 0x08);
        var system = new PadSystem([Bluetooth], (BluetoothPath, pad));
        var on = false;
        var reader = new DualSenseReader(system, readBluetooth: () => on);

        Assert.Empty(await reader.ReadAsync(default));
        on = true;
        Assert.Single(await reader.ReadAsync(default));
        on = false;
        Assert.Empty(await reader.ReadAsync(default));
    }

    private sealed class UsbHandle(byte[] report) : IHidHandle
    {
        public Task<byte[]?> ReadAsync(CancellationToken ct) => Task.FromResult<byte[]?>(report);
        public Task<bool> WriteAsync(byte[] r, CancellationToken ct) => throw new InvalidOperationException("never written");
        public Task DrainAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
