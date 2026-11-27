using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.Logitech;

namespace Widgets.Providers.Tests;

public class LogitechProtocolTests
{
    [Fact]
    public void ALongAndAShortRequestAreEncodedAndPadded()
    {
        var longRequest = Hidpp.EncodeRequest(0xFF, 5, 1, [], 0x0B);
        Assert.Equal(20, longRequest.Length);
        Assert.Equal([0x11, 0xFF, 0x05, 0x1B], longRequest[..4]);
        Assert.All(longRequest[4..], b => Assert.Equal(0, b));

        var shortRequest = Hidpp.EncodeRequest(2, 0, 0, [0x10, 0x04], 0x0B, useLong: false);
        Assert.Equal([0x10, 0x02, 0x00, 0x0B, 0x10, 0x04, 0x00], shortRequest);

        Assert.Throws<ArgumentException>(() => Hidpp.EncodeRequest(1, 1, 0, new byte[17]));
    }

    [Fact]
    public void RepliesAndBothKindsOfErrorAreParsed()
    {
        var ok = Hidpp.ParseReport([0x11, 0x01, 0x05, 0x1B, 60, 4, 0, 0])!;
        Assert.Equal((1, 5, 1, 0x0B), (ok.DeviceIndex, ok.FeatureIndex, ok.Function, ok.SwId));
        Assert.Equal([60, 4, 0, 0], ok.Params);
        Assert.Null(ok.Error);

        // HID++ 1.0 error to a 2.0 ping: 10 dev 8F sub addr err 00
        var one = Hidpp.ParseReport([0x10, 0x01, 0x8F, 0x00, 0x1B, 0x01, 0x00])!;
        Assert.Equal(("hidpp1", 0x01), (one.ErrorKind, one.Error));
        Assert.Equal((0, 1, 0x0B), (one.FeatureIndex, one.Function, one.SwId));

        // HID++ 2.0 error: 11 dev FF feature fn|sw err
        var two = Hidpp.ParseReport([0x11, 0x01, 0xFF, 0x05, 0x1B, 0x07, 0, 0])!;
        Assert.Equal(("hidpp2", 0x07), (two.ErrorKind, two.Error));
        Assert.Equal((5, 1, 0x0B), (two.FeatureIndex, two.Function, two.SwId));

        Assert.Null(Hidpp.ParseReport(null));
        Assert.Null(Hidpp.ParseReport([0x11, 0, 0]));                  // too short
        Assert.Null(Hidpp.ParseReport([0x20, 0, 0, 0, 0, 0, 0]));       // not a HID++ report
    }

