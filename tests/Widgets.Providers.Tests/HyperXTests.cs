using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.HyperX;

namespace Widgets.Providers.Tests;

public class HyperXProtocolTests
{
    [Fact]
    public void TheModelTableHasNoPidTwiceAndOnlyTheKingstonCloudIIAndTheAlphaAreBeta()
    {
        var pids = HyperXProtocol.Models.SelectMany(m => m.Pids.Select(p => (m.Vid, p))).Concat(HyperXProtocol.FlightPids.Select(p => (HyperXProtocol.HyperXVid, p))).ToList();
        Assert.Equal(pids.Count, pids.Distinct().Count());
        Assert.Equal([HyperXFamily.Cloud2K, HyperXFamily.Alpha], HyperXProtocol.Models.Where(m => m.Beta).Select(m => m.Family));
        Assert.Equal(5, HyperXProtocol.Models.Length);
    }

    [Fact]
    public void TheFlightRequestIsOneFixedReportOfTwentyBytes()
    {
        var request = HyperXProtocol.FlightRequest();
        Assert.Equal(20, request.Length);
        Assert.Equal([0x21, 0xFF, 0x05], request[..3]);
        Assert.All(request[3..], b => Assert.Equal(0, b));
        Assert.True(HyperXProtocol.IsFlightRequest(request));
        Assert.False(HyperXProtocol.IsFlightRequest([.. request[..19], (byte)1]));
    }

    [Theory]
    [InlineData(3000, 0)]
    [InlineData(3300, 0)]
    [InlineData(3500, 5)]
    [InlineData(3600, 12)]     // 5 + 10 * 100 / 150 = 11.67
    [InlineData(3752, 36)]     // 35.5, which rounds to the even 36
    [InlineData(3756, 36)]     // 36.5, which rounds to the even 36 and not up to 37
    [InlineData(3925, 85)]
    [InlineData(3975, 100)]
    [InlineData(4100, 100)]
    public void TheFlightVoltageIsConvertedOnItsOwnCurve(int mv, int percent) => Assert.Equal(percent, HyperXProtocol.FlightPercent(mv));

    [Fact]
    public void AFlightReplyIsReadAsBigEndianMillivoltsAndOtherReportsAreIgnored()
    {
        Assert.Equal(0x0F3C, HyperXProtocol.ParseFlightReply([0x21, 0xFF, 0x05, 0x0F, 0x3C, 0, 0]));
        Assert.Null(HyperXProtocol.ParseFlightReply([0x21, 0xFF, 0x06, 0x0F, 0x3C]));
        Assert.Null(HyperXProtocol.ParseFlightReply([0x21, 0xFF, 0x05, 0x0F]));
        Assert.Null(HyperXProtocol.ParseFlightReply(null));
    }

    [Fact]
    public void EveryModelRequestHasItsFamiliesLengthAndHead()
    {
        var cloud2 = HyperXProtocol.ModelRequest(HyperXFamily.Cloud2, HyperXProtocol.CmdLevel);
        Assert.Equal(52, cloud2.Length);
        Assert.Equal([0x06, 0xFF, 0xBB, 0x02], cloud2[..4]);

        var kingston = HyperXProtocol.ModelRequest(HyperXFamily.Cloud2K, HyperXProtocol.CmdCharge);
        Assert.Equal(62, kingston.Length);
        Assert.Equal(Convert.FromHexString("060002009A0000684A8E0A000000BB03"), kingston[..16]);

        var alpha = HyperXProtocol.ModelRequest(HyperXFamily.Alpha, HyperXProtocol.AlphaCharge);
        Assert.Equal(31, alpha.Length);
        Assert.Equal([0x21, 0xBB, 0x0C], alpha[..3]);

        var alpha2 = HyperXProtocol.ModelRequest(HyperXFamily.Alpha2, HyperXProtocol.Alpha2Battery);
        Assert.Equal(64, alpha2.Length);
        Assert.Equal([0x50, 0x02], alpha2[..2]);
    }

