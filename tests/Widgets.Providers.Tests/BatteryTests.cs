using Widgets.Core;
using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Tests;

public class BatteryTests
{
    private sealed class FakeHandle(Func<byte[], IEnumerable<byte[]>> respond, List<byte[]> sent) : IHidHandle
    {
        private readonly Queue<byte[]> _pending = new();

        public Task<bool> WriteAsync(byte[] report, CancellationToken ct)
        {
            sent.Add(report);
            foreach (var r in respond(report)) _pending.Enqueue(r);
            return Task.FromResult(true);
        }

        public async Task<byte[]?> ReadAsync(CancellationToken ct)
        {
            if (_pending.TryDequeue(out var r)) return r;
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return null; // silent device
        }

        public Task DrainAsync() { _pending.Clear(); return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class FakeHid(IReadOnlyList<HidInfo> infos, Func<byte[], IEnumerable<byte[]>> respond) : IHidSystem
    {
        public List<byte[]> Sent { get; } = [];
        public IReadOnlyList<HidInfo> Enumerate(int vendorId) => infos.Where(i => i.Vid == vendorId).ToList();
        public IHidHandle Open(HidInfo info) => new FakeHandle(respond, Sent);
    }

    private static HidInfo Info(int pid, int usagePage = 0xFFC0, int usage = 1, string path = "p1") =>
        new(path, 0x1038, pid, usagePage, usage, 65, 65);

    private static byte[] Reply(params byte[] bytes)
    {
        var r = new byte[65];
        bytes.CopyTo(r, 1); // byte 0 is the report id, 0 for unnumbered reports
        return r;
    }

    private static SteelSeriesReader Reader(FakeHid hid) => new(hid, TimeSpan.FromMilliseconds(300));

    // ---- SteelSeries: Nova 7 (reply layout: [0]=0xB0, [2]=level, [3]=connection/charging)

    [Fact]
    public async Task Nova7_PercentageAndRequestFormat()
    {
        var hid = new FakeHid([Info(0x22A1)], _ => [Reply(0xB0, 0, 44, 0x00 + 3)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));

        Assert.Equal("steelseries:22a1", r.Id);
        Assert.Equal(44, r.Percent);
        Assert.True(r.Connected);
        var sent = Assert.Single(hid.Sent);
        Assert.Equal(65, sent.Length);                       // padded to the output report length
        Assert.Equal(new byte[] { 0x00, 0xB0 }, sent[..2]);  // report id 0, status request
        Assert.All(sent[2..], b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Nova7_ChargingFlag()
    {
        var hid = new FakeHid([Info(0x22A1)], _ => [Reply(0xB0, 0, 80, 0x01)]);
        Assert.True(Assert.Single(await Reader(hid).ReadAsync(default)).Charging);
    }

    [Fact]
    public async Task Nova7_HeadsetOffIsConnectedFalseWithNoPercent()
    {
        var hid = new FakeHid([Info(0x22A1)], _ => [Reply(0xB0, 0, 44, 0x00)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.False(r.Connected);
        Assert.Null(r.Percent);
    }

    [Fact]
    public async Task Nova7_DiscreteVariantScalesZeroToFour()
    {
        var hid = new FakeHid([Info(0x2202)], _ => [Reply(0xB0, 0, 3, 0x01)]);
        Assert.Equal(75, Assert.Single(await Reader(hid).ReadAsync(default)).Percent);
    }

    [Fact]
    public async Task UnrelatedReportsBeforeTheStatusReplyAreSkipped()
    {
        var hid = new FakeHid([Info(0x22A1)], _ => [Reply(0x07, 1, 1, 1), Reply(0x25, 9, 9, 9), Reply(0xB0, 0, 61, 0x02)]);
        Assert.Equal(61, Assert.Single(await Reader(hid).ReadAsync(default)).Percent);
    }

    [Fact]
    public async Task SilentDeviceIsLeftOutAndDoesNotHang()
    {
        var hid = new FakeHid([Info(0x22A1)], _ => []);
        Assert.Empty(await Reader(hid).ReadAsync(default));
    }

    // ---- Rival 3

    [Fact]
    public async Task Rival3_ReadsLevelAndUsesItsOwnRequest()
    {
        var hid = new FakeHid([Info(0x1830)], _ => [Reply(36, 0, 0)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(36, r.Percent);
        Assert.Equal(DeviceKind.Mouse, r.Kind);
        Assert.Equal(new byte[] { 0x00, 0xAA, 0x01 }, hid.Sent[0][..3]);
    }

    [Fact]
    public async Task Rival3_DroppedRequestIsAskedAgain()
    {
        var requests = 0;
        var hid = new FakeHid([Info(0x1830)], _ => ++requests == 1 ? [] : [Reply(36, 0, 0)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(36, r.Percent);
        Assert.Equal(2, hid.Sent.Count);
    }

    [Fact]
    public async Task Rival3_NeverAnsweringGivesUpWithinTheDeviceBudget()
    {
        var hid = new FakeHid([Info(0x1830)], _ => []);
        var started = DateTime.UtcNow;
        Assert.Empty(await Reader(hid).ReadAsync(default));
        Assert.Equal(3, hid.Sent.Count);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(900));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Rival3_ImplausibleLevelIsNoReading(byte level)
    {
        var hid = new FakeHid([Info(0x1830)], _ => [Reply(level, 0, 0)]);
        Assert.Empty(await Reader(hid).ReadAsync(default));
    }

    // ---- selection of interfaces

    [Fact]
    public async Task OnlyTheVendorDefinedInterfaceIsOpened_AndOncePerProduct()
    {
        var infos = new[]
        {
            Info(0x22A1, usagePage: 0x000C, usage: 1, path: "consumer"),
            Info(0x22A1, usagePage: 0xFF00, usage: 1, path: "other-vendor"),
            Info(0x22A1, path: "b"),
            Info(0x22A1, path: "a"),
        };
        var hid = new FakeHid(infos, _ => [Reply(0xB0, 0, 50, 0x03)]);
        Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Single(hid.Sent);
    }

    [Fact]
    public async Task UnknownProductsAreIgnored()
    {
        var hid = new FakeHid([Info(0x9999)], _ => [Reply(0xB0, 0, 50, 3)]);
        Assert.Empty(await Reader(hid).ReadAsync(default));
        Assert.Empty(hid.Sent);
    }

    [Fact]
    public async Task EnumerationFailureYieldsNothing()
    {
        var reader = new SteelSeriesReader(new ThrowingHid());
        Assert.Empty(await reader.ReadAsync(default));
    }

    private sealed class ThrowingHid : IHidSystem
    {
        public IReadOnlyList<HidInfo> Enumerate(int vendorId) => throw new InvalidOperationException();
        public IHidHandle Open(HidInfo info) => throw new InvalidOperationException();
    }

    // ---- SteelSeries: Arctis Nova Pro Wireless (unverified on hardware; layout from the reference project:
    // request 06 B0 on interface mi_04, [6] = level 0..8, [15] = 1 off / 2 charging)

    private static byte[] NovaProReply(byte level, byte state)
    {
        var d = new byte[16];
        d[6] = level;
        d[15] = state;
        return Reply(d);
    }

    private static HidInfo NovaProInfo(string path = @"\?\hid#vid_1038&pid_12e0&mi_04#7&1") =>
        Info(0x12E0, usagePage: 0xFF00, usage: 3, path: path);

    [Fact]
    public async Task NovaPro_LevelScalesFromEightStepsAndSendsReportSix()
    {
        var hid = new FakeHid([NovaProInfo()], _ => [NovaProReply(4, 0)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));

        Assert.Equal("steelseries:12e0", r.Id);
        Assert.Equal("SteelSeries Arctis Nova Pro Wireless", r.Name);
        Assert.Equal(50, r.Percent);
        Assert.True(r.Connected);
        Assert.False(r.Charging);
        var sent = Assert.Single(hid.Sent);
        Assert.Equal(new byte[] { 0x06, 0xB0 }, sent[..2]);
        Assert.All(sent[2..], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 100)]
    [InlineData(200, 100)] // out of range is clamped, never above 100
    public async Task NovaPro_ClampsTheLevel(byte raw, int percent)
    {
        var hid = new FakeHid([NovaProInfo()], _ => [NovaProReply(raw, 0)]);
        Assert.Equal(percent, Assert.Single(await Reader(hid).ReadAsync(default)).Percent);
    }

    [Fact]
    public async Task NovaPro_ChargingFlag()
    {
        var hid = new FakeHid([NovaProInfo()], _ => [NovaProReply(6, 0x02)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.True(r.Charging);
        Assert.Equal(75, r.Percent);
    }

    [Fact]
    public async Task NovaPro_HeadsetOffIsConnectedFalseWithNoPercent()
    {
        var hid = new FakeHid([NovaProInfo()], _ => [NovaProReply(6, 0x01)]);
        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.False(r.Connected);
        Assert.Null(r.Percent);
        Assert.False(r.Charging);
    }

    [Fact]
    public async Task NovaPro_OtherInterfacesAreNeverWrittenTo()
    {
        var hid = new FakeHid([NovaProInfo(@"\?\hid#vid_1038&pid_12e0&mi_00#7&1"), NovaProInfo(@"\?\hid#vid_1038&pid_12e0&mi_03#7&2")],
            _ => [NovaProReply(4, 0)]);
        Assert.Empty(await Reader(hid).ReadAsync(default));
        Assert.Empty(hid.Sent);
    }

    [Fact]
    public async Task NovaPro_VariantXIsRecognised()
    {
        var info = Info(0x12E5, usagePage: 0xFF00, usage: 3, path: @"\?\hid#vid_1038&pid_12e5&mi_04#7&1");
        var hid = new FakeHid([info], _ => [NovaProReply(8, 0)]);
        Assert.Equal("SteelSeries Arctis Nova Pro Wireless X", Assert.Single(await Reader(hid).ReadAsync(default)).Name);
    }

    // ---- provider

    private sealed class ScriptedReader : IBatteryReader
    {
        public Queue<Func<IReadOnlyList<BatteryReading>>> Steps { get; } = new();
        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) => Task.FromResult(Steps.Dequeue()());
    }

    private static BatteryReading Dev(int? pct, bool connected = true) =>
        new("steelseries:22a1", "SteelSeries Arctis Nova 7", DeviceKind.Headset, pct, false, connected);

    [Fact]
    public async Task Provider_ShowsLiveValueWithShortName()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Dev(44)]);
        var m = Assert.Single(await new BatteryProvider([reader]).PollAsync(default));
        Assert.Equal("battery:steelseries:22a1", m.Id);
        Assert.Equal("Arctis Nova 7", m.Label);
        Assert.Equal(MetricKind.Battery, m.Kind);
        Assert.Equal(44, m.Value);
        Assert.False(m.Stale);
    }

    [Fact]
    public async Task Provider_FlagsChargingOnlyWhileLiveAndCharging()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Dev(44) with { Charging = true }]);
        reader.Steps.Enqueue(() => []); // goes silent: the last value is shown, but not as charging
        var p = new BatteryProvider([reader]);
        Assert.True(Assert.Single(await p.PollAsync(default)).Charging);
        var stale = Assert.Single(await p.PollAsync(default));
        Assert.True(stale.Stale);
        Assert.False(stale.Charging);
    }

    [Fact]
    public async Task Provider_NotChargingByDefault()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Dev(44)]);
        Assert.False(Assert.Single(await new BatteryProvider([reader]).PollAsync(default)).Charging);
    }

    [Fact]
    public async Task Provider_KeepsLastValueStaleWhenTheDeviceGoesSilent()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Dev(44)]);
        reader.Steps.Enqueue(() => []);
        var p = new BatteryProvider([reader]);
        await p.PollAsync(default);
        var m = Assert.Single(await p.PollAsync(default));
        Assert.Equal(44, m.Value);
        Assert.True(m.Stale);
    }

    [Fact]
    public async Task Provider_DeviceSwitchedOffShowsNoValueNotAnOldOne()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Dev(44)]);
        reader.Steps.Enqueue(() => [Dev(null, connected: false)]);
        var p = new BatteryProvider([reader]);
        await p.PollAsync(default);
        var m = Assert.Single(await p.PollAsync(default));
        Assert.Null(m.Value);
        Assert.False(m.Stale);
        Assert.Equal("--", MetricFormat.Value(m, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Provider_AFailingReaderDoesNotHideTheOthers()
    {
        var bad = new ScriptedReader();
        bad.Steps.Enqueue(() => throw new IOException());
        var good = new ScriptedReader();
        good.Steps.Enqueue(() => [Dev(70)]);
        var m = Assert.Single(await new BatteryProvider([bad, good]).PollAsync(default));
        Assert.Equal(70, m.Value);
    }

    private static BatteryReading Pad(string id, string name, int? pct, string? note = null) =>
        new(id, name, DeviceKind.Gamepad, pct, false, true, note);

    [Fact]
    public async Task Provider_AConnectedPadMakesAnOlderPadStepAsideButKeepsItsEntry()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Pad("dualsense:0ce6", "DualSense", 100)]);
        reader.Steps.Enqueue(() => [Pad("xinput:0", "Xbox Controller", null, "USB")]); // DualSense unplugged, Xbox plugged in
        var p = new BatteryProvider([reader]);
        await p.PollAsync(default);
        var list = await p.PollAsync(default);

        // Each pad is its own entry: the settings page keeps the DualSense row (label, colour, order) while the taskbar drops it.
        var xbox = Assert.Single(list, m => m.Id == "battery:xinput:0");
        Assert.Equal("USB", MetricFormat.Value(xbox));
        Assert.False(xbox.Superseded);
        Assert.Null(xbox.OfflineSince);
        var ds = Assert.Single(list, m => m.Id == "battery:dualsense:0ce6");
        Assert.True(ds.Superseded);
        Assert.NotNull(ds.OfflineSince);
        Assert.Equal(100, ds.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Provider_TheOlderPadReturnsTheMomentItIsConnectedAgainAndTheOtherStepsAside()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Pad("dualsense:0ce6", "DualSense", 100)]);
        reader.Steps.Enqueue(() => [Pad("xinput:0", "Xbox Controller", 65)]);
        reader.Steps.Enqueue(() => [Pad("dualsense:0ce6", "DualSense", 90)]); // Xbox pad switched off, DualSense back
        var p = new BatteryProvider([reader]);
        await p.PollAsync(default);
        await p.PollAsync(default);
        var list = await p.PollAsync(default);
        Assert.False(Assert.Single(list, m => m.Id == "battery:dualsense:0ce6").Superseded);
        Assert.True(Assert.Single(list, m => m.Id == "battery:xinput:0").Superseded);
    }


    [Fact]
    public async Task Provider_WithNoPadConnectedTheLastPadStaysDimmed()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Pad("dualsense:0ce6", "DualSense", 100)]);
        reader.Steps.Enqueue(() => []);
        var p = new BatteryProvider([reader]);
        await p.PollAsync(default);
        var m = Assert.Single(await p.PollAsync(default));
        Assert.Equal(100, m.Value);
        Assert.True(m.Stale);
    }

    [Fact]
    public async Task Provider_TwoConnectedPadsAreBothShown()
    {
        var reader = new ScriptedReader();
        reader.Steps.Enqueue(() => [Pad("dualsense:0ce6", "DualSense", 80), Pad("xinput:0", "Xbox Controller", 65)]);
        Assert.Equal(2, (await new BatteryProvider([reader]).PollAsync(default)).Count);
    }

    [Fact]
    public async Task Provider_APadDoesNotHideASleepingMouse()
    {
        var reader = new ScriptedReader();
        var mouse = new BatteryReading("steelseries:1830", "SteelSeries Rival 3 Wireless", DeviceKind.Mouse, 31, false, true);
        reader.Steps.Enqueue(() => [mouse]);
        reader.Steps.Enqueue(() => [Pad("xinput:0", "Xbox Controller", null, "USB")]);
        var p = new BatteryProvider([reader]);
        await p.PollAsync(default);
        var list = await p.PollAsync(default);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, m => m.Id == "battery:steelseries:1830" && m.Stale && m.Value == 31);
    }
}