    [Fact]
    public void OnlyTheListedQueriesCanBeBuilt()
    {
        // allowed
        Hidpp.BuildGuardedRequest(0xFF, 0, Hidpp.FRoot, 0, [0x10, 0x04]);
        Hidpp.BuildGuardedRequest(1, 0, Hidpp.FRoot, 1, [0, 0, 0xA5]);
        Hidpp.BuildGuardedRequest(1, 5, Hidpp.FUnifiedBattery, 1, []);
        Hidpp.BuildGuardedRequest(1, 3, Hidpp.FDeviceName, 2, []);
        Hidpp.BuildHidpp1GetRegister(1, 0x0D);
        Hidpp.BuildHidpp1GetRegister(0xFF, 0x07);

        // refused before anything could be written
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 5, Hidpp.FUnifiedBattery, 0, []));       // a function not listed
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 5, Hidpp.FUnifiedBattery, 2, []));
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 5, 0x1814, 0, []));                       // a feature not listed
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 3, Hidpp.FRoot, 0, [0x18, 0x14]));        // getFeature for a feature not listed
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 3, Hidpp.FRoot, 0, [0x10]));              // and one with no id
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 2, Hidpp.FRoot, 1, [0, 0, 0xA5]));        // root is always index 0
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 0, Hidpp.FUnifiedBattery, 1, []));        // index 0 is root
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 0x80, Hidpp.FUnifiedBattery, 1, []));     // collides with the error markers
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(7, 5, Hidpp.FUnifiedBattery, 1, []));        // no such device index
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildGuardedRequest(1, 5, Hidpp.FUnifiedBattery, 1, [], swId: 0));
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildHidpp1GetRegister(1, 0x01));                                // a register that is not a battery one
        Assert.Throws<HidppSafetyException>(() => Hidpp.BuildHidpp1GetRegister(9, 0x0D));
    }

    [Theory]
    [InlineData(new byte[] { 60, 4, 0, 0 }, 60, false)]      // percent given
    [InlineData(new byte[] { 0, 8, 0, 0 }, 100, false)]      // none given: the level flag stands in (full)
    [InlineData(new byte[] { 0, 4, 0, 0 }, 55, false)]       // good
    [InlineData(new byte[] { 0, 2, 0, 0 }, 15, false)]       // low
    [InlineData(new byte[] { 0, 1, 0, 0 }, 5, false)]        // critical
    [InlineData(new byte[] { 40, 4, 1, 1 }, 40, true)]       // charging
    [InlineData(new byte[] { 40, 4, 2, 1 }, 40, true)]       // charging slowly
    [InlineData(new byte[] { 100, 8, 3, 1 }, 100, false)]    // full
    public void TheUnifiedBatteryIsDecoded(byte[] reply, int percent, bool charging) =>
        Assert.Equal((percent, charging), Hidpp.DecodeUnifiedBattery(reply));

    [Fact]
    public void AnUnusableUnifiedBatteryReplyGivesNothing()
    {
        Assert.Null(Hidpp.DecodeUnifiedBattery([60, 4]));
        Assert.Null(Hidpp.DecodeUnifiedBattery([101, 4, 0]));
        Assert.Equal((0, false), Hidpp.DecodeUnifiedBattery([0, 0, 0]));   // no level and no flag: 0%, as upstream
    }

    [Theory]
    [InlineData(new byte[] { 80, 70, 0 }, 80, false)]
    [InlineData(new byte[] { 80, 70, 1 }, 80, true)]
    [InlineData(new byte[] { 80, 70, 2 }, 80, true)]
    [InlineData(new byte[] { 80, 70, 4 }, 80, true)]
    [InlineData(new byte[] { 80, 70, 3 }, 80, false)]
    public void TheBatteryLevelStatusIsDecoded(byte[] reply, int percent, bool charging) =>
        Assert.Equal((percent, charging), Hidpp.DecodeBatteryStatus(reply));

    [Fact]
    public void TheBatteryLevelStatusRejectsALevelOver100()
    {
        Assert.Null(Hidpp.DecodeBatteryStatus([101, 70, 0]));
        Assert.Null(Hidpp.DecodeBatteryStatus([50, 40]));
    }

    [Theory]
    [InlineData(3500, 0)]
    [InlineData(3400, 0)]
    [InlineData(3600, 8)]
    [InlineData(3650, 16)]    // 8 + 17 * 50 / 100 = 16.5, which rounds to the even 16
    [InlineData(3900, 70)]
    [InlineData(4150, 98)]    // 95 + 5 * 50 / 100 = 97.5, rounds to the even 98
    [InlineData(4200, 100)]
    [InlineData(4300, 100)]
    public void VoltageBecomesAPercentOnTheCurve(int mv, int percent) => Assert.Equal(percent, Hidpp.VoltageToPercent(mv));

    [Fact]
    public void VoltageAndAdcDifferInTheirChargingBitAndRejectNonsense()
    {
        var mv3900 = new byte[] { 0x0F, 0x3C, 0x80 };  // 3900 mV, bit 7
        Assert.Equal((70, true), Hidpp.DecodeVoltage(mv3900));
        Assert.Equal((70, false), Hidpp.DecodeAdc(mv3900));
        var adc = new byte[] { 0x0F, 0x3C, 0x01 };     // bit 0
        Assert.Equal((70, false), Hidpp.DecodeVoltage(adc));
        Assert.Equal((70, true), Hidpp.DecodeAdc(adc));

        Assert.Null(Hidpp.DecodeVoltage([0x09, 0xC3, 0]));   // 2499 mV
        Assert.Null(Hidpp.DecodeVoltage([0x13, 0x89, 0]));   // 5001 mV
        Assert.Null(Hidpp.DecodeAdc([0x0F, 0x3C]));
    }

    [Fact]
    public void TheHidpp1RegistersAreDecoded()
    {
        Assert.Equal((83, false), Hidpp.DecodeHidpp1Charge([83, 0, 0x30]));
        Assert.Equal((83, true), Hidpp.DecodeHidpp1Charge([83, 0, 0x50]));
        Assert.Null(Hidpp.DecodeHidpp1Charge([101, 0, 0x30]));
        Assert.Null(Hidpp.DecodeHidpp1Charge([83, 0]));

        Assert.Equal((5, false), Hidpp.DecodeHidpp1Status([1, 0x00]));
        Assert.Equal((20, false), Hidpp.DecodeHidpp1Status([3, 0x00]));
        Assert.Equal((50, true), Hidpp.DecodeHidpp1Status([5, 0x21]));
        Assert.Equal((90, false), Hidpp.DecodeHidpp1Status([7, 0x26]));   // 0x26 is not charging with a low nibble up to 4
        Assert.Equal((100, false), Hidpp.DecodeHidpp1Status([7, 0x25]));  // full
        Assert.Null(Hidpp.DecodeHidpp1Status([4, 0x00]));                // not one of the four level codes
    }

    [Fact]
    public void ANameIsAssembledFromChunksAndATypeBecomesAKind()
    {
        Assert.Equal("MX Master 3", Hidpp.AssembleName([[.. "MX Mast"u8], [.. "er 3"u8, 0, 0]], 11));
        Assert.Equal("MX", Hidpp.AssembleName([[.. "MX"u8, 0, (byte)'X']], 5));   // stops at the first zero
        Assert.Equal(DeviceKind.Mouse, Hidpp.KindFromDeviceType(3));
        Assert.Equal(DeviceKind.Keyboard, Hidpp.KindFromDeviceType(0));
        Assert.Equal(DeviceKind.Headset, Hidpp.KindFromDeviceType(8));
        Assert.Equal(DeviceKind.Gamepad, Hidpp.KindFromDeviceType(10));
        Assert.Equal(DeviceKind.Other, Hidpp.KindFromDeviceType(1));
        Assert.Null(Hidpp.KindFromDeviceType(99));
        Assert.Null(Hidpp.KindFromDeviceType(null));
    }

    [Fact]
    public void NamesFromADeviceAreCleanedAndAKindCanComeFromTheName()
    {
        Assert.Equal("MX bMaster/b", DeviceNaming.Sanitize("MX <b>Master</b>\u0001 &"));   // only the characters < > & go, as upstream
        Assert.Equal(48, DeviceNaming.Sanitize(new string('x', 80)).Length);
        Assert.Equal("a b", DeviceNaming.Sanitize("a    b"));
        Assert.Equal(DeviceKind.Mouse, DeviceNaming.KindFromName("Logitech MX Master 3"));
        Assert.Equal(DeviceKind.Keyboard, DeviceNaming.KindFromName("K380 Multi-Device"));
        Assert.Equal(DeviceKind.Headset, DeviceNaming.KindFromName("Galaxy Buds"));
        Assert.Equal(DeviceKind.Gamepad, DeviceNaming.KindFromName("Xbox Wireless"));
        Assert.Equal(DeviceKind.Other, DeviceNaming.KindFromName("Mystery thing"));
        Assert.Equal(DeviceKind.Other, DeviceNaming.KindFromName("Rebuds"));   // "buds" counts as a whole word only
    }

    // ---- grouping

    private static HidInfo Col(string path, int pid, int usagePage, int usage, int len, string product = "USB Receiver") =>
        new(path, 0x046D, pid, usagePage, usage, len, len, product);

    [Fact]
    public void TheShortAndLongCollectionsOfOneInterfaceFormOneGroup()
    {
        var groups = Hidpp.FindGroups([
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col01#7&abc123&0&0000#{g}", 0xC52B, 0xFF00, 1, 7),
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&abc123&0&0001#{g}", 0xC52B, 0xFF00, 2, 20),
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_00#7&def456&0&0000#{g}", 0xC52B, 0x0001, 6, 8),            // the keyboard interface
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col03#7&abc123&0&0002#{g}", 0xC52B, 0xFF00, 4, 20),     // another usage
            Col(@"\\?\hid#vid_1038&pid_1830&mi_01#7&000&0&0000#{g}", 0x1830, 0xFF00, 1, 7) with { Vid = 0x1038 },
        ]);
        var g = Assert.Single(groups);
        Assert.NotNull(g.Short);
        Assert.NotNull(g.Long);
        Assert.Equal(0xC52B, g.Pid);
    }

    [Fact]
    public void ACollectionWithAReportTooSmallToCarryARequestIsNotUsed()
    {
        Assert.Empty(Hidpp.FindGroups([
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col01#7&abc123&0&0000#{g}", 0xC52B, 0xFF00, 1, 6),
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&abc123&0&0001#{g}", 0xC52B, 0xFF00, 2, 19),
        ]));
    }

    [Fact]
    public void TwoReceiversAreTwoGroupsAndAReceiverIsRecognisedByPidOrName()
    {
        var groups = Hidpp.FindGroups([
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&aaaa&0&0001#{g}", 0xC52B, 0xFF00, 2, 20),
            Col(@"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&bbbb&0&0001#{g}", 0xC52B, 0xFF00, 2, 20),
        ]);
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.True(Hidpp.IsReceiver(g)));

        var direct = Hidpp.FindGroups([Col(@"\\?\hid#vid_046d&pid_c077&mi_01&col02#7&cccc&0&0001#{g}", 0xC077, 0xFF00, 2, 20, "USB Optical Mouse")]);
        Assert.False(Hidpp.IsReceiver(Assert.Single(direct)));

        var named = Hidpp.FindGroups([Col(@"\\?\hid#vid_046d&pid_ffff&mi_01&col02#7&dddd&0&0001#{g}", 0xFFFF, 0xFF00, 2, 20, "Some Receiver")]);
        Assert.True(Hidpp.IsReceiver(Assert.Single(named)));
    }

    [Theory]
    [InlineData(0x046D, 0xC52B, true)]
    [InlineData(0x046D, 0xC548, true)]   // Bolt
    [InlineData(0x046D, 0xC077, false)]
    [InlineData(0x1038, 0xC52B, false)]
    public void TheRegistryKnowsTheReceivers(int vid, int pid, bool expected) => Assert.Equal(expected, SupportedDevices.Supports(vid, pid));
}

