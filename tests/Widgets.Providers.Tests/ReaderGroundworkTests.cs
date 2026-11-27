using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Tests;

public class ReaderGroundworkTests
{
    private static BatteryReading Dev(string name, int pct = 50) =>
        new("shared:1", name, DeviceKind.Mouse, pct, false, true);

    private sealed class AsyncReader(Func<CancellationToken, Task<IReadOnlyList<BatteryReading>>> read) : IBatteryReader
    {
        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) => read(ct);
    }

    private sealed class BlockingFirstLineReader(Func<IReadOnlyList<BatteryReading>> read) : IBatteryReader
    {
        // Blocks before its first await, the way a reader listing HID devices does.
        public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) => Task.FromResult(read());
    }

    [Fact]
    public async Task ReadersRunTogetherSoOneThatWaitsOnAnotherStillFinishes()
    {
        // Run in turn, the first reader would wait for a signal only the second sends, and never finish.
        var secondStarted = new TaskCompletionSource();
        var first = new AsyncReader(async ct =>
        {
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return [Dev("first", 10)];
        });
        var second = new AsyncReader(ct =>
        {
            secondStarted.SetResult();
            return Task.FromResult<IReadOnlyList<BatteryReading>>([Dev("second", 20)]);
        });

        var metrics = await new BatteryProvider([first, second]).PollAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(metrics);
    }

    [Fact]
    public async Task WhenTwoReadersReportTheSameIdTheLaterReaderWinsWhicheverFinishesLast()
    {
        var release = new TaskCompletionSource();
        var slowEarlier = new AsyncReader(async _ => { await release.Task; return [Dev("earlier", 10)]; });
        var fastLater = new AsyncReader(_ =>
        {
            release.SetResult(); // the earlier reader can only finish after this one
            return Task.FromResult<IReadOnlyList<BatteryReading>>([Dev("later", 77)]);
        });

        var m = Assert.Single(await new BatteryProvider([slowEarlier, fastLater]).PollAsync(default).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(77, m.Value);
    }

    [Fact]
    public async Task AFailingReaderDoesNotTakeTheOthersDown()
    {
        var bad = new AsyncReader(_ => throw new InvalidOperationException("boom"));
        var good = new BlockingFirstLineReader(() => [Dev("good", 42)]);

        var m = Assert.Single(await new BatteryProvider([bad, good]).PollAsync(default));
        Assert.Equal(42, m.Value);
    }

    [Fact]
    public async Task AReaderOutOfItsOwnBudgetIsSkippedButTheWholePollBeingCancelledStillPropagates()
    {
        var outOfBudget = new AsyncReader(_ => throw new OperationCanceledException());
        var good = new BlockingFirstLineReader(() => [Dev("good", 42)]);
        Assert.Single(await new BatteryProvider([outOfBudget, good]).PollAsync(default));

        using var cts = new CancellationTokenSource();
        var waits = new AsyncReader(async ct => { await Task.Delay(Timeout.Infinite, ct); return []; });
        var poll = new BatteryProvider([waits]).PollAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
    }

    // ---- registry

    [Theory]
    [InlineData(0x1038, 0x22A1, true)]   // SteelSeries Arctis Nova 7
    [InlineData(0x054C, 0x0CE6, true)]   // DualSense
    [InlineData(0x054C, 0x0DF2, true)]   // DualSense Edge
    [InlineData(0x1038, 0x0001, false)]  // SteelSeries, not a model it reads
    [InlineData(0x046D, 0x22A1, false)]  // same product id, another vendor
    public void TheRegistryKnowsWhatTheReadersRead(int vid, int pid, bool expected) =>
        Assert.Equal(expected, SupportedDevices.Supports(vid, pid));

    // ---- feature report writes

    [Fact]
    public async Task AHandleThatCannotSetFeatureReportsFalseAndDoesNotThrow()
    {
        IHidHandle handle = new NoFeatureHandle();
        Assert.False(await handle.SetFeatureAsync([0x00, 0x01], default));
        Assert.Null(await handle.GetFeatureAsync(0x05, default));
    }

    private sealed class NoFeatureHandle : IHidHandle
    {
        public Task<bool> WriteAsync(byte[] report, CancellationToken ct) => Task.FromResult(false);
        public Task<byte[]?> ReadAsync(CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task DrainAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
