using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.Logitech;

namespace Widgets.Providers.Tests;

public class LogitechHeadsetProtocolTests
{
    [Fact]
    public void TheModelTableHasNoPidTwiceAndOnlyTheCenturionModelsAreExperimental()
    {
        var pids = HeadsetProtocols.Models.SelectMany(m => m.Pids).ToList();
        Assert.Equal(pids.Count, pids.Distinct().Count());

        Assert.Equal(
            ["Logitech G PRO X 2 LIGHTSPEED", "Logitech G PRO X 3 LIGHTSPEED", "Logitech G522 LIGHTSPEED"],
            HeadsetProtocols.Models.Where(m => m.Experimental).Select(m => m.Name));
        Assert.All(HeadsetProtocols.Models.Where(m => m.Protocol == HeadsetProtocol.Centurion), m => Assert.NotNull(m.Prefix));
        Assert.All(HeadsetProtocols.Models.Where(m => m.Protocol == HeadsetProtocol.Legacy), m =>
        {
            Assert.NotNull(m.Curve);
            Assert.InRange(m.Sw, 1, 15);
        });
    }

    [Fact]
    public void EveryCurveRisesFromZeroToOneHundred()
    {
        foreach (var curve in new[] { HeadsetProtocols.CurveG533, HeadsetProtocols.CurveG535, HeadsetProtocols.CurveG633, HeadsetProtocols.CurveGPro })
        {
            Assert.Equal(0, curve[0].Percent);
            Assert.Equal(100, curve[^1].Percent);
            for (var i = 1; i < curve.Length; i++)
            {
                Assert.True(curve[i].Mv > curve[i - 1].Mv);
                Assert.True(curve[i].Percent > curve[i - 1].Percent);
            }
        }
    }

    [Theory]
    [InlineData(3000, 0)]
    [InlineData(3200, 0)]
    [InlineData(3350, 4)]    // a point on the curve
    [InlineData(3800, 48)]   // between (3750, 38) and (3850, 58)
    [InlineData(4150, 100)]
    [InlineData(4400, 100)]
    public void VoltageBecomesAPercentOnACurve(int mv, int percent) => Assert.Equal(percent, HeadsetProtocols.Percent(mv, HeadsetProtocols.CurveG633));

    // ---- legacy

    [Fact]
    public void TheLegacyRequestsAreTheTwoVoltageFeaturesOnly()
    {
        var lookup = HeadsetProtocols.LegacyLookup(0x1F20, 0x0A);
        Assert.Equal([0x11, 0xFF, 0x00, 0x0A, 0x1F, 0x20], lookup[..6]);
        Assert.Equal(20, lookup.Length);

        var read = HeadsetProtocols.LegacyRequest(5, 0x1001, 0x0D);
        Assert.Equal([0x11, 0xFF, 0x05, 0x0D], read[..4]);

        Assert.Throws<HidppSafetyException>(() => HeadsetProtocols.LegacyLookup(0x1004, 1));
        Assert.Throws<HidppSafetyException>(() => HeadsetProtocols.LegacyRequest(5, 0x1000, 1));
    }

    [Fact]
    public void ALegacyLookupReplyIsOkUnsupportedOrNotOurs()
    {
        Assert.Equal(("ok", 5), HeadsetProtocols.ParseLegacyLookup([0x11, 0xFF, 0x00, 0x0A, 5, 0, 3, 0], 0x0A));
        Assert.Equal("unsupported", HeadsetProtocols.ParseLegacyLookup([0x11, 0xFF, 0x00, 0x0A, 0, 0, 3, 0], 0x0A)!.Value.State);   // index 0: not found
        Assert.Equal("unsupported", HeadsetProtocols.ParseLegacyLookup([0x11, 0xFF, 0xFF, 0x00, 0x0A, 0x02, 0, 0], 0x0A)!.Value.State);
        Assert.Null(HeadsetProtocols.ParseLegacyLookup([0x11, 0xFF, 0x00, 0x0B, 5, 0, 3, 0], 0x0A));  // another software id
        Assert.Null(HeadsetProtocols.ParseLegacyLookup([0x11, 0x01, 0x00, 0x0A, 5, 0, 3, 0], 0x0A));  // another device
        Assert.Null(HeadsetProtocols.ParseLegacyLookup(null, 0x0A));
    }