public class LogitechReaderTests
{
    private const string LongPath = @"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&abc123&0&0001#{g}";
    private const string ShortPath = @"\\?\hid#vid_046d&pid_c52b&mi_02&col01#7&abc123&0&0000#{g}";

    private static readonly HidInfo LongCollection = new(LongPath, 0x046D, 0xC52B, 0xFF00, 2, 20, 20, "USB Receiver");
    private static readonly HidInfo ShortCollection = new(ShortPath, 0x046D, 0xC52B, 0xFF00, 1, 7, 7, "USB Receiver");

    /// <summary>One device the simulated receiver answers for.</summary>
    private sealed class SimDevice
    {
        public Dictionary<int, int> Features { get; } = [];                       // feature id to its index
        public Dictionary<(int FeatureId, int Function), byte[]> Answers { get; } = [];
        public bool Absent { get; set; }                                          // HID++ 1.0 error 0x09, as for an empty slot
        public bool Hidpp1Only { get; set; }                                      // HID++ 1.0 error 0x01 to a 2.0 ping
        public Dictionary<int, byte[]> Registers { get; } = [];                   // HID++ 1.0 registers
    }

    /// <summary>A receiver or a cabled device: answers a request the way HID++ hardware does and remembers every request.</summary>
    private sealed class Sim
    {
        public Dictionary<int, SimDevice> Devices { get; } = [];
        public List<byte[]> Requests { get; } = [];
        public Func<byte[], byte[]?>? Noise { get; set; }                         // a reply that is not ours, sent just before the real one

