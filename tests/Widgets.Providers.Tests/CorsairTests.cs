using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Corsair;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Tests;

public class CorsairProtocolTests
{
    [Fact]
    public void OnlyTheThreeV2ModelsAreBetaAndTheFamiliesAreAsListed()
    {
        Assert.Equal([0x0A97, 0x2A02, 0x2A08], CorsairProtocol.Models.Where(m => m.Value.Beta).Select(m => m.Key).Order());
        Assert.All(CorsairProtocol.Models.Where(m => m.Value.Beta), m => Assert.Equal(CorsairFamily.V2, m.Value.Family));
        Assert.Equal(CorsairFamily.Xt, CorsairProtocol.Models[0x0A64].Family);
        Assert.True(CorsairProtocol.Models[0x0A62].Wired);
        Assert.False(CorsairProtocol.Models[0x0A3E].Wired);
        Assert.Equal(CorsairFamily.Void, CorsairProtocol.Models[0x0A14].Family);
    }

    [Fact]
    public void OnlyAWhitelistedGetCanBeBuilt()
    {
        var frame = CorsairProtocol.BragiRequest(CorsairProtocol.TargetHeadset, CorsairProtocol.PropBattery);
        Assert.Equal(64, frame.Length);
        Assert.Equal([0x02, 0x09, 0x02, 0x0F], frame[..4]);
        Assert.All(frame[4..], b => Assert.Equal(0, b));

        foreach (byte prop in new byte[] { CorsairProtocol.PropBattery, CorsairProtocol.PropCharge, CorsairProtocol.PropHeartbeat, CorsairProtocol.PropFirmware })
            foreach (byte target in new byte[] { CorsairProtocol.TargetSelf, CorsairProtocol.TargetHeadset })
                CorsairProtocol.BragiRequest(target, prop);

        Assert.Throws<InvalidOperationException>(() => CorsairProtocol.BragiRequest(0x0A, CorsairProtocol.PropBattery));      // another target
        Assert.Throws<InvalidOperationException>(() => CorsairProtocol.BragiRequest(CorsairProtocol.TargetSelf, 0x14));       // another property
        Assert.Throws<InvalidOperationException>(() => CorsairProtocol.BragiRequest(CorsairProtocol.TargetSelf, CorsairProtocol.PropBattery, command: 0x01));
        Assert.Throws<InvalidOperationException>(() => CorsairProtocol.BragiRequest(CorsairProtocol.TargetSelf, CorsairProtocol.PropBattery, command: 0x03));
    }

    [Fact]
    public void AFrameGetsItsLeadingZeroWhenTheCollectionHasNoReportIdsAndIsPaddedToTheOutputLength()
    {
        var withIds = CorsairProtocol.BragiFrame(0x09, 0x0F, reportIds: true, outputLength: 64);
        Assert.Equal(64, withIds.Length);
        Assert.Equal(0x02, withIds[0]);

        var none = CorsairProtocol.BragiFrame(0x09, 0x0F, reportIds: false, outputLength: 65);
        Assert.Equal(65, none.Length);
        Assert.Equal([0x00, 0x02, 0x09, 0x02, 0x0F], none[..5]);

        Assert.Equal(128, CorsairProtocol.BragiFrame(0x09, 0x0F, true, 128).Length);
    }

    [Fact]
    public void TheWireCheckAcceptsAGetAndNothingElse()
    {
        Assert.True(CorsairProtocol.IsAllowedWire(CorsairProtocol.BragiFrame(0x09, 0x0F, true, 64), true));
        Assert.True(CorsairProtocol.IsAllowedWire(CorsairProtocol.BragiFrame(0x08, 0x13, false, 65), false));
        Assert.True(CorsairProtocol.IsAllowedWire(CorsairProtocol.BragiFrame(0x09, 0x0F, true, 96), true));

        var extra = CorsairProtocol.BragiFrame(0x09, 0x0F, true, 64);
        extra[30] = 1;                                                                 // a payload the request does not have
        Assert.False(CorsairProtocol.IsAllowedWire(extra, true));

        var set = CorsairProtocol.BragiFrame(0x09, 0x0F, true, 64);
        set[2] = 0x01;
        Assert.False(CorsairProtocol.IsAllowedWire(set, true));

        var otherProp = CorsairProtocol.BragiFrame(0x09, 0x0F, true, 64);
        otherProp[3] = 0x20;
        Assert.False(CorsairProtocol.IsAllowedWire(otherProp, true));

        Assert.False(CorsairProtocol.IsAllowedWire(CorsairProtocol.BragiRequest(0x09, 0x0F)[..40], true));                // too short
        Assert.False(CorsairProtocol.IsAllowedWire(CorsairProtocol.BragiRequest(0x09, 0x0F), false));                     // no leading zero
        Assert.False(CorsairProtocol.IsAllowedWire([], false));
    }