    [Fact]
    public void AnythingOutsideTheTableIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Cloud2, 0x04));
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Cloud2K, 0x0B));
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Alpha, 0x01));
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Alpha2, 0x52, 0x02));
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Alpha2, 0x01, 0x01));
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Cloud2));            // no command at all
        Assert.Throws<InvalidOperationException>(() => HyperXProtocol.ModelRequest(HyperXFamily.Alpha2, 0x52));      // one byte of two
        Assert.False(HyperXProtocol.IsAllowedModelRequest(new byte[52]));
    }

    private static byte[] Pad(int length, params byte[] head) => [.. head, .. new byte[length - head.Length]];

    [Fact]
    public void AReplyIsMatchedByFamilyAndCommand()
    {
        Assert.NotNull(HyperXProtocol.MatchReply(HyperXFamily.Cloud2, Pad(20, 0x06, 0xFF, 0xBB, 0x02), 0x02));
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Cloud2, Pad(20, 0x06, 0xFF, 0xBB, 0x03), 0x02));
        Assert.NotNull(HyperXProtocol.MatchReply(HyperXFamily.Cloud2K, Pad(64, 0x0B, 0x00, 0xBB, 0x02), 0x02));
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Cloud2K, Pad(64, 0x0C, 0x00, 0xBB, 0x02), 0x02));
        Assert.NotNull(HyperXProtocol.MatchReply(HyperXFamily.Alpha, Pad(31, 0x21, 0xBB, 0x0B), 0x0B));
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Alpha, Pad(31, 0x21, 0xBB, 0x03), 0x0B));
        Assert.NotNull(HyperXProtocol.MatchReply(HyperXFamily.Alpha2, Pad(64, 0x53, 0x01), 0x52, 0x01));            // the reply id is the command plus one
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Alpha2, Pad(64, 0x51, 0x01), 0x52, 0x01));
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Alpha2, Pad(64, 0xFF, 0x01), 0x52, 0x01));               // the unrelated stream of the Flight 2
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Alpha2, [0x53, 0x01, 0, 0], 0x52, 0x01));                // too short
        Assert.Null(HyperXProtocol.MatchReply(HyperXFamily.Alpha2, null, 0x52, 0x01));
    }

    [Fact]
    public void ACollectionMustMatchPageUsageAndBothLengthsExactly()
    {
        HidInfo I(int page, int usage, int inLen, int outLen, int vid = 0x03F0, int pid = 0x08BE) => new("p", vid, pid, page, usage, inLen, outLen, "");
        var alpha2 = HyperXProtocol.Models[3];

        Assert.True(HyperXProtocol.CollectionOk(alpha2, I(0xFF13, 0xFF00, 64, 64)));
        Assert.False(HyperXProtocol.CollectionOk(alpha2, I(0xFF13, 0xFF01, 64, 64)));
        Assert.False(HyperXProtocol.CollectionOk(alpha2, I(0xFF14, 0xFF00, 64, 64)));
        Assert.False(HyperXProtocol.CollectionOk(alpha2, I(0xFF13, 0xFF00, 65, 64)));
        Assert.False(HyperXProtocol.CollectionOk(alpha2, I(0xFF13, 0xFF00, 64, 32)));

        Assert.True(HyperXProtocol.FlightCollectionOk(I(0xFF53, 0x0001, 20, 20, 0x0951, 0x16C4)));
        Assert.False(HyperXProtocol.FlightCollectionOk(I(0xFF53, 0x0001, 20, 64, 0x0951, 0x16C4)));
        Assert.False(HyperXProtocol.FlightCollectionOk(I(0xFF00, 0x0001, 20, 20, 0x0951, 0x16C4)));
    }
}

public class HyperXReaderTests
{
    private static HidInfo Info(int vid, int pid, int page, int usage, int inLen, int outLen, string path, string product = "") =>
        new(path, vid, pid, page, usage, inLen, outLen, product);

    private static HyperXReader Reader(IHidSystem hid, bool experimental = false, double seconds = 0.4) =>
        new(hid, TimeSpan.FromSeconds(seconds), () => experimental);

    private static byte[] Pad(int length, params byte[] head) => [.. head, .. new byte[length - head.Length]];

    /// <summary>A dongle that answers each request frame by <paramref name="respond"/> (null is silence). Every frame sent must be on a table.</summary>
    private sealed class Sim(Func<byte[], byte[]?> respond, bool noise = false)
    {
        private readonly Queue<byte[]> _queue = new();
        public List<byte[]> Requests { get; } = [];