        public IHidHandle Handle()
        {
            var queue = new Queue<byte[]>();
            return new RecordingHandle(
                read: () => queue.Count > 0 ? queue.Dequeue() : null,
                onWrite: packet =>
                {
                    Requests.Add(packet);
                    if (Noise?.Invoke(packet) is { } noise) queue.Enqueue(noise);
                    if (Answer(packet) is { } reply) queue.Enqueue(reply);
                    return true;
                });
        }

        private static byte[] Long(params byte[] bytes) { var r = new byte[20]; bytes.CopyTo(r, 0); return r; }
        private static byte[] Short(params byte[] bytes) { var r = new byte[7]; bytes.CopyTo(r, 0); return r; }

        private byte[]? Answer(byte[] p)
        {
            var dev = p[1];
            if (!Devices.TryGetValue(dev, out var d)) return null;                // silent

            if (p[2] == 0x81)                                                      // HID++ 1.0 GET_REGISTER
                return d.Registers.TryGetValue(p[3], out var reg) ? Short([0x10, dev, 0x81, p[3], .. reg]) : Short(0x10, dev, 0x8F, 0x81, p[3], 0x02, 0);

            if (d.Absent) return Short(0x10, dev, 0x8F, p[2], p[3], 0x09, 0);
            if (d.Hidpp1Only) return Short(0x10, dev, 0x8F, p[2], p[3], 0x01, 0);

            var function = p[3] >> 4;
            if (p[2] == 0)
            {
                if (function == 1) return Long([0x11, dev, 0x00, p[3], 4, 2, 0xA5]);
                var id = (p[4] << 8) | p[5];
                return Long([0x11, dev, 0x00, p[3], (byte)d.Features.GetValueOrDefault(id, 0), 0, 3]);
            }
            var feature = d.Features.FirstOrDefault(f => f.Value == p[2]).Key;
            if (feature != 0 && d.Answers.TryGetValue((feature, function), out var answer))
                return Long([0x11, dev, p[2], p[3], .. answer]);
            return Long(0x11, dev, 0xFF, p[2], p[3], 0x07, 0);                     // invalid function
        }