    [Fact]
    public void ALegacyVoltageReplyIsOkOffOrNotOurs()
    {
        Assert.Equal(("ok", 3900, 3), HeadsetProtocols.ParseLegacyReply([0x11, 0xFF, 5, 0x0A, 0x0F, 0x3C, 3, 0], 5, 0x0A));
        Assert.Equal("off", HeadsetProtocols.ParseLegacyReply([0x11, 0xFF, 0xFF, 5, 0x0A, 0x05, 0, 0], 5, 0x0A)!.Value.State);
        Assert.Null(HeadsetProtocols.ParseLegacyReply([0x11, 0xFF, 5, 0x0A, 0x09, 0xC3, 3, 0], 5, 0x0A));    // 2499 mV
        Assert.Null(HeadsetProtocols.ParseLegacyReply([0x11, 0xFF, 6, 0x0A, 0x0F, 0x3C, 3, 0], 5, 0x0A));    // another feature index
        Assert.Null(HeadsetProtocols.ParseLegacyReply([0x11, 0xFF, 5, 0x0A, 0x0F, 0x3C], 5, 0x0A));          // too short
    }

    // ---- Astro

    [Fact]
    public void TheAstroRequestIsOneFixedReportAndItsReplyIsDecoded()
    {
        Assert.Equal(64, HeadsetProtocols.AstroRequest.Length);
        Assert.Equal([0x02, 0x0C, 0x03, 0x00, 0x06, 0x0C], HeadsetProtocols.AstroRequest[..6]);

        Assert.Equal((55, false), HeadsetProtocols.ParseAstroReply([0x02, 0x0C, 0x05, 0x00, 0x06, 0x00, 55, 0, 0]));
        Assert.Equal((55, true), HeadsetProtocols.ParseAstroReply([0x02, 0x0C, 0x05, 0x00, 0x06, 0x00, 55, 0, 1]));
        Assert.Null(HeadsetProtocols.ParseAstroReply([0x02, 0x0C, 0x05, 0x00, 0x06, 0x00, 101, 0, 0]));
        Assert.Null(HeadsetProtocols.ParseAstroReply([0x02, 0x0C, 0x03, 0x00, 0x06, 0x00, 55, 0, 0]));       // a length that is too small
        Assert.Null(HeadsetProtocols.ParseAstroReply([0x02, 0x0D, 0x05, 0x00, 0x06, 0x00, 55, 0, 0]));
        Assert.Null(HeadsetProtocols.ParseAstroReply([0x02, 0x0C, 0x05]));
    }

    // ---- Centurion