    [Fact]
    public void TheVoidRequestIsTwoBytesPaddedToTheOutputLength()
    {
        Assert.Equal([0xC9, 0x64], CorsairProtocol.VoidRequest(2));
        Assert.Equal(64, CorsairProtocol.VoidRequest(64).Length);
        Assert.Equal(2, CorsairProtocol.VoidRequest(0).Length);
        Assert.True(CorsairProtocol.IsVoidRequest(CorsairProtocol.VoidRequest(64)));
        Assert.False(CorsairProtocol.IsVoidRequest([0xC9, 0x65]));
        Assert.False(CorsairProtocol.IsVoidRequest([0xC9, 0x64, 0x01]));
    }

    [Fact]
    public void AVoidReplyIsOfflineOkChargingOrNotOurs()
    {
        Assert.Equal((CorsairProtocol.VoidState.Offline, (int?)null, false), CorsairProtocol.ParseVoidReply([0x64, 0, 0, 0, 0])!.Value);
        Assert.Equal((CorsairProtocol.VoidState.Ok, (int?)70, false), CorsairProtocol.ParseVoidReply([0x64, 0, 70, 0, 1])!.Value);
        Assert.Equal((CorsairProtocol.VoidState.Ok, (int?)70, true), CorsairProtocol.ParseVoidReply([0x64, 0, 70 | 0x80, 0, 2])!.Value);     // the mic flag is not the level
        Assert.Equal(CorsairProtocol.VoidState.Charging, CorsairProtocol.ParseVoidReply([0x64, 0, 30, 0, 4])!.Value.State);
        Assert.Equal(CorsairProtocol.VoidState.Charging, CorsairProtocol.ParseVoidReply([0x64, 0, 30, 0, 5])!.Value.State);

        Assert.Null(CorsairProtocol.ParseVoidReply([0x64, 0, 70, 0, 3]));              // a status that is not listed
        Assert.Null(CorsairProtocol.ParseVoidReply([0x64, 0, 101, 0, 1]));
        Assert.Null(CorsairProtocol.ParseVoidReply([0x65, 0, 70, 0, 1]));
        Assert.Null(CorsairProtocol.ParseVoidReply([0x64, 0, 70, 0]));
        Assert.Null(CorsairProtocol.ParseVoidReply(null));
    }

    [Fact]
    public void ABragiReplyIsMatchedByTargetAndAMissingReportIdIsStripped()
    {
        byte[] Reply(byte source, byte status = 0, params byte[] value) => [0x01, source, 0x02, status, .. value, .. new byte[56]];

        Assert.NotNull(CorsairProtocol.MatchBragi(Reply(1, 0, 0xEB, 0x02), CorsairProtocol.TargetHeadset));
        Assert.NotNull(CorsairProtocol.MatchBragi(Reply(0), CorsairProtocol.TargetSelf));
        Assert.Null(CorsairProtocol.MatchBragi(Reply(0), CorsairProtocol.TargetHeadset));                    // from the other source
        Assert.NotNull(CorsairProtocol.MatchBragi(Reply(0), CorsairProtocol.TargetHeadset, strictSource: false));
        Assert.NotNull(CorsairProtocol.MatchBragi([0x00, .. Reply(1)], CorsairProtocol.TargetHeadset));      // a leading 0 before the id

        Assert.Null(CorsairProtocol.MatchBragi([0x01, 0x01, 0x03, 0, 0, 0, 0, 0], CorsairProtocol.TargetHeadset));   // not a GET reply
        Assert.Null(CorsairProtocol.MatchBragi([0x01, 0x01, 0x02], CorsairProtocol.TargetHeadset));                   // too short
        Assert.Null(CorsairProtocol.MatchBragi(null, CorsairProtocol.TargetHeadset));

        Assert.Equal(0x02EB, CorsairProtocol.BragiValue(Reply(1, 0, 0xEB, 0x02), 2));
        Assert.Equal(0x0201_02EB, CorsairProtocol.BragiValue(Reply(1, 0, 0xEB, 0x02, 0x01, 0x02)));
    }