        /// <summary>Every request sent must be one of the listed queries, on the right device and for a feature the device has.</summary>
        public void AssertOnlyReadOnlyRequests()
        {
            Assert.NotEmpty(Requests);
            foreach (var p in Requests)
            {
                Assert.Contains(p[0], new byte[] { 0x10, 0x11 });
                if (p[2] == 0x81)
                {
                    Assert.Equal(0x10, p[0]);
                    Assert.Contains(p[3], new byte[] { 0x07, 0x0D });
                    continue;
                }
                Assert.Equal(0x11, p[0]);
                var function = p[3] >> 4;
                if (p[2] == 0)
                {
                    Assert.Contains(function, new[] { 0, 1 });
                    if (function == 0)
                        Assert.Contains((p[4] << 8) | p[5], new[] { 0x0005, 0x1000, 0x1001, 0x1004, 0x1F20 });
                    continue;
                }
                var feature = Devices[p[1]].Features.First(f => f.Value == p[2]).Key;
                var allowed = feature switch
                {
                    0x0005 => new[] { 0, 1, 2 },
                    0x1004 => new[] { 1 },
                    _ => new[] { 0 },
                };
                Assert.Contains(function, allowed);
            }
        }
    }

    /// <summary>A mouse in receiver slot 1 with the usual features.</summary>
    private static SimDevice Mouse(string name = "MX Master 3", int type = 3, byte soc = 60)
    {
        var d = new SimDevice();
        d.Features[0x0005] = 3;
        d.Features[0x1004] = 5;
        d.Answers[(0x0005, 0)] = [(byte)name.Length];
        d.Answers[(0x0005, 1)] = [.. System.Text.Encoding.ASCII.GetBytes(name)];
        d.Answers[(0x0005, 2)] = [(byte)type];
        d.Answers[(0x1004, 1)] = [soc, 4, 0, 1];
        return d;
    }