public class GamepadBatteryTests
{
    private static byte[] DualSenseReport(byte status, byte id = 0x01)
    {
        var r = new byte[64];
        r[0] = id;
        r[53] = status;
        return r;
    }

    [Theory]
    [InlineData(0x00, 5, false)]    // discharging, level 0
    [InlineData(0x07, 75, false)]
    [InlineData(0x0A, 100, false)]  // level 10 is clamped
    [InlineData(0x15, 55, true)]    // charging
    [InlineData(0x2A, 100, false)]  // full
    public void DualSense_DecodesLevelAndChargeState(byte status, int percent, bool charging)
    {
        var r = DualSenseReader.Decode(DualSenseReport(status), 0x0CE6)!;
        Assert.Equal(percent, r.Percent);
        Assert.Equal(charging, r.Charging);
        Assert.Equal("dualsense:0ce6", r.Id);
    }

    [Fact]
    public void DualSense_FaultStateHasNoPercent() =>
        Assert.Null(DualSenseReader.Decode(DualSenseReport(0xF3), 0x0CE6)!.Percent);

    [Fact]
    public void DualSense_IgnoresOtherReportsAndShortOnes()
    {
        Assert.Null(DualSenseReader.Decode(DualSenseReport(0x15, id: 0x31), 0x0CE6));
        Assert.Null(DualSenseReader.Decode(new byte[10], 0x0CE6));
    }