    [Fact]
    public void OnlyTheFourCenturionRequestsAreAllowed()
    {
        Assert.True(HeadsetProtocols.IsAllowedCenturionPayload([0x00, 0x01, 0x00, 0x03]));
        Assert.True(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x01]));
        Assert.True(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x11, 0x00, 0x05, 0x00, 0x00, 0x01, 0x01, 0x04]));
        Assert.True(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x11, 0x00, 0x03, 0x00, 0x09, 0x01]));

        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([]));
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x00, 0x01, 0x00, 0x04]));                   // another feature
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x00, 0x01]));                               // the bridge index must be 1 to 0x7F
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x80, 0x01]));
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x02]));                               // not the software id
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x11, 0x00, 0x03, 0x00, 0x09, 0x02]));
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x11, 0x00, 0x03, 0x00, 0x00, 0x01])); // sub index 0
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x11, 0x00, 0x05, 0x00, 0x00, 0x01, 0x01, 0x05]));
        Assert.False(HeadsetProtocols.IsAllowedCenturionPayload([0x07, 0x10, 0x00, 0x03, 0x00, 0x09, 0x01]));
        Assert.Throws<HidppSafetyException>(() => HeadsetProtocols.CenturionFrame([0x51], [0x07, 0x02]));
    }

    [Fact]
    public void ACenturionFrameIsThePrefixTheLengthAndThePayloadPaddedTo64()
    {
        var frame = HeadsetProtocols.CenturionBridgeLookup([0x51]);
        Assert.Equal(64, frame.Length);
        Assert.Equal([0x51, 0x05, 0x00, 0x00, 0x01, 0x00, 0x03], frame[..7]);
        Assert.All(frame[7..], b => Assert.Equal(0, b));

        var two = HeadsetProtocols.CenturionStatus([0x50, 0x23], 7);
        Assert.Equal([0x50, 0x23, 0x03, 0x00, 0x07, 0x01], two[..6]);

        Assert.Equal([0x50, 0x23, 0x08, 0x00, 0x07, 0x11, 0x00, 0x03, 0x00, 0x09, 0x01], HeadsetProtocols.CenturionBatteryGet([0x50, 0x23], 7, 9)[..11]);
    }

    [Fact]
    public void ACenturionPayloadIsExtractedOnlyFromAFrameWithTheRightPrefixAndALengthThatFits()
    {
        byte[] Frame(byte[] prefix, params byte[] payload) => [.. prefix, (byte)(payload.Length + 1), 0, .. payload, .. new byte[40]];

        Assert.Equal([7, 0x10, 9], HeadsetProtocols.ExtractCenturionPayload(Frame([0x51], 7, 0x10, 9), [0x51]));
        Assert.Equal([7, 0x10, 9], HeadsetProtocols.ExtractCenturionPayload(Frame([0x50, 0x23], 7, 0x10, 9), [0x50, 0x23]));
        Assert.Null(HeadsetProtocols.ExtractCenturionPayload(Frame([0x51], 7, 0x10, 9), [0x50, 0x23]));       // another model's prefix
        Assert.Null(HeadsetProtocols.ExtractCenturionPayload(null, [0x51]));
        Assert.Null(HeadsetProtocols.ExtractCenturionPayload([0x51, 0x01, 0x00, 0x00], [0x51]));               // length 1: no payload
        Assert.Null(HeadsetProtocols.ExtractCenturionPayload([0x51, 0x40, 0x00, 1, 2, 3], [0x51]));            // claims more than it holds
    }

    [Fact]
    public void ACenturionBatteryIsDecoded()
    {
        Assert.Equal((80, false), HeadsetProtocols.ParseCenturionBattery([80, 0, 0]));
        Assert.Equal((80, true), HeadsetProtocols.ParseCenturionBattery([80, 0, 1]));
        Assert.Equal((80, true), HeadsetProtocols.ParseCenturionBattery([80, 0, 2]));
        Assert.Equal((80, false), HeadsetProtocols.ParseCenturionBattery([80]));
        Assert.Null(HeadsetProtocols.ParseCenturionBattery([101]));
        Assert.Null(HeadsetProtocols.ParseCenturionBattery([]));
        Assert.Null(HeadsetProtocols.ParseCenturionBattery(null));
    }
}

public class LogitechHeadsetReaderTests
{
    private static HidInfo Info(string path, int pid, int page, int usage, int len) =>
        new(path, 0x046D, pid, page, usage, len, len, "Some Headset");

    private static LogitechHeadsetReader Reader(IHidSystem hid, bool experimental = false) =>
        new(hid, budget: TimeSpan.FromMilliseconds(400), experimental: () => experimental);

    // ---- legacy HID++

    private const string LegacyPath = @"\\?\hid#vid_046d&pid_0ab5&mi_03#7&1&0000#{g}";   // a G733 or similar
    private static readonly HidInfo LegacyInfo = Info(LegacyPath, 0x0AB5, 0xFF43, 0x0202, 20);