    private static LogitechReader Reader(IHidSystem hid, TimeProvider? time = null) =>
        new(hid, time, pingTimeout: TimeSpan.FromMilliseconds(60), queryTimeout: TimeSpan.FromMilliseconds(60));

    private static RecordingHidSystem Receiver(Sim sim) =>
        new([ShortCollection, LongCollection], (ShortPath, sim.Handle()), (LongPath, sim.Handle()));

    [Fact]
    public async Task AMouseInAReceiverSlotIsReadWithItsNameKindAndBattery()
    {
        var sim = new Sim { Devices = { [1] = Mouse() } };
        var r = Assert.Single(await Reader(Receiver(sim)).ReadAsync(default));

        Assert.Equal("logitech:c52b:01", r.Id);
        Assert.Equal("MX Master 3", r.Name);
        Assert.Equal(DeviceKind.Mouse, r.Kind);
        Assert.Equal(60, r.Percent);
        Assert.False(r.Charging);
        Assert.True(r.Connected);
        sim.AssertOnlyReadOnlyRequests();
        Assert.All(sim.Requests, p => Assert.Equal(0x11, p[0]));                  // long reports where there is a long collection
    }

    [Fact]
    public async Task SlotsAreScannedUntilTwoInARowStaySilent()
    {
        var sim = new Sim { Devices = { [1] = Mouse(), [4] = Mouse("Far Away", soc: 10) } };
        var all = await Reader(Receiver(sim)).ReadAsync(default);

        Assert.Equal(["logitech:c52b:01"], all.Select(r => r.Id));                 // 2 and 3 are silent, so 4 is never asked
        Assert.DoesNotContain(sim.Requests, p => p[1] == 4);
        Assert.DoesNotContain(sim.Requests, p => p[1] >= 5);
    }

    [Fact]
    public async Task ASlotThatAnswersAbsentIsSkippedWithoutCountingAsSilence()
    {
        var sim = new Sim { Devices = { [1] = new SimDevice { Absent = true }, [2] = new SimDevice { Absent = true }, [3] = Mouse("Third") } };
        var all = await Reader(Receiver(sim)).ReadAsync(default);
        Assert.Equal(["logitech:c52b:03"], all.Select(r => r.Id));
    }

    [Fact]
    public async Task TheSecondPollDoesNotAskForTheNameAgainAndConfirmsTheBatteryFeatureOnce()
    {
        var sim = new Sim { Devices = { [1] = Mouse() } };
        var reader = Reader(Receiver(sim));
        await reader.ReadAsync(default);
        var first = sim.Requests.Count;
        sim.Requests.Clear();

        var again = Assert.Single(await reader.ReadAsync(default));
        Assert.Equal("MX Master 3", again.Name);                                    // remembered
        Assert.Equal(60, again.Percent);
        // ping, one getFeature to confirm the battery feature index, the battery read, and the second silent slot's pings
        Assert.DoesNotContain(sim.Requests, p => p[2] == 3);                       // nothing to the name feature
        Assert.DoesNotContain(sim.Requests, p => p[2] == 0 && ((p[4] << 8) | p[5]) == 0x0005);
        Assert.True(sim.Requests.Count < first);
        sim.AssertOnlyReadOnlyRequests();
    }