    [Fact]
    public void ACollectionIsAcceptedPerFamily()
    {
        HidInfo I(int page, int usage, int inLen, int outLen) => new("p", 0x1B1C, 0, page, usage, inLen, outLen, "");
        var voidModel = CorsairProtocol.Models[0x0A14];
        var xt = CorsairProtocol.Models[0x0A64];
        var v2 = CorsairProtocol.Models[0x2A08];

        Assert.True(CorsairProtocol.CollectionOk(voidModel, I(0xFFC5, 1, 5, 2)));
        Assert.False(CorsairProtocol.CollectionOk(voidModel, I(0xFFC5, 1, 4, 2)));
        Assert.False(CorsairProtocol.CollectionOk(voidModel, I(0xFF42, 1, 64, 64)));

        Assert.True(CorsairProtocol.CollectionOk(xt, I(0xFF42, 1, 64, 64)));
        Assert.False(CorsairProtocol.CollectionOk(xt, I(0xFF42, 1, 32, 64)));
        Assert.False(CorsairProtocol.CollectionOk(xt, I(0xFF42, 2, 64, 64)));

        Assert.True(CorsairProtocol.CollectionOk(v2, I(0xFF00, 1, 64, 64)));
        Assert.True(CorsairProtocol.CollectionOk(v2, I(0xFF42, 3, 65, 65)));
        Assert.False(CorsairProtocol.CollectionOk(v2, I(0x0001, 1, 64, 64)));
        Assert.False(CorsairProtocol.CollectionOk(v2, I(0xFF00, 1, 64, 32)));
    }
}

public class CorsairReaderTests
{
    private static HidInfo Info(int pid, int page, int usage, int len, string path, string product = "") =>
        new(path, 0x1B1C, pid, page, usage, len, len, product);

    private static CorsairReader Reader(IHidSystem hid, bool experimental = false, double seconds = 0.6) =>
        new(hid, TimeSpan.FromSeconds(seconds), () => experimental);

    // ---- Void

    private sealed class VoidSim(int level = 70, bool mic = false, int status = 1, bool silent = false, bool refuseWrite = false)
    {
        private readonly Queue<byte[]> _queue = new();
        public List<byte[]> Requests { get; } = [];

        public RecordingHandle Handle() => new(
            read: () => _queue.Count > 0 ? _queue.Dequeue() : null,
            onWrite: frame =>
            {
                Requests.Add(frame);
                Assert.True(CorsairProtocol.IsVoidRequest(frame));
                if (refuseWrite) return false;
                if (!silent) _queue.Enqueue([0x64, 0, (byte)(level | (mic ? 0x80 : 0)), 0, (byte)status]);
                return true;
            });
    }