    private sealed class LegacySim(int sw, Dictionary<int, int> features, int mv, int state = 0, bool off = false, bool silent = false)
    {
        public List<byte[]> Requests { get; } = [];
        public RecordingHandle Handle()
        {
            var queue = new Queue<byte[]>();
            return new RecordingHandle(
                read: () => queue.Count > 0 ? queue.Dequeue() : null,
                onWrite: p =>
                {
                    Requests.Add(p);
                    if (silent) return true;
                    var function = p[3] >> 4;
                    Assert.Equal(sw, p[3] & 0x0F);
                    if (p[2] == 0)
                    {
                        var id = (p[4] << 8) | p[5];
                        queue.Enqueue([0x11, 0xFF, 0x00, p[3], (byte)features.GetValueOrDefault(id, 0), 0, 3, .. new byte[13]]);
                    }
                    else if (off)
                    {
                        queue.Enqueue([0x11, 0xFF, 0xFF, p[2], p[3], 0x05, 0, .. new byte[13]]);
                    }
                    else
                    {
                        Assert.Equal(0, function);
                        queue.Enqueue([0x11, 0xFF, p[2], p[3], (byte)(mv >> 8), (byte)(mv & 0xFF), (byte)state, .. new byte[13]]);
                    }
                    return true;
                });
        }

        public void AssertOnlyTheTwoFeatures()
        {
            foreach (var p in Requests)
            {
                Assert.Equal(0x11, p[0]);
                Assert.Equal(0xFF, p[1]);
                if (p[2] == 0) Assert.Contains((p[4] << 8) | p[5], new[] { 0x1F20, 0x1001 });
                else Assert.Contains(p[2], features.Values);
            }
        }
    }