    [Fact]
    public async Task ABatteryFeatureOtherThanTheUnifiedOneIsFoundInOrder()
    {
        var d = new SimDevice();
        d.Features[0x0005] = 2;
        d.Features[0x1000] = 4;                                                    // no 0x1004
        d.Answers[(0x0005, 0)] = [3];
        d.Answers[(0x0005, 1)] = [.. "Key"u8];
        d.Answers[(0x0005, 2)] = [0];
        d.Answers[(0x1000, 0)] = [72, 60, 1];
        var r = Assert.Single(await Reader(Receiver(new Sim { Devices = { [1] = d } })).ReadAsync(default));

        Assert.Equal((72, true), (r.Percent, r.Charging));
        Assert.Equal(DeviceKind.Keyboard, r.Kind);
    }

    [Fact]
    public async Task AVoltageOnlyDeviceBecomesAPercentOnTheCurve()
    {
        var d = new SimDevice();
        d.Features[0x1F20] = 6;
        d.Answers[(0x1F20, 0)] = [0x0F, 0x3C, 0x00];                               // 3900 mV
        var r = Assert.Single(await Reader(Receiver(new Sim { Devices = { [1] = d } })).ReadAsync(default));
        Assert.Equal(70, r.Percent);
    }

    [Fact]
    public async Task ADeviceThatAnswersButCannotGiveABatteryIsListedWithNoValueAndForgottenAfterTwoFailures()
    {
        var d = Mouse();
        d.Features.Remove(0x1004);
        d.Answers.Remove((0x1004, 1));
        var sim = new Sim { Devices = { [1] = d } };
        var reader = Reader(Receiver(sim));

        var r = Assert.Single(await reader.ReadAsync(default));
        Assert.Null(r.Percent);
        Assert.True(r.Connected);                                                  // the provider then keeps the last known level
        Assert.Equal("MX Master 3", r.Name);

        await reader.ReadAsync(default);                                           // the second failure drops what was remembered
        sim.Requests.Clear();
        await reader.ReadAsync(default);
        Assert.Contains(sim.Requests, p => p[2] == 0 && ((p[4] << 8) | p[5]) == 0x0005); // so the name is asked for again
    }

    [Fact]
    public async Task AHidpp1DeviceIsReadThroughItsBatteryRegisters()
    {
        var d = new SimDevice { Hidpp1Only = true };
        d.Registers[0x0D] = [83, 0, 0x30];
        var sim = new Sim { Devices = { [1] = d } };
        var r = Assert.Single(await Reader(Receiver(sim)).ReadAsync(default));

        Assert.Equal(83, r.Percent);
        Assert.Equal("Logitech wireless device", r.Name);                          // the product name is the receiver's
        Assert.Contains(sim.Requests, p => p[0] == 0x10 && p[2] == 0x81 && p[3] == 0x0D);
        sim.AssertOnlyReadOnlyRequests();
    }

    [Fact]
    public async Task AHidpp1DeviceWithOnlyTheCoarseRegisterFallsBackToIt()
    {
        var d = new SimDevice { Hidpp1Only = true };
        d.Registers[0x07] = [5, 0x00];
        var r = Assert.Single(await Reader(Receiver(new Sim { Devices = { [1] = d } })).ReadAsync(default));
        Assert.Equal(50, r.Percent);
    }

    [Fact]
    public async Task ARepliesForAnotherDeviceOrAnotherRequestAreIgnored()
    {
        var sim = new Sim { Devices = { [1] = Mouse() } };
        // Before each real answer, a reply that echoes another device's index (G Hub talking to another device, say).
        sim.Noise = p => [0x11, (byte)(p[1] == 1 ? 2 : 1), p[2], p[3], 99, 4, 0, 1, .. new byte[12]];
        var r = Assert.Single(await Reader(Receiver(sim)).ReadAsync(default));
        Assert.Equal(60, r.Percent);

        // And one with the right device but a software id that is not the request's.
        var sim2 = new Sim { Devices = { [1] = Mouse() } };
        sim2.Noise = p => p[2] == 0 ? null : [0x11, p[1], p[2], (byte)((p[3] & 0xF0) | ((p[3] + 1) & 0x0F)), 12, 4, 0, 1, .. new byte[12]];
        Assert.Equal(60, Assert.Single(await Reader(Receiver(sim2)).ReadAsync(default)).Percent);
    }

