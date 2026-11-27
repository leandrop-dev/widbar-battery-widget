using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;
using Widgets.Providers.Battery.Razer;

namespace Widgets.Providers.Tests;

public class RazerProtocolTests
{
    [Fact]
    public void TheBatteryRequestIsOneFeatureReportWithItsCrc()
    {
        var request = RazerProtocol.BuildRequest(0x1F, RazerProtocol.ClassPower, RazerProtocol.CmdGetBattery, 0x02);

        Assert.Equal(91, request.Length);
        Assert.Equal(0, request[0]);                                    // report id
        Assert.Equal([0x00, 0x1F], request[1..3]);                      // status 0, transaction id
        Assert.Equal([0x02, 0x07, 0x80], request[6..9]);                // size, class, command
        Assert.Equal(0x85, request[89]);                                // 2 ^ 7 ^ 0x80, the transaction id is outside the crc
        Assert.Equal(0x81, RazerProtocol.BuildRequest(0x1F, RazerProtocol.ClassPower, RazerProtocol.CmdGetCharging, 0x02)[89]);   // 2 ^ 7 ^ 0x84
    }

    [Fact]
    public void AResponseIsDecodedWithOrWithoutItsReportIdAndItsCrcIsChecked()
    {
        var good = Reply(0x3F, 0x80, 0x02, 0, 200);
        var withId = RazerProtocol.DecodeResponse(good)!;
        Assert.Equal((byte)0x02, withId.Status);
        Assert.Equal((byte)0x3F, withId.TransactionId);
        Assert.Equal((byte)0x80, withId.CommandId);
        Assert.Equal(200, withId.Args[1]);
        Assert.True(withId.CrcOk);

        Assert.True(RazerProtocol.DecodeResponse(good[1..])!.CrcOk);

        var broken = (byte[])good.Clone();
        broken[10] ^= 0xFF;
        Assert.False(RazerProtocol.DecodeResponse(broken)!.CrcOk);

        Assert.Null(RazerProtocol.DecodeResponse(null));
        Assert.Null(RazerProtocol.DecodeResponse(new byte[40]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 50)]
    [InlineData(255, 100)]
    public void TheLevelIsScaledFromTwoFiftyFive(int raw, int percent) => Assert.Equal(percent, RazerProtocol.BatteryPercent([0, (byte)raw]));

    [Fact]
    public void ChargingIsOneAndAShortReplyIsNeither()
    {
        Assert.True(RazerProtocol.IsCharging([0, 1]));
        Assert.False(RazerProtocol.IsCharging([0, 0]));
        Assert.False(RazerProtocol.IsCharging([1]));
        Assert.Null(RazerProtocol.BatteryPercent([1]));
    }

    [Fact]
    public void TheKnownTransactionIdGoesFirstThenTheUsualOrder()
    {
        Assert.Equal([0x3F, 0x1F, 0xFF, 0x9F], RazerProtocol.TransactionOrder(0x007C));
        Assert.Equal([0x1F, 0x3F, 0xFF, 0x9F], RazerProtocol.TransactionOrder(0x1234));
        Assert.Equal([0xFF, 0x1F, 0x3F, 0x9F], RazerProtocol.TransactionOrder(0x007C, preferred: 0xFF));
    }

    [Fact]
    public void MouseMatsHaveNoBattery()
    {
        Assert.True(RazerProtocol.HasNoBattery(0x0C00));
        Assert.True(RazerProtocol.HasNoBattery(0x0CFF));
        Assert.False(RazerProtocol.HasNoBattery(0x0BFF));
        Assert.False(RazerProtocol.HasNoBattery(0x0D00));
    }

    // ---- ODESSA

    [Fact]
    public void OnlyTheThreeReadCommandsCanBeBuilt()
    {
        foreach (var command in RazerProtocol.AllowedCommands)
        {
            Assert.Equal(64, RazerProtocol.OdessaRequest(command, 1, true).Length);
            Assert.Equal(64, RazerProtocol.PaRequest(command).Length);
        }
        foreach (byte other in new byte[] { 0x00, 0x22, 0x2B, 0xE1, 0xFF })
        {
            Assert.Throws<ArgumentException>(() => RazerProtocol.OdessaRequest(other, 1, false));
            Assert.Throws<ArgumentException>(() => RazerProtocol.PaRequest(other));
        }
    }

    [Fact]
    public void AHyperSpeedRequestCarriesTheTransactionTheTargetAndAnXorChecksum()
    {
        var dongle = RazerProtocol.OdessaRequest(RazerProtocol.CmdConnection, 5, remote: false);
        var headset = RazerProtocol.OdessaRequest(RazerProtocol.CmdBattery, 5, remote: true);

        Assert.Equal([0x02, 0x00, 0x65], dongle[..3]);                  // report id, then the transaction plus 0x60
        Assert.Equal(4, dongle[6]);
        Assert.Equal(0x00, dongle[9]);
        Assert.Equal(0x80, headset[9]);
        Assert.Equal(RazerProtocol.CmdBattery, headset[10]);

        foreach (var frame in new[] { dongle, headset })
        {
            byte x = 0;
            for (var i = 0; i < 62; i++) x ^= frame[i];
            Assert.Equal(x, frame[62]);
        }
    }

    [Fact]
    public void AHyperSpeedReplyIsAcceptedOnlyForTheCommandAndTransactionAsked()
    {
        var reply = HyperSpeedReply(5, RazerProtocol.CmdBattery, 2, [77]);

        var parsed = RazerProtocol.ParseOdessaReply(reply, RazerProtocol.CmdBattery, 5)!.Value;
        Assert.Equal((byte)2, parsed.Status);
        Assert.Equal([77], parsed.Data);
        Assert.Null(RazerProtocol.ParseOdessaReply(reply, RazerProtocol.CmdCharging, 5));           // another command
        Assert.Null(RazerProtocol.ParseOdessaReply(reply, RazerProtocol.CmdBattery, 6));            // another transaction
        Assert.Null(RazerProtocol.ParseOdessaReply(null, RazerProtocol.CmdBattery, 5));
        Assert.Null(RazerProtocol.ParseOdessaReply(new byte[10], RazerProtocol.CmdBattery, 5));

        var broken = (byte[])reply.Clone();
        broken[13] ^= 0x55;
        Assert.Null(RazerProtocol.ParseOdessaReply(broken, RazerProtocol.CmdBattery, 5));           // checksum

        var changedStatus = (byte[])reply.Clone();                                                  // the status byte is outside the checksum
        changedStatus[1] = 1;
        Assert.Equal((byte)1, RazerProtocol.ParseOdessaReply(changedStatus, RazerProtocol.CmdBattery, 5)!.Value.Status);
    }

    [Fact]
    public void APaRequestIsFixedAndItsReplyIsParsedByCommand()
    {
        var request = RazerProtocol.PaRequest(RazerProtocol.CmdBattery);
        Assert.Equal([0x02, 0x80, 0x08, 0x00, 0x00, (byte)'P', (byte)'A', 0x08, 0x00, 0x03, RazerProtocol.CmdBattery], request[..11]);
        Assert.All(request[11..], b => Assert.Equal(0, b));

        var reply = PaReply(RazerProtocol.CmdBattery, [64]);
        Assert.Equal([64], RazerProtocol.ParsePaReply(reply, RazerProtocol.CmdBattery));
        Assert.Null(RazerProtocol.ParsePaReply(reply, RazerProtocol.CmdCharging));
        Assert.Null(RazerProtocol.ParsePaReply(null, RazerProtocol.CmdBattery));

        var wrongMagic = (byte[])reply.Clone();
        wrongMagic[2] = (byte)'X';
        Assert.Null(RazerProtocol.ParsePaReply(wrongMagic, RazerProtocol.CmdBattery));

        var lies = (byte[])reply.Clone();
        lies[14] = 60;                                                  // a payload longer than the frame says it holds
        Assert.Null(RazerProtocol.ParsePaReply(lies, RazerProtocol.CmdBattery));
    }

    // ---- builders shared with the reader tests

    internal static byte[] Reply(byte transaction, byte command, byte status, params byte[] args)
    {
        var packet = new byte[90];
        packet[0] = status;
        packet[1] = transaction;
        packet[5] = 2;
        packet[6] = RazerProtocol.ClassPower;
        packet[7] = command;
        args.CopyTo(packet, 8);
        packet[88] = RazerProtocol.Crc(packet);
        return [0, .. packet];
    }

    internal static byte[] HyperSpeedReply(int transaction, byte command, byte status, byte[] data)
    {
        var b = new byte[64];
        b[0] = 2;
        b[1] = status;
        b[2] = (byte)(transaction + 0x60);
        b[10] = command;
        b[11] = 1;
        b[12] = (byte)data.Length;
        data.CopyTo(b, 13);
        byte x = 0;
        for (var i = 0; i < 62; i++)
            if (i != 1) x ^= b[i];
        b[62] = x;
        return b;
    }

    internal static byte[] PaReply(byte command, byte[] data)
    {
        var b = new byte[64];
        b[0] = 2;
        b[1] = (byte)(13 + data.Length);
        b[2] = (byte)'P';
        b[3] = (byte)'I';
        b[4] = 0x08;
        b[12] = command;
        b[13] = 1;
        b[14] = (byte)data.Length;
        data.CopyTo(b, 15);
        return b;
    }
}

public class RazerReaderTests
{
    private static HidInfo Mouse(int pid, string path, int page = 0xFF00, int usage = 0x0001, int feature = 91, string product = "") =>
        new(path, 0x1532, pid, page, usage, 0, 0, product, feature);