    private sealed class OneInfoHid(HidInfo info, byte[] report) : IHidSystem
    {
        public IReadOnlyList<HidInfo> Enumerate(int vendorId) => vendorId == 0x054C ? [info] : [];
        public IHidHandle Open(HidInfo i) => new Once(report);

        private sealed class Once(byte[] report) : IHidHandle
        {
            private bool _given;
            public Task<bool> WriteAsync(byte[] r, CancellationToken ct) => throw new InvalidOperationException("a DualSense is never written to");
            public Task<byte[]?> ReadAsync(CancellationToken ct) { var r = _given ? null : report; _given = true; return Task.FromResult<byte[]?>(r); }
            public Task DrainAsync() => Task.CompletedTask;
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task DualSense_ReaderUsesTheGamepadInterfaceAndNeverWrites()
    {
        var info = new HidInfo("p", 0x054C, 0x0CE6, 0x01, 0x05, 64, 0);
        var r = Assert.Single(await new DualSenseReader(new OneInfoHid(info, DualSenseReport(0x15))).ReadAsync(default));
        Assert.Equal(55, r.Percent);

        var wrongUsage = info with { UsagePage = 0xFF00 };
        Assert.Empty(await new DualSenseReader(new OneInfoHid(wrongUsage, DualSenseReport(0x15))).ReadAsync(default));
    }