        public RecordingHandle Handle(Func<byte, int, byte[]?>? onInputReport = null) => new(
            read: () => _queue.Count > 0 ? _queue.Dequeue() : null,
            onWrite: frame =>
            {
                Requests.Add(frame);
                Assert.True(HyperXProtocol.IsFlightRequest(frame) || HyperXProtocol.IsAllowedModelRequest(frame));
                if (noise) { _queue.Enqueue(Pad(64, 0xFF, 0x01, 0x07)); _queue.Enqueue(Pad(64, 0x45, 0x02, 0x07)); }   // reports of another kind
                if (respond(frame) is { } reply) _queue.Enqueue(reply);
                return true;
            },
            onGetInputReport: onInputReport);
    }

    // ---- Cloud Flight

    private static byte[] FlightReply(int mv) => Pad(20, 0x21, 0xFF, 0x05, (byte)(mv >> 8), (byte)(mv & 0xFF));
    private const string FlightPath = @"\\?\hid#vid_0951&pid_16c4&mi_03#7&1&0000#{g}";
    private static readonly HidInfo FlightInfo = Info(0x0951, 0x16C4, 0xFF53, 1, 20, 20, FlightPath);

    [Fact]
    public async Task ACloudFlightIsReadFromItsVoltage()
    {
        var sim = new Sim(_ => FlightReply(3925), noise: true);
        var hid = new RecordingHidSystem([FlightInfo], (FlightPath, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(("hyperx:16c4", "HyperX Cloud Flight Wireless", DeviceKind.Headset, 85, false, true), (r.Id, r.Name, r.Kind, r.Percent, r.Charging, r.Connected));
        Assert.Equal(FlightReplyRequest(), Assert.Single(sim.Requests));
    }

    private static byte[] FlightReplyRequest() => HyperXProtocol.FlightRequest();

    [Fact]
    public async Task WhileChargingTheFlightKeepsTheLastLevelAndASilentDongleIsOff()
    {
        var mv = 4200;
        var sim = new Sim(_ => FlightReply(mv));
        var reader = Reader(new RecordingHidSystem([FlightInfo], (FlightPath, sim.Handle())));

        var first = Assert.Single(await reader.ReadAsync(default));
        Assert.Equal((null, true, true), (first.Percent, first.Charging, first.Connected));       // charging from the start: no level to keep

        mv = 3925;
        Assert.Equal(85, Assert.Single(await reader.ReadAsync(default)).Percent);
        mv = 4200;
        var charging = Assert.Single(await reader.ReadAsync(default));
        Assert.Equal((85, true), (charging.Percent, charging.Charging));

        var silent = new Sim(_ => null);
        var off = Assert.Single(await Reader(new RecordingHidSystem([FlightInfo], (FlightPath, silent.Handle())), seconds: 0.3).ReadAsync(default));
        Assert.Equal((false, null), (off.Connected, off.Percent));                                // on this model silence means off
    }

    [Fact]
    public async Task AFlightCollectionOfTheWrongShapeIsIgnored()
    {
        var wrong = Info(0x0951, 0x16C4, 0xFF53, 1, 64, 64, FlightPath);
        var hid = new RecordingHidSystem([wrong], (FlightPath, new Sim(_ => FlightReply(3925)).Handle()));
        Assert.Empty(await Reader(hid).ReadAsync(default));
        Assert.Empty(hid.Opened);
    }

    // ---- Cloud II (HP revision)

    private const string Cloud2Path = @"\\?\hid#vid_03f0&pid_0696&mi_03#7&1&0000#{g}";
    private static readonly HidInfo Cloud2Info = Info(0x03F0, 0x0696, 0xFF90, 0x0303, 20, 52, Cloud2Path, "HyperX Cloud II Wireless");

    [Fact]
    public async Task ACloudIIIsReadForItsLevelAndItsChargeState()
    {
        var sim = new Sim(f => f[3] == 0x02 ? Pad(20, 0x06, 0xFF, 0xBB, 0x02, 0, 0, 0, 64) : Pad(20, 0x06, 0xFF, 0xBB, 0x03, 1));
        var r = Assert.Single(await Reader(new RecordingHidSystem([Cloud2Info], (Cloud2Path, sim.Handle()))).ReadAsync(default));

        Assert.Equal(("hyperx:0696", 64, true, true), (r.Id, r.Percent, r.Charging, r.Connected));
        Assert.Equal([0x02, 0x03], sim.Requests.Select(f => (int)f[3]));
        Assert.All(sim.Requests, f => Assert.Equal(52, f.Length));
    }

    [Fact]
    public async Task ACloudIIThatIsSilentOrReportsAnImpossibleLevelGivesNothingAndAMissingChargeAnswerIsNotCharging()
    {
        Assert.Empty(await Reader(new RecordingHidSystem([Cloud2Info], (Cloud2Path, new Sim(_ => null).Handle())), seconds: 0.3).ReadAsync(default));
        Assert.Empty(await Reader(new RecordingHidSystem([Cloud2Info], (Cloud2Path, new Sim(f => f[3] == 0x02 ? Pad(20, 0x06, 0xFF, 0xBB, 0x02, 0, 0, 0, 101) : null).Handle()))).ReadAsync(default));

        var noCharge = new Sim(f => f[3] == 0x02 ? Pad(20, 0x06, 0xFF, 0xBB, 0x02, 0, 0, 0, 50) : null);
        var r = Assert.Single(await Reader(new RecordingHidSystem([Cloud2Info], (Cloud2Path, noCharge.Handle())), seconds: 0.5).ReadAsync(default));
        Assert.Equal((50, false), (r.Percent, r.Charging));
    }

    // ---- Cloud II (Kingston revision), beta

    private const string KingstonPath = @"\\?\hid#vid_0951&pid_1718&mi_00#7&1&0000#{g}";
    private static readonly HidInfo KingstonInfo = Info(0x0951, 0x1718, 0xFF00, 0x0001, 64, 62, KingstonPath);

    [Fact]
    public async Task TheKingstonCloudIIIsOnlyReadWithExperimentalReadersAndGetsItsHarmlessPreRead()
    {
        var sim = new Sim(f => f[15] == 0x02 ? Pad(64, 0x0B, 0, 0xBB, 0x02, 0, 0, 0, 40) : Pad(64, 0x0B, 0, 0xBB, 0x03, 0));
        var handle = sim.Handle(onInputReport: (_, _) => null);
        var hid = new RecordingHidSystem([KingstonInfo], (KingstonPath, handle));

        Assert.Empty(await Reader(hid, experimental: false).ReadAsync(default));
        Assert.Empty(hid.Opened);

        var r = Assert.Single(await Reader(hid, experimental: true).ReadAsync(default));
        Assert.Equal((40, false), (r.Percent, r.Charging));
        Assert.Equal([(0x06, 64)], handle.InputReportRequests);
        Assert.All(sim.Requests, f => Assert.Equal(62, f.Length));
    }

    // ---- Cloud Alpha, beta

    private const string AlphaPath = @"\\?\hid#vid_03f0&pid_098d&mi_00#7&1&0000#{g}";
    private static readonly HidInfo AlphaInfo = Info(0x03F0, 0x098D, 0xFF00, 0x0001, 31, 31, AlphaPath);

    private sealed class AlphaState { public int Connection; public int Charging; public int Level = 55; }

    private static Sim AlphaSim(AlphaState s) => new(f => f[2] switch
    {
        0x03 => Pad(31, 0x21, 0xBB, 0x03, (byte)s.Connection),
        0x0C => Pad(31, 0x21, 0xBB, 0x0C, (byte)s.Charging),
        0x0B => Pad(31, 0x21, 0xBB, 0x0B, (byte)s.Level),
        _ => null,
    });

    [Fact]
    public async Task AnAlphaIsOffWhenNotLinkedAndKeepsItsLastLevelWhileCharging()
    {
        var state = new AlphaState();
        var sim = AlphaSim(state);
        var reader = Reader(new RecordingHidSystem([AlphaInfo], (AlphaPath, sim.Handle())), experimental: true);

        var normal = Assert.Single(await reader.ReadAsync(default));
        Assert.Equal((55, false, true), (normal.Percent, normal.Charging, normal.Connected));
        Assert.Equal([0x03, 0x0C, 0x0B], sim.Requests.Select(f => (int)f[2]));

        state.Charging = 1;
        sim.Requests.Clear();
        var charging = Assert.Single(await reader.ReadAsync(default));
        Assert.Equal((55, true), (charging.Percent, charging.Charging));
        Assert.Equal([0x03, 0x0C], sim.Requests.Select(f => (int)f[2]));                          // no level query while charging

        state.Connection = 1;
        sim.Requests.Clear();
        var off = Assert.Single(await reader.ReadAsync(default));
        Assert.Equal((false, null), (off.Connected, off.Percent));
        Assert.Single(sim.Requests);                                                              // it stopped at the connection answer
    }

    [Fact]
    public async Task AnAlphaIsNotReadWithoutExperimentalReadersAndAnImpossibleLevelGivesNothing()
    {
        var state = new AlphaState { Level = 120 };
        var hid = new RecordingHidSystem([AlphaInfo], (AlphaPath, AlphaSim(state).Handle()));
        Assert.Empty(await Reader(hid, experimental: false).ReadAsync(default));
        Assert.Empty(hid.Opened);
        Assert.Empty(await Reader(hid, experimental: true).ReadAsync(default));
    }

    // ---- Cloud Alpha 2 and Cloud Flight 2

    [Theory]
    [InlineData(0x08BE, 0xFF13, "HyperX Cloud Alpha 2 Wireless")]
    [InlineData(0x0AC1, 0xFF90, "HyperX Cloud Flight 2 Wireless")]
    public async Task AnAlpha2ProtocolHeadsetIsReadAndNamedFromTheTableWhenWindowsGivesNoName(int pid, int page, string name)
    {
        var path = $@"\\?\hid#vid_03f0&pid_{pid:x4}&mi_00#7&1&0000#{{g}}";
        var sim = new Sim(f => f[0] == 0x52 ? Pad(64, 0x53, 0x01, 0x01) : Pad(64, 0x51, 0x02, 88, 1), noise: true);
        var hid = new RecordingHidSystem([Info(0x03F0, pid, page, 0xFF00, 64, 64, path)], (path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(($"hyperx:{pid:x4}", name, 88, true), (r.Id, r.Name, r.Percent, r.Charging));
        Assert.Equal([0x52, 0x50], sim.Requests.Select(f => (int)f[0]));
    }

    [Fact]
    public async Task AnAlpha2HeadsetThatIsNotLinkedIsOffAndSilenceGivesNothing()
    {
        const string path = @"\\?\hid#vid_03f0&pid_08be&mi_00#7&1&0000#{g}";
        var info = Info(0x03F0, 0x08BE, 0xFF13, 0xFF00, 64, 64, path);

        var off = new Sim(_ => Pad(64, 0x53, 0x01, 0x00));
        Assert.False(Assert.Single(await Reader(new RecordingHidSystem([info], (path, off.Handle()))).ReadAsync(default)).Connected);
        Assert.Single(off.Requests);

        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, new Sim(_ => null).Handle())), seconds: 0.3).ReadAsync(default));
        Assert.Empty(await Reader(new RecordingHidSystem([info], (path, new Sim(f => f[0] == 0x52 ? Pad(64, 0x53, 0x01, 0x01) : Pad(64, 0x51, 0x02, 101)).Handle()))).ReadAsync(default));
    }

