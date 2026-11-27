using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Tests;

public class DualShock4Tests
{
    private const string UsbPath = @"\\?\hid#vid_054c&pid_09cc&col01#7&1&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string BluetoothPath = @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc#8&1&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string AdapterPath = @"\\?\hid#vid_054c&pid_0ba0&mi_03#7&1&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private static HidInfo Info(string path, int pid, int inputLength) => new(path, 0x054C, pid, 0x01, 0x05, inputLength, 0, "", 78);

    private static byte[] Usb(byte status, byte status1 = 0)
    {
        var r = new byte[64];
        r[0] = 0x01; r[30] = status; r[31] = status1;
        return r;
    }

    private static byte[] Extended(byte status, byte status1 = 0)
    {
        var r = new byte[78];
        r[0] = 0x11; r[32] = status; r[33] = status1;
        return r;
    }

    private static byte[] Basic() { var r = new byte[78]; r[0] = 0x01; return r; }

    private static DualShock4Reader Reader(IHidSystem hid, bool bluetooth = false) => new(hid, readBluetooth: () => bluetooth);

    // ---- decoding (upstream decode_ds4_status)

    [Theory]
    [InlineData(0x00, 5, false)]   // discharging, level 0
    [InlineData(0x05, 55, false)]
    [InlineData(0x09, 95, false)]
    [InlineData(0x0A, 100, false)] // 10 * 10 + 5 would pass 100
    [InlineData(0x15, 55, true)]   // cable connected: charging
    [InlineData(0x19, 95, true)]
    [InlineData(0x1A, 100, true)]  // level 10 on the cable: still charging, shown full
    [InlineData(0x1B, 100, false)] // level 11 on the cable: full
    public void TheStatusByteIsDecodedLikeUpstream(byte status, int percent, bool charging)
    {
        var usb = DualShock4Reader.Decode(Usb(status), 0x09CC, bluetooth: false)!;
        var bt = DualShock4Reader.Decode(Extended(status), 0x09CC, bluetooth: true)!;
        foreach (var r in new[] { usb, bt })
        {
            Assert.Equal(percent, r.Percent);
            Assert.Equal(charging, r.Charging);
            Assert.True(r.Connected);
            Assert.Equal("dualshock4:09cc", r.Id);
            Assert.Equal(DeviceKind.Gamepad, r.Kind);
        }
    }

    [Fact]
    public void TheWrongReportKindOrALengthTooShortGivesNothing()
    {
        Assert.Null(DualShock4Reader.Decode(Basic(), 0x09CC, bluetooth: true));            // basic Bluetooth report: no battery
        Assert.Null(DualShock4Reader.Decode(Extended(0x05), 0x09CC, bluetooth: false));    // a 0x11 report is not a USB one
        Assert.Null(DualShock4Reader.Decode(Usb(0x05), 0x09CC, bluetooth: true));
        Assert.Null(DualShock4Reader.Decode(new byte[31], 0x09CC, bluetooth: false));      // no room for the second status byte
    }

    [Fact]
    public void TheAdapterWithNoPadReportsADisconnectedDeviceNotZeroPercent()
    {
        var none = DualShock4Reader.Decode(Usb(0x05, status1: 0x04), 0x0BA0, bluetooth: false)!;
        Assert.False(none.Connected);
        Assert.Null(none.Percent);

        var some = DualShock4Reader.Decode(Usb(0x05, status1: 0x00), 0x0BA0, bluetooth: false)!;
        Assert.True(some.Connected);
        Assert.Equal(55, some.Percent);

        // The bit means "no pad" on the adapter only; on a pad itself it is something else and is ignored.
        Assert.True(DualShock4Reader.Decode(Usb(0x05, status1: 0x04), 0x09CC, bluetooth: false)!.Connected);
    }

    // ---- the registry

    [Theory]
    [InlineData(0x054C, 0x05C4, true)]
    [InlineData(0x054C, 0x09CC, true)]
    [InlineData(0x054C, 0x0BA0, true)]
    [InlineData(0x054C, 0x0CE6, true)]   // a DualSense, read by the other Sony reader
    [InlineData(0x054C, 0x0268, false)]  // a PlayStation 3 pad
    [InlineData(0x1038, 0x09CC, false)]
    public void TheRegistryKnowsTheDualShock4(int vid, int pid, bool expected) =>
        Assert.Equal(expected, SupportedDevices.Supports(vid, pid));

    // ---- the reader

    [Fact]
    public async Task APadOnACableIsReadWithoutAnyRequest()
    {
        var pad = new RecordingHandle(read: () => Usb(0x15));
        var system = new RecordingHidSystem([Info(UsbPath, 0x09CC, 64)], (UsbPath, pad));

        var r = Assert.Single(await Reader(system).ReadAsync(default));
        Assert.Equal(55, r.Percent);
        Assert.True(r.Charging);
        Assert.Empty(pad.Written);
        Assert.Empty(pad.SetFeatures);
        Assert.Empty(pad.FeatureRequests);
    }