    [Theory]
    [InlineData(2, 0, 5)]
    [InlineData(3, 1, 30)]
    [InlineData(2, 2, 65)]
    [InlineData(3, 3, 100)]
    public void XInput_MapsCoarseLevels(byte type, byte level, int percent) =>
        Assert.Equal(percent, XInputReader.Decode(type, level));

    [Theory]
    [InlineData(0)] // disconnected
    [InlineData(1)] // wired
    [InlineData(0xFF)] // unknown
    public void XInput_NoBatteryForWiredOrUnknown(byte type) => Assert.Null(XInputReader.Decode(type, 3));

    [Fact]
    public async Task XInput_ReportsWirelessLevelsAndWiredPadsAsUsb()
    {
        var slots = new (byte, byte)?[] { (1, 3), null, (3, 2), (0, 0) };
        var list = await new XInputReader(i => slots[i]).ReadAsync(default);
        Assert.Equal(2, list.Count);
        var wired = list[0];
        Assert.Equal("xinput:0", wired.Id);
        Assert.Equal("Xbox Controller", wired.Name);
        Assert.Null(wired.Percent);
        Assert.True(wired.Connected);
        Assert.Equal("USB", wired.Note);
        var wireless = list[1];
        Assert.Equal("xinput:2", wireless.Id);
        Assert.Equal(65, wireless.Percent);
        Assert.Null(wireless.Note);
    }
}

public class BluetoothLeBatteryTests
{
    private static Task<IReadOnlyList<BatteryReading>> Read(params GattBattery[] devices) =>
        new BluetoothLeBatteryReader(_ => Task.FromResult<IReadOnlyList<GattBattery>>(devices)).ReadAsync(default);