    private static RazerReader Reader(IHidSystem hid) => new(hid, scanBudget: TimeSpan.FromSeconds(3), settle: TimeSpan.Zero);

    /// <summary>A device that answers only the transaction ids in <paramref name="answers"/>, with the given level and charging state.</summary>
    private sealed class Sim(int[] answers, int level = 128, bool charging = false, bool badCrc = false, bool refuseSet = false)
    {
        private byte[]? _pending;
        public List<(byte Transaction, byte Command)> Requests { get; } = [];
        public int Level { get; set; } = level;

        public RecordingHandle Handle() => new(
            onSetFeature: report =>
            {
                if (refuseSet) return false;
                var txn = report[2];
                var command = report[8];
                Requests.Add((txn, command));
                Assert.Equal(91, report.Length);
                Assert.Equal(0x07, report[7]);                                          // the power class only
                Assert.Contains(command, new byte[] { 0x80, 0x84 });                    // battery and charging gets only
                Assert.Equal(RazerProtocol.Crc(report[1..]), report[89]);
                _pending = answers.Contains(txn)
                    ? RazerProtocolTests.Reply(txn, command, 0x02, 0, command == 0x80 ? (byte)Level : (byte)(charging ? 1 : 0))
                    : null;
                if (badCrc && _pending is not null) _pending[10] ^= 0xFF;
                return true;
            },
            onGetFeature: _ => _pending);
    }