    [Fact]
    public async Task ALegacyHeadsetIsReadThroughItsVoltageFeatureAndTheModelsCurve()
    {
        var sim = new LegacySim(0x0A, new() { [0x1F20] = 5 }, mv: 3900, state: 0);
        var hid = new RecordingHidSystem([LegacyInfo], (LegacyPath, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal("logitech:0ab5", r.Id);
        Assert.Equal(DeviceKind.Headset, r.Kind);
        Assert.Equal(HeadsetProtocols.Percent(3900, HeadsetProtocols.CurveG633), r.Percent);
        Assert.False(r.Charging);
        Assert.True(r.Connected);
        sim.AssertOnlyTheTwoFeatures();
        Assert.Equal(2, sim.Requests.Count);                                         // one lookup and one read
    }

    [Fact]
    public async Task ChargingIsStateThreeAndTheSecondVoltageFeatureIsTriedWhenTheFirstIsMissing()
    {
        var sim = new LegacySim(0x0A, new() { [0x1001] = 4 }, mv: 4000, state: 3);   // no 0x1F20
        var hid = new RecordingHidSystem([LegacyInfo], (LegacyPath, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.True(r.Charging);
        Assert.Equal(HeadsetProtocols.Percent(4000, HeadsetProtocols.CurveG633), r.Percent);
        Assert.Equal(3, sim.Requests.Count);                                         // lookup, lookup, read
        sim.AssertOnlyTheTwoFeatures();
    }

    [Fact]
    public async Task ALegacyHeadsetThatAnswersWithAnErrorIsOffAndOneThatIsSilentGivesNothing()
    {
        var off = new LegacySim(0x0A, new() { [0x1F20] = 5 }, mv: 0, off: true);
        var r = Assert.Single(await Reader(new RecordingHidSystem([LegacyInfo], (LegacyPath, off.Handle()))).ReadAsync(default));
        Assert.False(r.Connected);
        Assert.Null(r.Percent);

        var silent = new LegacySim(0x0A, new() { [0x1F20] = 5 }, mv: 0, silent: true);
        Assert.Empty(await Reader(new RecordingHidSystem([LegacyInfo], (LegacyPath, silent.Handle()))).ReadAsync(default));

        var neither = new LegacySim(0x0A, [], mv: 3900);                              // no voltage feature at all
        Assert.Empty(await Reader(new RecordingHidSystem([LegacyInfo], (LegacyPath, neither.Handle()))).ReadAsync(default));
    }

    [Fact]
    public async Task OnlyTheCollectionThatTheModelNeedsIsUsedAndEachBaseIsReadOnce()
    {
        var sim = new LegacySim(0x0A, new() { [0x1F20] = 5 }, mv: 3900);
        var wrongPage = Info(@"\\?\hid#vid_046d&pid_0ab5&mi_01#7&1&0000#{g}", 0x0AB5, 0xFF00, 0x0002, 20);
        var tooSmall = Info(@"\\?\hid#vid_046d&pid_0ab5&mi_02#7&1&0000#{g}", 0x0AB5, 0xFF43, 0x0202, 7);
        var second = Info(@"\\?\hid#vid_046d&pid_0ab5&mi_04#7&1&0000#{g}", 0x0AB5, 0xFF43, 0x0202, 20);
        var hid = new RecordingHidSystem([wrongPage, tooSmall, second, LegacyInfo],
            (wrongPage.Path, new RecordingHandle()), (tooSmall.Path, new RecordingHandle()), (second.Path, sim.Handle()), (LegacyPath, sim.Handle()));

        Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Single(hid.Opened);                                                    // the first usable one, in path order, and no other
    }

    // ---- Astro

    [Fact]
    public async Task AnAstroA50IsAskedOneFixedReportAndRead()
    {
        const string path = @"\\?\hid#vid_046d&pid_0b1c&mi_02#7&1&0000#{g}";
        var info = Info(path, 0x0B1C, 0xFF32, 0x0074, 64);
        var pad = new RecordingHandle(
            read: () => [0x02, 0x0C, 0x05, 0x00, 0x06, 0x00, 72, 0, 1, .. new byte[55]],
            onWrite: p => true);
        var hid = new RecordingHidSystem([info], (path, pad));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal((72, true), (r.Percent, r.Charging));
        Assert.Equal("logitech:0b1c", r.Id);
        Assert.Equal([HeadsetProtocols.AstroRequest], pad.Written);
    }

    // ---- Centurion

    private const string CenturionPath = @"\\?\hid#vid_046d&pid_0b45&mi_03#7&1&0000#{g}";   // a G PRO X 3
    private static readonly HidInfo CenturionInfo = Info(CenturionPath, 0x0B45, 0xFFA0, 0x0001, 64);
    private static readonly byte[] Prefix3 = [0x50, 0x23];

    private static byte[] CFrame(byte[] prefix, params byte[] payload) =>
        [.. prefix, (byte)(payload.Length + 1), 0, .. payload, .. new byte[64 - prefix.Length - 2 - payload.Length]];

    private sealed class CenturionSim(byte[] prefix, int bridge = 7, int sub = 9, int percent = 80, int state = 1, bool connected = true, bool wrongShape = false)
    {
        public List<byte[]> Requests { get; } = [];
        public RecordingHandle Handle()
        {
            var queue = new Queue<byte[]>();
            return new RecordingHandle(
                read: () => queue.Count > 0 ? queue.Dequeue() : null,
                onWrite: frame =>
                {
                    Requests.Add(frame);
                    var length = frame[prefix.Length];
                    var payload = frame[(prefix.Length + 2)..(prefix.Length + 1 + length)];
                    switch (payload.Length)
                    {
                        case 4:                                                      // bridge lookup
                            queue.Enqueue(CFrame(prefix, 0x00, 0x10, (byte)bridge));
                            break;
                        case 2:                                                      // bridge status
                            queue.Enqueue(wrongShape
                                ? CFrame(prefix, (byte)bridge, 1, 0)
                                : CFrame(prefix, (byte)bridge, 1, 0, 0, (byte)(connected ? 1 : 0), (byte)(connected ? 1 : 0)));
                            break;
                        case 9:                                                      // battery lookup
                            queue.Enqueue(CFrame(prefix, (byte)bridge, 0x11));
                            queue.Enqueue(CFrame(prefix, (byte)bridge, 0x10, 0, 0, 0, 0, 0, (byte)sub));
                            break;
                        case 7:                                                      // battery getter
                            queue.Enqueue(CFrame(prefix, (byte)bridge, 0x11));
                            queue.Enqueue(CFrame(prefix, (byte)bridge, 0x10, 0, 0, 0, (byte)sub, 0, (byte)percent, 0, (byte)state));
                            break;
                    }
                    return true;
                });
        }
    }

    [Fact]
    public async Task ACenturionHeadsetIsOnlyReadWhenExperimentalReadersAreOn()
    {
        var sim = new CenturionSim(Prefix3);
        var hid = new RecordingHidSystem([CenturionInfo], (CenturionPath, sim.Handle()));

        Assert.Empty(await Reader(hid, experimental: false).ReadAsync(default));
        Assert.Empty(hid.Opened);
        Assert.Empty(sim.Requests);

        var r = Assert.Single(await Reader(hid, experimental: true).ReadAsync(default));
        Assert.Equal(("logitech:0b45", 80, true), (r.Id, r.Percent, r.Charging));
    }

    [Fact]
    public async Task ACenturionHeadsetIsReadThroughTheBridgeAndOnlyFixedRequestsAreSent()
    {
        var sim = new CenturionSim(Prefix3, bridge: 7, sub: 9, percent: 41, state: 0);
        var hid = new RecordingHidSystem([CenturionInfo], (CenturionPath, sim.Handle()));

        var r = Assert.Single(await Reader(hid, experimental: true).ReadAsync(default));
        Assert.Equal((41, false), (r.Percent, r.Charging));
        Assert.True(r.Connected);

        Assert.Equal(4, sim.Requests.Count);          // bridge lookup, status, battery lookup, battery getter
        foreach (var frame in sim.Requests)
        {
            Assert.Equal(64, frame.Length);
            Assert.Equal(Prefix3, frame[..2]);
            Assert.True(HeadsetProtocols.IsAllowedCenturionPayload(frame[4..(3 + frame[2])]));
        }
    }

    [Fact]
    public async Task ACenturionBaseWithNoHeadsetConnectedIsOffAndAWrongShapedBridgeGivesNothing()
    {
        var off = new CenturionSim(Prefix3, connected: false);
        var r = Assert.Single(await Reader(new RecordingHidSystem([CenturionInfo], (CenturionPath, off.Handle())), experimental: true).ReadAsync(default));
        Assert.False(r.Connected);
        Assert.Null(r.Percent);
        Assert.Equal(2, off.Requests.Count);          // it stopped at the status: no battery lookup for a headset that is not there

        var odd = new CenturionSim(Prefix3, wrongShape: true);
        Assert.Empty(await Reader(new RecordingHidSystem([CenturionInfo], (CenturionPath, odd.Handle())), experimental: true).ReadAsync(default));
        Assert.Equal(2, odd.Requests.Count);

        var silent = new RecordingHandle(read: () => null, onWrite: _ => true);
        Assert.Empty(await Reader(new RecordingHidSystem([CenturionInfo], (CenturionPath, silent)), experimental: true).ReadAsync(default));
    }

    [Fact]
    public async Task APrefixOfAnotherModelIsNotAcceptedAsAnAnswer()
    {
        var sim = new CenturionSim([0x51]);           // answers with the G PRO X 2's prefix to a G PRO X 3
        var hid = new RecordingHidSystem([CenturionInfo], (CenturionPath, sim.Handle()));
        Assert.Empty(await Reader(hid, experimental: true).ReadAsync(default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x80)]
    public async Task ABridgeIndexOutsideTheValidRangeIsRefused(int bridge)
    {
        var sim = new CenturionSim(Prefix3, bridge: bridge);
        var hid = new RecordingHidSystem([CenturionInfo], (CenturionPath, sim.Handle()));
        Assert.Empty(await Reader(hid, experimental: true).ReadAsync(default));
        Assert.Single(sim.Requests);                  // nothing more is sent to an index that is not a bridge
    }

    // ---- registry and the combined reader

    [Theory]
    [InlineData(0x046D, 0x0AB5, true)]
    [InlineData(0x046D, 0x0B1C, true)]
    [InlineData(0x046D, 0x0B45, true)]    // experimental, but known
    [InlineData(0x046D, 0x0001, false)]
    [InlineData(0x1038, 0x0AB5, false)]
    public void TheRegistryKnowsTheHeadsetBases(int vid, int pid, bool expected) => Assert.Equal(expected, SupportedDevices.Supports(vid, pid));

    private static BatteryReading Reading(string id, int? percent) => new(id, "x", DeviceKind.Headset, percent, false, true);

    [Fact]
    public void AHeadsetBaseThatHidppAlsoListsKeepsOneEntryByTheRuleOfTheOriginal()
    {
        // Both have a reading: the generic HID++ one stays.
        Assert.Equal(["logitech:0ab5:01"], LogitechCombinedReader.Dedupe([Reading("logitech:0ab5:01", 50), Reading("logitech:0ab5", 60)]).Select(r => r.Id));

        // Only the headset one has a reading: it replaces the HID++ ones, a receiver with several entries included.
        Assert.Equal(["logitech:0ab5"], LogitechCombinedReader.Dedupe([Reading("logitech:0ab5:01", null), Reading("logitech:0ab5:02:abcdef", null), Reading("logitech:0ab5", 60)]).Select(r => r.Id));

        // Neither has one: the headset entry goes.
        Assert.Equal(["logitech:0ab5:01"], LogitechCombinedReader.Dedupe([Reading("logitech:0ab5:01", null), Reading("logitech:0ab5", null)]).Select(r => r.Id));

        // One HID++ entry with a reading is enough to keep them.
        Assert.Equal(["logitech:0ab5:01", "logitech:0ab5:02"],
            LogitechCombinedReader.Dedupe([Reading("logitech:0ab5:01", null), Reading("logitech:0ab5:02", 40), Reading("logitech:0ab5", 60)]).Select(r => r.Id));
    }

    [Fact]
    public void DedupeLeavesOtherDevicesAndOtherBasesAlone()
    {
        var all = new[]
        {
            Reading("logitech:0b1c", 70),                 // a headset with no HID++ entries
            Reading("logitech:c52b:01", 30),              // a receiver's mouse
            Reading("steelseries:22a1", 90),
            Reading("dualsense:0ce6", 55),
        };
        Assert.Equal(all.Select(r => r.Id), LogitechCombinedReader.Dedupe(all).Select(r => r.Id));
        Assert.Empty(LogitechCombinedReader.Dedupe([]));
    }

    [Fact]
    public async Task TheCombinedReaderGivesBothKindsAndOneFailingReaderDoesNotHideTheOther()
    {
        var headset = new RecordingHidSystem([LegacyInfo], (LegacyPath, new LegacySim(0x0A, new() { [0x1F20] = 5 }, 3900).Handle()));
        var combined = new LogitechCombinedReader(
            new LogitechReader(new ThrowingHid()),                                      // its listing throws
            new LogitechHeadsetReader(headset, budget: TimeSpan.FromMilliseconds(400)));

        Assert.Equal(["logitech:0ab5"], (await combined.ReadAsync(default)).Select(r => r.Id));
    }

    private sealed class ThrowingHid : IHidSystem
    {
        public IReadOnlyList<HidInfo> Enumerate(int vendorId) => throw new InvalidOperationException("boom");
        public IHidHandle Open(HidInfo info) => throw new InvalidOperationException();
    }
}