    [Fact]
    public async Task ConnectedDeviceShowsItsLevel()
    {
        var r = Assert.Single(await Read(new GattBattery("id1", " Keyboard K1 ", true, 82)));
        Assert.Equal(82, r.Percent);
        Assert.Equal("Keyboard K1", r.Name);
        Assert.Equal("ble:id1", r.Id);
        Assert.True(r.Connected);
    }

    [Fact]
    public async Task DisconnectedDeviceIsOffWithNoPercent()
    {
        var r = Assert.Single(await Read(new GattBattery("id1", "Buds", false, 55)));
        Assert.False(r.Connected);
        Assert.Null(r.Percent);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task ImplausibleLevelIsDropped(int level) =>
        Assert.Null(Assert.Single(await Read(new GattBattery("id1", "X", true, level))).Percent);

    [Fact]
    public async Task UnnamedDeviceGetsAGenericName() =>
        Assert.Equal("Bluetooth device", Assert.Single(await Read(new GattBattery("id1", "", true, 40))).Name);

    [Fact]
    public async Task RealBluetoothStackNeverThrows()
    {
        // Whatever this machine has paired (possibly nothing), the Windows-backed reader must return normally.
        var result = await new BluetoothLeBatteryReader().ReadAsync(default);
        Assert.NotNull(result);
    }
}

public class BatteryCacheTests
{
    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Reader(Func<IReadOnlyList<BatteryReading>> read) : IBatteryReader
    {
        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) => Task.FromResult(read());
    }