    [Fact]
    public async Task AKnownMouseIsAskedItsOwnTransactionIdFirstAndNamedFromTheTable()
    {
        const string path = @"\\?\hid#vid_1532&pid_007c&mi_00#7&1&0000#{g}";
        var sim = new Sim([0x3F], level: 128, charging: true);
        var hid = new RecordingHidSystem([Mouse(0x007C, path, product: "Razer Device")], (path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));

        Assert.Equal(("Razer DeathAdder V2 Pro Wired", DeviceKind.Mouse, 50, true, true), (r.Name, r.Kind, r.Percent, r.Charging, r.Connected));
        Assert.StartsWith("razer:007c:", r.Id);
        Assert.Equal([(0x3F, 0x80), (0x3F, 0x84)], sim.Requests);                      // its own id first, and nothing else needed
    }

    [Fact]
    public async Task TheTransactionIdThatAnsweredIsRemembered()
    {
        const string path = @"\\?\hid#vid_1532&pid_1234&mi_00#7&1&0000#{g}";
        var sim = new Sim([0xFF]);
        var reader = Reader(new RecordingHidSystem([Mouse(0x1234, path, product: "Razer Orochi")], (path, sim.Handle())));

        Assert.Single(await reader.ReadAsync(default));
        Assert.Equal(6, sim.Requests.Count);                                           // 1F twice, 3F twice, then FF for battery and charging
        Assert.Equal([0x1F, 0x1F, 0x3F, 0x3F, 0xFF, 0xFF], sim.Requests.Select(q => (int)q.Transaction));

        sim.Requests.Clear();
        Assert.Single(await reader.ReadAsync(default));
        Assert.Equal([(0xFF, 0x80), (0xFF, 0x84)], sim.Requests);                      // the next poll starts with the one that worked
    }