    [Fact]
    public async Task ABluetoothPadIsLeftAloneUnlessTheSettingAllowsIt()
    {
        var pad = new RecordingHandle(read: () => Basic());
        var system = new RecordingHidSystem([Info(BluetoothPath, 0x09CC, 78)], (BluetoothPath, pad));

        Assert.Empty(await Reader(system, bluetooth: false).ReadAsync(default));
        Assert.Empty(await new DualShock4Reader(system).ReadAsync(default)); // the default is off
        Assert.Empty(system.Opened);
    }

    [Fact]
    public async Task ABasicModeBluetoothPadIsAskedOnceForFeatureReport02ThenRead()
    {
        var extended = false;
        var pad = new RecordingHandle(
            read: () => extended ? Extended(0x08) : Basic(),
            onGetFeature: _ => { extended = true; return new byte[37]; });
        var system = new RecordingHidSystem([Info(BluetoothPath, 0x09CC, 78)], (BluetoothPath, pad));

        var r = Assert.Single(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Equal(85, r.Percent);
        Assert.Equal([(byte)0x02], pad.FeatureRequests);
        Assert.Equal(1, pad.Drains);
        Assert.Empty(pad.Written);
        Assert.Empty(pad.SetFeatures);
    }

    [Fact]
    public async Task APadAlreadyInExtendedModeIsNotAskedAnything()
    {
        var pad = new RecordingHandle(read: () => Extended(0x1B));
        var system = new RecordingHidSystem([Info(BluetoothPath, 0x09CC, 78)], (BluetoothPath, pad));

        var r = Assert.Single(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Equal(100, r.Percent);
        Assert.Empty(pad.FeatureRequests);
    }

    [Fact]
    public async Task APadThatRefusesTheRequestOrSendsNothingGivesNoReadingAndNoError()
    {
        var refuses = new RecordingHandle(read: () => Basic(), onGetFeature: _ => null);
        var silent = new RecordingHandle(read: () => null);

        var a = new RecordingHidSystem([Info(BluetoothPath, 0x09CC, 78)], (BluetoothPath, refuses));
        Assert.Empty(await Reader(a, bluetooth: true).ReadAsync(default));
        Assert.Single(refuses.FeatureRequests);          // asked once, not in a loop

        var b = new RecordingHidSystem([Info(BluetoothPath, 0x09CC, 78)], (BluetoothPath, silent));
        Assert.Empty(await Reader(b, bluetooth: true).ReadAsync(default));
        Assert.Empty(silent.FeatureRequests);            // heard nothing, so nothing to switch
    }

    [Fact]
    public async Task ACableIsPreferredAndTheBluetoothLinkIsNotTouched()
    {
        var cable = new RecordingHandle(read: () => Usb(0x0B));
        var bluetooth = new RecordingHandle(read: () => Basic());
        var system = new RecordingHidSystem(
            [Info(BluetoothPath, 0x09CC, 78), Info(UsbPath, 0x09CC, 64)],
            (UsbPath, cable), (BluetoothPath, bluetooth));

        var r = Assert.Single(await Reader(system, bluetooth: true).ReadAsync(default));
        Assert.Equal(100, r.Percent);
        Assert.Equal([UsbPath], system.Opened);
        Assert.Empty(bluetooth.FeatureRequests);
    }

    [Fact]
    public async Task TheAdapterIsReadAndOnlyTheRightCollectionOfADeviceCounts()
    {
        var adapter = new RecordingHandle(read: () => Usb(0x05, status1: 0x04));
        var other = new RecordingHandle(read: () => Usb(0x05));
        var notAGamepad = new HidInfo(@"\\?\hid#vid_054c&pid_09cc&col02#x#{g}", 0x054C, 0x09CC, 0xFF00, 0x01, 64, 0);
        var system = new RecordingHidSystem(
            [Info(AdapterPath, 0x0BA0, 64), notAGamepad],
            (AdapterPath, adapter), (notAGamepad.Path, other));

        var r = Assert.Single(await Reader(system).ReadAsync(default));
        Assert.False(r.Connected);
        Assert.Equal("dualshock4:0ba0", r.Id);
        Assert.Equal([AdapterPath], system.Opened);
    }

    [Fact]
    public async Task TheSettingIsReadOnEveryReadSoTurningItOnWorksWithoutARestart()
    {
        var pad = new RecordingHandle(read: () => Extended(0x08));
        var system = new RecordingHidSystem([Info(BluetoothPath, 0x09CC, 78)], (BluetoothPath, pad));
        var on = false;
        var reader = new DualShock4Reader(system, readBluetooth: () => on);

        Assert.Empty(await reader.ReadAsync(default));
        on = true;
        Assert.Single(await reader.ReadAsync(default));
        on = false;
        Assert.Empty(await reader.ReadAsync(default));
    }

    [Fact]
    public async Task ADualSenseIsNotTakenForADualShock()
    {
        var dualSense = new HidInfo(@"\\?\hid#vid_054c&pid_0ce6&mi_03#x#{g}", 0x054C, 0x0CE6, 0x01, 0x05, 64, 64);
        var system = new RecordingHidSystem([dualSense], (dualSense.Path, new RecordingHandle(read: () => Usb(0x05))));
        Assert.Empty(await Reader(system).ReadAsync(default));
        Assert.Empty(system.Opened);
    }
}