    private sealed class TempPath : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "bc-" + Guid.NewGuid().ToString("N"));
        public string File_ => Path.Combine(Dir, "sub", "battery-cache.json");
        public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static BatteryReading Mouse(int? pct) =>
        new("steelseries:1830", "SteelSeries Rival 3 Wireless", DeviceKind.Mouse, pct, false, true);

    [Fact]
    public async Task SleepingDeviceAtStartupShowsItsRememberedLevelDimmed()
    {
        using var t = new TempPath();
        var cache = new BatteryCache(t.File_);
        var time = new FakeTime(T0);

        // Run 1: the mouse answers.
        await new BatteryProvider([new Reader(() => [Mouse(36)])], time: time, cache: cache).PollAsync(default);

        // Run 2 (new provider, as after a restart): the mouse is asleep and says nothing.
        time.Now = T0.AddHours(5);
        var m = Assert.Single(await new BatteryProvider([new Reader(() => [])], time: time, cache: cache).PollAsync(default));
        Assert.Equal("Rival 3 Wireless", m.Label);
        Assert.Equal(36, m.Value);
        Assert.True(m.Stale);
        Assert.Equal(T0, m.Updated); // shows when the value was actually read
    }

    [Fact]
    public async Task OfflineSinceIsWhenTheDeviceWasLastConnected()
    {
        var time = new FakeTime(T0);
        var steps = new Queue<IReadOnlyList<BatteryReading>>([
            [Mouse(40)],                              // connected
            [],                                       // silent
            [Mouse(null) with { Connected = false }], // reported off
        ]);
        var p = new BatteryProvider([new Reader(steps.Dequeue)], time: time);

        Assert.Null(Assert.Single(await p.PollAsync(default)).OfflineSince);
        time.Now = T0.AddMinutes(20);
        Assert.Equal(T0, Assert.Single(await p.PollAsync(default)).OfflineSince);
        time.Now = T0.AddMinutes(50);
        Assert.Equal(T0, Assert.Single(await p.PollAsync(default)).OfflineSince);
    }