    [Fact]
    public async Task OnlyCollectionsThatCanTakeTheReportAreAskedAndEachProductOnce()
    {
        var sim = new Sim([0x1F]);
        string P(int n) => $@"\\?\hid#vid_1532&pid_00aa&mi_0{n}#7&1&0000#{{g}}";
        var infos = new[]
        {
            Mouse(0x00AA, P(0), feature: 0),                                           // no feature report
            Mouse(0x00AA, P(1), feature: 40),                                          // one that is too short
            Mouse(0x00AA, P(2), page: 0x000C, usage: 0x0001),                          // a page that cannot take it
            Mouse(0x00AA, P(3), page: 0x0001, usage: 0x0002),                          // the mouse collection: yes
            Mouse(0x00AA, P(4)),                                                       // a vendor collection: asked first, and then the product is done
            Mouse(0x0C02, @"\\?\hid#vid_1532&pid_0c02&mi_00#7&1&0000#{g}"),            // a mouse mat
            Mouse(0x0555, @"\\?\hid#vid_1532&pid_0555&mi_00#7&1&0000#{g}"),            // a headset dongle: the other reader's
        };
        var hid = new RecordingHidSystem(infos, (P(3), sim.Handle()), (P(4), sim.Handle()));

        Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal([P(4)], hid.Opened);                                              // vendor collections go first, and one answer ends it
    }

    [Fact]
    public async Task AZeroRightAfterALevelIsDroppedButAFirstZeroIsBelieved()
    {
        const string path = @"\\?\hid#vid_1532&pid_00ab&mi_00#7&1&0000#{g}";
        var sim = new Sim([0x1F], level: 0);
        var reader = Reader(new RecordingHidSystem([Mouse(0x00AB, path)], (path, sim.Handle())));

        Assert.Equal(0, Assert.Single(await reader.ReadAsync(default)).Percent);       // nothing above zero was seen yet
        sim.Level = 200;
        Assert.Equal(78, Assert.Single(await reader.ReadAsync(default)).Percent);
        sim.Level = 0;
        Assert.Null(Assert.Single(await reader.ReadAsync(default)).Percent);           // a glitch right after
    }

    [Fact]
    public async Task ADeviceThatRefusesIsSilentOrAnswersWithABadCrcGivesNothing()
    {
        const string path = @"\\?\hid#vid_1532&pid_00ab&mi_00#7&1&0000#{g}";
        foreach (var sim in new[] { new Sim([0x1F], refuseSet: true), new Sim([0x1F], badCrc: true), new Sim([]) })
            Assert.Empty(await Reader(new RecordingHidSystem([Mouse(0x00AB, path)], (path, sim.Handle()))).ReadAsync(default));
    }