    [Theory]
    [InlineData(0x0951, 0x16C4, true)]
    [InlineData(0x0951, 0x1723, true)]
    [InlineData(0x03F0, 0x0696, true)]
    [InlineData(0x03F0, 0x0AC1, true)]
    [InlineData(0x0951, 0x1718, true)]     // experimental, but known
    [InlineData(0x03F0, 0x16C4, false)]    // a product id of the other vendor
    [InlineData(0x0951, 0x0696, false)]
    [InlineData(0x03F0, 0x0001, false)]
    public void TheRegistryKnowsTheHyperXDongles(int vid, int pid, bool expected) => Assert.Equal(expected, SupportedDevices.Supports(vid, pid));

    [Fact]
    public async Task OneReadingPerVendorAndProductAndTheOtherVendorsAreBothScanned()
    {
        const string second = @"\\?\hid#vid_0951&pid_16c4&mi_04#7&1&0000#{g}";
        var other = Info(0x0951, 0x16C4, 0xFF53, 1, 20, 20, second);
        var cloud2 = new Sim(f => f[3] == 0x02 ? Pad(20, 0x06, 0xFF, 0xBB, 0x02, 0, 0, 0, 64) : Pad(20, 0x06, 0xFF, 0xBB, 0x03, 0));
        var hid = new RecordingHidSystem([FlightInfo, other, Cloud2Info],
            (FlightPath, new Sim(_ => FlightReply(3925)).Handle()), (second, new Sim(_ => FlightReply(3925)).Handle()), (Cloud2Path, cloud2.Handle()));

        var readings = await Reader(hid).ReadAsync(default);
        Assert.Equal(["hyperx:0696", "hyperx:16c4"], readings.Select(r => r.Id).Order());       // the HP and the HyperX vendor are both read
        Assert.DoesNotContain(second, hid.Opened);
    }
}