    [Fact]
    public async Task ADeviceFirstMetSwitchedOffIsOfflineFromNowNotFromTheEpoch()
    {
        var time = new FakeTime(T0);
        var off = Mouse(null) with { Connected = false };
        var m = Assert.Single(await new BatteryProvider([new Reader(() => [off])], time: time).PollAsync(default));
        Assert.Equal(T0, m.OfflineSince);
    }

    [Fact]
    public async Task ARememberedDeviceIsOfflineFromWhenItWasLastHeard()
    {
        using var t = new TempPath();
        var cache = new BatteryCache(t.File_);
        var time = new FakeTime(T0);
        await new BatteryProvider([new Reader(() => [Mouse(36)])], time: time, cache: cache).PollAsync(default);

        time.Now = T0.AddHours(5);
        var m = Assert.Single(await new BatteryProvider([new Reader(() => [])], time: time, cache: cache).PollAsync(default));
        Assert.Equal(T0, m.OfflineSince);
    }

    [Fact]
    public async Task ARealReadingReplacesTheRememberedOne()
    {
        using var t = new TempPath();
        var cache = new BatteryCache(t.File_);
        await new BatteryProvider([new Reader(() => [Mouse(36)])], cache: cache).PollAsync(default);

        var m = Assert.Single(await new BatteryProvider([new Reader(() => [Mouse(34)])], cache: cache).PollAsync(default));
        Assert.Equal(34, m.Value);
        Assert.False(m.Stale);
        Assert.Equal(34, Assert.Single(cache.Load(DateTimeOffset.UtcNow)).Percent);
    }

    [Fact]
    public async Task SwitchedOffDeviceStillShowsNoValueEvenWhenRemembered()
    {
        using var t = new TempPath();
        var cache = new BatteryCache(t.File_);
        await new BatteryProvider([new Reader(() => [Mouse(36)])], cache: cache).PollAsync(default);

        var off = Mouse(null) with { Connected = false };
        var m = Assert.Single(await new BatteryProvider([new Reader(() => [off])], cache: cache).PollAsync(default));
        Assert.Null(m.Value);
    }

    [Fact]
    public async Task SteadyLevelDoesNotRewriteTheFile()
    {
        using var t = new TempPath();
        var cache = new BatteryCache(t.File_);
        var p = new BatteryProvider([new Reader(() => [Mouse(36)])], cache: cache);
        await p.PollAsync(default);
        var first = File.GetLastWriteTimeUtc(t.File_);
        await Task.Delay(50);
        await p.PollAsync(default);
        Assert.Equal(first, File.GetLastWriteTimeUtc(t.File_));
    }

    [Fact]
    public void Load_ForgetsOldDevicesAndIgnoresDamage()
    {
        using var t = new TempPath();
        var cache = new BatteryCache(t.File_);
        cache.Save([
            new CachedBattery("a", "A", DeviceKind.Mouse, 50, T0),
            new CachedBattery("old", "Old", DeviceKind.Mouse, 50, T0 - BatteryCache.MaxAge - TimeSpan.FromDays(1)),
            new CachedBattery("bad", "Bad", DeviceKind.Mouse, 250, T0),
            new CachedBattery("a", "Dup", DeviceKind.Mouse, 10, T0),
        ]);
        Assert.Equal(["a"], cache.Load(T0).Select(c => c.Id));

        File.WriteAllText(t.File_, "not json");
        Assert.Empty(cache.Load(T0));
        Assert.Empty(new BatteryCache(Path.Combine(t.Dir, "missing.json")).Load(T0));
    }

    [Fact]
    public async Task UnwritableCacheLocationDoesNotBreakPolling()
    {
        using var t = new TempPath();
        Directory.CreateDirectory(t.Dir);
        var blocker = Path.Combine(t.Dir, "blocker");
        File.WriteAllText(blocker, "x"); // a file where a folder is needed
        var cache = new BatteryCache(Path.Combine(blocker, "c.json"));
        var m = Assert.Single(await new BatteryProvider([new Reader(() => [Mouse(36)])], cache: cache).PollAsync(default));
        Assert.Equal(36, m.Value);
    }
}