    [Fact]
    public async Task ADeviceWithAGenericNameAndNoTableEntryIsStillShown()
    {
        const string path = @"\\?\hid#vid_1532&pid_9999&mi_00#7&1&0000#{g}";
        var sim = new Sim([0x1F]);
        var hid = new RecordingHidSystem([Mouse(0x9999, path, product: "Razer BlackWidow Keyboard")], (path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(("Razer BlackWidow Keyboard", DeviceKind.Keyboard), (r.Name, r.Kind));
    }

    [Theory]
    [InlineData(0x1532, 0x007C, true)]
    [InlineData(0x1532, 0x0555, true)]
    [InlineData(0x1532, 0x0565, true)]
    [InlineData(0x1532, 0x9999, false)]
    [InlineData(0x046D, 0x007C, false)]
    public void TheRegistryKnowsTheRazerProducts(int vid, int pid, bool expected) => Assert.Equal(expected, SupportedDevices.Supports(vid, pid));
}

public class RazerOdessaReaderTests
{
    private static HidInfo Dongle(int pid, string path, int page, int usage = 0x0001, int len = 64) =>
        new(path, 0x1532, pid, page, usage, len, len, "Razer");

    private static RazerOdessaReader Reader(IHidSystem hid) =>
        new(hid, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500), TimeSpan.Zero);

    private sealed class HyperSpeedSim(bool connected = true, int level = 64, bool charging = false, int busyFirst = 0, bool silent = false)
    {
        private readonly Queue<byte[]> _queue = new();
        private int _busy = busyFirst;
        public List<byte[]> Requests { get; } = [];

        public RecordingHandle Handle() => new(
            read: () => _queue.Count > 0 ? _queue.Dequeue() : null,
            onWrite: frame =>
            {
                Requests.Add(frame);
                var command = frame[10];
                var transaction = frame[2] - 0x60;
                var remote = frame[9] == 0x80;
                Assert.Equal(RazerProtocol.OdessaRequest(command, transaction, remote), frame);   // a frame the table can build, and only that
                if (silent) return true;
                if (_busy > 0) { _busy--; _queue.Enqueue(RazerProtocolTests.HyperSpeedReply(transaction, command, 3, [])); return true; }
                var data = command switch
                {
                    RazerProtocol.CmdConnection => new[] { (byte)(connected ? 1 : 0) },
                    RazerProtocol.CmdBattery => [(byte)level],
                    _ => new[] { (byte)(charging ? 1 : 0) },
                };
                _queue.Enqueue(RazerProtocolTests.HyperSpeedReply(transaction, command, 2, data));
                return true;
            });
    }

    [Fact]
    public async Task AHyperSpeedHeadsetIsReadThroughItsDongle()
    {
        const string path = @"\\?\hid#vid_1532&pid_0565&mi_03#7&1&0000#{g}";
        var sim = new HyperSpeedSim(level: 64, charging: true);
        var hid = new RecordingHidSystem([Dongle(0x0565, path, 0xFF14)], (path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));

        Assert.Equal(("razer:0565", "Razer BlackShark V2 HyperSpeed", DeviceKind.Headset, 64, true, true), (r.Id, r.Name, r.Kind, r.Percent, r.Charging, r.Connected));
        Assert.Equal([RazerProtocol.CmdConnection, RazerProtocol.CmdBattery, RazerProtocol.CmdCharging], sim.Requests.Select(f => f[10]));
        Assert.Equal([false, true, true], sim.Requests.Select(f => f[9] == 0x80));     // the connection goes to the dongle, the rest to the headset
    }

    [Fact]
    public async Task ABusyDongleIsAskedAgainAndADongleWithNoHeadsetIsOff()
    {
        const string path = @"\\?\hid#vid_1532&pid_0565&mi_03#7&1&0000#{g}";
        var busy = new HyperSpeedSim(busyFirst: 2);
        var r = Assert.Single(await Reader(new RecordingHidSystem([Dongle(0x0565, path, 0xFF14)], (path, busy.Handle()))).ReadAsync(default));
        Assert.Equal(64, r.Percent);
        Assert.Equal(5, busy.Requests.Count);                                          // two busy answers, then the three commands

        var off = new HyperSpeedSim(connected: false);
        var gone = Assert.Single(await Reader(new RecordingHidSystem([Dongle(0x0565, path, 0xFF14)], (path, off.Handle()))).ReadAsync(default));
        Assert.False(gone.Connected);
        Assert.Null(gone.Percent);
        Assert.Single(off.Requests);                                                   // it stops once the headset is not there
    }

    [Fact]
    public async Task ASilentDongleAndACollectionOfTheWrongShapeGiveNothing()
    {
        const string path = @"\\?\hid#vid_1532&pid_0565&mi_03#7&1&0000#{g}";
        var silent = new HyperSpeedSim(silent: true);
        Assert.Empty(await Reader(new RecordingHidSystem([Dongle(0x0565, path, 0xFF14)], (path, silent.Handle()))).ReadAsync(default));

        var wrongPage = new HyperSpeedSim();
        var hid = new RecordingHidSystem([Dongle(0x0565, path, 0xFF00), Dongle(0x0565, path + "2", 0xFF14, len: 32)], (path, wrongPage.Handle()));
        Assert.Empty(await Reader(hid).ReadAsync(default));
        Assert.Empty(hid.Opened);
    }

    private sealed class PaSim(bool connected = true, int level = 90, bool charging = false, bool silent = false, bool noise = false)
    {
        private readonly Queue<byte[]> _queue = new();
        public List<byte[]> Requests { get; } = [];

        public RecordingHandle Handle() => new(
            read: () => _queue.Count > 0 ? _queue.Dequeue() : null,
            onWrite: frame =>
            {
                Requests.Add(frame);
                var command = frame[10];
                Assert.Equal(RazerProtocol.PaRequest(command), frame);
                if (silent) return true;
                if (noise) _queue.Enqueue(new byte[64]);                               // a report of another kind first
                _queue.Enqueue(RazerProtocolTests.PaReply(command, command switch
                {
                    RazerProtocol.CmdConnection => [(byte)(connected ? 1 : 0)],
                    RazerProtocol.CmdBattery => [(byte)level],
                    _ => [(byte)(charging ? 1 : 0)],
                }));
                return true;
            });
    }

    [Fact]
    public async Task AV2ProHeadsetIsReadWithTheFramedRequestsAndNoWakeFrameIsEverSent()
    {
        const string path = @"\\?\hid#vid_1532&pid_0555&mi_00#7&1&0000#{g}";
        var sim = new PaSim(level: 90, charging: true, noise: true);
        var hid = new RecordingHidSystem([Dongle(0x0555, path, 0xFF00)], (path, sim.Handle()));

        var r = Assert.Single(await Reader(hid).ReadAsync(default));
        Assert.Equal(("razer:0555", "Razer BlackShark V2 Pro", 90, true), (r.Id, r.Name, r.Percent, r.Charging));
        Assert.Equal([RazerProtocol.CmdConnection, RazerProtocol.CmdBattery, RazerProtocol.CmdCharging], sim.Requests.Select(f => f[10]));
        Assert.All(sim.Requests, f => Assert.Equal(0x03, f[9]));                       // GET frames only: the wake frame has command type 2
    }

    [Fact]
    public async Task AV2ProDongleThatDoesNotAnswerIsSkippedWithoutBeingWoken()
    {
        const string path = @"\\?\hid#vid_1532&pid_0555&mi_00#7&1&0000#{g}";
        var sim = new PaSim(silent: true);
        Assert.Empty(await Reader(new RecordingHidSystem([Dongle(0x0555, path, 0xFF00)], (path, sim.Handle()))).ReadAsync(default));
        Assert.Single(sim.Requests);                                                   // one connection get, nothing that changes state

        var off = new PaSim(connected: false);
        var r = Assert.Single(await Reader(new RecordingHidSystem([Dongle(0x0555, path, 0xFF00)], (path, off.Handle()))).ReadAsync(default));
        Assert.False(r.Connected);
    }
}