    [Fact]
    public async Task AVoidHeadsetIsReadAndTheMicFlagIsNotTheLevel()
    {
        const string path = @"\\?\hid#vid_1b1c&pid_0a14&mi_03#7&1&0000#{g}";
        var sim = new VoidSim(level: 70, mic: true, status: 4);
        var hid = new RecordingHidSystem([Info(0x0A14, 0xFFC5, 1, 16, path, "Corsair VOID Wireless")], (path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(("corsair:0a14", "Corsair VOID Wireless", DeviceKind.Headset, 70, true, true), (r.Id, r.Name, r.Kind, r.Percent, r.Charging, r.Connected));
        Assert.Equal(16, Assert.Single(sim.Requests).Length);                           // padded to the output report length
    }

    [Fact]
    public async Task AVoidReceiverWithNoHeadsetIsOffAndASilentOrRefusingOneGivesNothing()
    {
        const string path = @"\\?\hid#vid_1b1c&pid_0a14&mi_03#7&1&0000#{g}";
        HidInfo info = Info(0x0A14, 0xFFC5, 1, 16, path);

        var off = Assert.Single(await Reader(new RecordingHidSystem([info], (path, new VoidSim(status: 0).Handle()))).ReadAsync(default));
        Assert.False(off.Connected);
        Assert.Null(off.Percent);
        Assert.Equal("Corsair Void", off.Name);                                         // no product string: the table's name

        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, new VoidSim(silent: true).Handle())), seconds: 0.3).ReadAsync(default));
        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, new VoidSim(refuseWrite: true).Handle()))).ReadAsync(default));
    }

    // ---- Bragi

    /// <summary>A Bragi device. <paramref name="answers"/> says what each (target, property) replies, or null for silence.</summary>
    private sealed class BragiSim(Func<byte, byte, (int Status, byte[] Value)?> answers, bool leadingZeroOnReplies = false)
    {
        private readonly Queue<byte[]> _queue = new();
        public List<byte[]> Requests { get; } = [];
        public List<(byte Target, byte Prop)> Gets { get; } = [];

        public RecordingHandle Handle(bool reportIds) => new(
            read: () => _queue.Count > 0 ? _queue.Dequeue() : null,
            onWrite: frame =>
            {
                Requests.Add(frame);
                Assert.True(CorsairProtocol.IsAllowedWire(frame, reportIds));
                var body = reportIds ? frame : frame[1..];
                var (target, prop) = (body[1], body[3]);
                Gets.Add((target, prop));
                if (answers(target, prop) is { } a)
                {
                    byte[] reply = [0x01, (byte)(target == 0x08 ? 0 : 1), 0x02, (byte)a.Status, .. a.Value, .. new byte[64 - 4 - a.Value.Length]];
                    _queue.Enqueue(leadingZeroOnReplies ? [0x00, .. reply] : reply);
                }
                return true;
            });
    }

    private static byte[] Tenths(int value) => BitConverter.GetBytes(value);

    [Fact]
    public async Task AWirelessXtIsAskedTheHeadsetFirstAndTheBatteryIsInTenths()
    {
        const string path = @"\\?\hid#vid_1b1c&pid_0a64&mi_04#7&1&0000#{g}";
        var sim = new BragiSim((t, p) => p == 0x0F ? (0, Tenths(755)) : (0, Tenths(1)));
        var hid = new RecordingHidSystem([Info(0x0A64, 0xFF42, 1, 64, path)], (path, sim.Handle(true)));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(("corsair:0a64", "Corsair Virtuoso XT", 75, true), (r.Id, r.Name, r.Percent, r.Charging));
        Assert.Equal([(0x09, 0x0F), (0x09, 0x10)], sim.Gets);
    }

    [Fact]
    public async Task AWiredXtIsAskedTheUnitItselfFirstAndAnotherTargetIsTriedWhenTheFirstIsSilent()
    {
        const string wiredPath = @"\\?\hid#vid_1b1c&pid_0a62&mi_04#7&1&0000#{g}";
        var wired = new BragiSim((t, p) => (0, Tenths(p == 0x0F ? 400 : 0)));
        var r = Assert.Single(await Reader(new RecordingHidSystem([Info(0x0A62, 0xFF42, 1, 64, wiredPath)], (wiredPath, wired.Handle(true)))).ReadAsync(default));
        Assert.Equal((40, false), (r.Percent, r.Charging));
        Assert.Equal((byte)0x08, wired.Gets[0].Target);

        const string path = @"\\?\hid#vid_1b1c&pid_0a64&mi_04#7&1&0000#{g}";
        var onlySelf = new BragiSim((t, p) => t == 0x08 ? ((byte)0, Tenths(p == 0x0F ? 900 : 0)) : null);
        var fallback = Assert.Single(await Reader(new RecordingHidSystem([Info(0x0A64, 0xFF42, 1, 64, path)], (path, onlySelf.Handle(true))), seconds: 2).ReadAsync(default));
        Assert.Equal(90, fallback.Percent);
        Assert.Equal([0x09, 0x08], onlySelf.Gets.Take(2).Select(g => (int)g.Target));
    }

    [Fact]
    public async Task AnErrorStatusFromTheExpectedTargetIsOffButSilenceAndAnImpossibleLevelAreNot()
    {
        const string path = @"\\?\hid#vid_1b1c&pid_0a64&mi_04#7&1&0000#{g}";
        var info = Info(0x0A64, 0xFF42, 1, 64, path);

        var refused = new BragiSim((t, p) => t == 0x09 ? (1, []) : null);
        var off = Assert.Single(await Reader(new RecordingHidSystem([info], (path, refused.Handle(true))), seconds: 2).ReadAsync(default));
        Assert.False(off.Connected);

        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, new BragiSim((t, p) => null).Handle(true))), seconds: 2).ReadAsync(default));

        var absurd = new BragiSim((t, p) => (0, Tenths(1001)));
        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, absurd.Handle(true))), seconds: 2).ReadAsync(default));
    }

    // ---- V2

    private static (byte, byte[])? V2Answers(byte target, byte prop, int battery = 800, byte heartbeatStatus = 0, byte batteryStatus = 0) => (target, prop) switch
    {
        (0x09, 0x12) => (heartbeatStatus, []),
        (0x09, 0x0F) => (batteryStatus, BitConverter.GetBytes((short)battery)),
        _ => null,
    };

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task AV2HeadsetIsOnlyReadWhenExperimentalReadersAreOnAndWakesTheReceiverWithReadsOnly(int length, bool reportIds)
    {
        const string path = @"\\?\hid#vid_1b1c&pid_2a08&mi_03#7&1&0000#{g}";
        var sim = new BragiSim((t, p) => V2Answers(t, p), leadingZeroOnReplies: !reportIds);
        var hid = new RecordingHidSystem([Info(0x2A08, 0xFF00, 1, length, path)], (path, sim.Handle(reportIds)));

        Assert.Empty(await Reader(hid, experimental: false).ReadAsync(default));
        Assert.Empty(hid.Opened);

        var r = Assert.Single(await Reader(hid, experimental: true).ReadAsync(default));
        Assert.Equal(("corsair:2a08", "Corsair Void Wireless V2", 80, false, true), (r.Id, r.Name, r.Percent, r.Charging, r.Connected));
        Assert.Equal([(0x08, 0x13), (0x08, 0x12), (0x09, 0x12), (0x09, 0x0F)], sim.Gets);        // firmware, heartbeat, then the headset's own
        Assert.All(sim.Requests, f => Assert.Equal(length, f.Length));
    }

    [Fact]
    public async Task AV2HeadsetThatRefusesItsHeartbeatIsOffAndOneThatIsSilentGivesNothing()
    {
        const string path = @"\\?\hid#vid_1b1c&pid_2a02&mi_03#7&1&0000#{g}";
        var info = Info(0x2A02, 0xFF00, 1, 64, path);

        var off = new BragiSim((t, p) => V2Answers(t, p, heartbeatStatus: 1));
        var r = Assert.Single(await Reader(new RecordingHidSystem([info], (path, off.Handle(true))), experimental: true).ReadAsync(default));
        Assert.False(r.Connected);
        Assert.Equal(3, off.Gets.Count);                                                          // it stopped at the heartbeat

        var silent = new BragiSim((t, p) => null);
        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, silent.Handle(true))), experimental: true).ReadAsync(default));
    }

    [Fact]
    public async Task AV2BatteryThatKeepsAnsweringWithAnErrorIsTriedThreeTimesThenShownWithNoLevel()
    {
        const string path = @"\\?\hid#vid_1b1c&pid_0a97&mi_03#7&1&0000#{g}";
        var sim = new BragiSim((t, p) => V2Answers(t, p, batteryStatus: 3));
        var hid = new RecordingHidSystem([Info(0x0A97, 0xFF00, 1, 64, path)], (path, sim.Handle(true)));

        var r = Assert.Single(await Reader(hid, experimental: true).ReadAsync(default));
        Assert.True(r.Connected);
        Assert.Null(r.Percent);
        Assert.Equal(3, sim.Gets.Count(g => g.Prop == 0x0F));
    }

    [Theory]
    [InlineData(0x1B1C, 0x0A14, true)]
    [InlineData(0x1B1C, 0x0A64, true)]
    [InlineData(0x1B1C, 0x2A08, true)]     // experimental, but known
    [InlineData(0x1B1C, 0x0001, false)]
    [InlineData(0x1532, 0x0A64, false)]
    public void TheRegistryKnowsTheCorsairReceivers(int vid, int pid, bool expected) => Assert.Equal(expected, SupportedDevices.Supports(vid, pid));

    [Fact]
    public async Task OnlyOneReadingPerProductAndAWrongCollectionIsIgnored()
    {
        const string good = @"\\?\hid#vid_1b1c&pid_0a14&mi_03#7&1&0000#{g}";
        const string other = @"\\?\hid#vid_1b1c&pid_0a14&mi_04#7&1&0000#{g}";
        const string wrong = @"\\?\hid#vid_1b1c&pid_0a14&mi_01#7&1&0000#{g}";
        var sim = new VoidSim();
        var hid = new RecordingHidSystem(
            [Info(0x0A14, 0xFFC5, 1, 16, good), Info(0x0A14, 0xFFC5, 1, 16, other), Info(0x0A14, 0xFF42, 1, 64, wrong)],
            (good, sim.Handle()), (other, new VoidSim().Handle()), (wrong, new VoidSim().Handle()));

        Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal([good], hid.Opened);
    }
}