    [Fact]
    public async Task ACabledDeviceWithNoReceiverIsAskedAtIndex0xFF()
    {
        var direct = new HidInfo(@"\\?\hid#vid_046d&pid_c077&mi_01&col02#7&eeee&0&0001#{g}", 0x046D, 0xC077, 0xFF00, 2, 20, 20, "USB Optical Mouse");
        var sim = new Sim { Devices = { [0xFF] = Mouse("G502", soc: 33) } };
        var hid = new RecordingHidSystem([direct], (direct.Path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal("logitech:c077:ff", r.Id);
        Assert.Equal(33, r.Percent);
        Assert.All(sim.Requests, p => Assert.Equal(0xFF, p[1]));
        sim.AssertOnlyReadOnlyRequests();
    }

    [Fact]
    public async Task AnInterfaceWithOnlyAShortCollectionUsesShortReports()
    {
        var sim = new Sim();
        var d = new SimDevice { Hidpp1Only = true };
        d.Registers[0x0D] = [40, 0, 0x30];
        sim.Devices[1] = d;
        var hid = new RecordingHidSystem([ShortCollection], (ShortPath, sim.Handle()));

        Assert.Equal(40, Assert.Single(await Reader(hid).ReadAsync(default)).Percent);
        Assert.All(sim.Requests, p => Assert.Equal(7, p.Length));
    }

    [Fact]
    public async Task TwoReceiversOfTheSameKindGetIdsThatTellThemApart()
    {
        var a = new HidInfo(@"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&aaaa&0&0001#{g}", 0x046D, 0xC52B, 0xFF00, 2, 20, 20, "USB Receiver");
        var b = new HidInfo(@"\\?\hid#vid_046d&pid_c52b&mi_02&col02#7&bbbb&0&0001#{g}", 0x046D, 0xC52B, 0xFF00, 2, 20, 20, "USB Receiver");
        var simA = new Sim { Devices = { [1] = Mouse("Mouse A", soc: 10) } };
        var simB = new Sim { Devices = { [1] = Mouse("Mouse B", soc: 90) } };
        var hid = new RecordingHidSystem([a, b], (a.Path, simA.Handle()), (b.Path, simB.Handle()));

        var all = await Reader(hid).ReadAsync(default);
        Assert.Equal(2, all.Select(r => r.Id).Distinct().Count());
        Assert.All(all, r => Assert.Matches(@"^logitech:c52b:01:[0-9a-f]{6}$", r.Id));
        Assert.Equal([10, 90], all.Select(r => r.Percent!.Value).Order());
    }

    [Fact]
    public async Task ASilentReceiverGivesNothingAndNoError()
    {
        var sim = new Sim();
        Assert.Empty(await Reader(Receiver(sim)).ReadAsync(default));
        Assert.Empty(await Reader(new RecordingHidSystem([])).ReadAsync(default));
    }

    [Fact]
    public async Task ASlotAskedAgainAfterTenMinutesRetriesAnUnreadableNameButNotBefore()
    {
        var d = Mouse();
        d.Answers.Remove((0x0005, 0));                                             // the name feature cannot be read
        var sim = new Sim { Devices = { [1] = d } };
        var time = new FakeTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
        var reader = Reader(Receiver(sim), time);

        await reader.ReadAsync(default);
        sim.Requests.Clear();
        time.Now += TimeSpan.FromMinutes(5);
        await reader.ReadAsync(default);
        Assert.DoesNotContain(sim.Requests, p => p[2] == 0 && ((p[4] << 8) | p[5]) == 0x0005);

        sim.Requests.Clear();
        time.Now += TimeSpan.FromMinutes(6);
        await reader.ReadAsync(default);
        Assert.Contains(sim.Requests, p => p[2] == 0 && ((p[4] << 8) | p[5]) == 0x0005);
    }
}
