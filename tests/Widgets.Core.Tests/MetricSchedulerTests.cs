using Widgets.Core;

namespace Widgets.Core.Tests;

public class MetricSchedulerTests
{
    private sealed class FakeProvider(string id, Func<CancellationToken, Task<IReadOnlyList<Metric>>> poll) : IProvider
    {
        public string Id => id;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(20);
        public Task<IReadOnlyList<Metric>> PollAsync(CancellationToken ct) => poll(ct);
    }

    private static Metric M(string id, double? value, string source = "p") =>
        new(id, id, MetricKind.Percent, value, "%", source);

    private static Task<IReadOnlyList<Metric>> Ok(params Metric[] m) => Task.FromResult<IReadOnlyList<Metric>>(m);

    private sealed class SlowProvider(Func<int> value) : IProvider
    {
        public string Id => "slow";
        public TimeSpan Interval => TimeSpan.FromMinutes(10); // never due during the test
        public Task<IReadOnlyList<Metric>> PollAsync(CancellationToken ct) => Ok(M("a", value()));
    }

    [Fact]
    public async Task RequestPoll_PollsAgainWithoutWaitingForTheInterval()
    {
        var store = new MetricStore();
        var current = 1;
        await using var s = new MetricScheduler([new SlowProvider(() => current)], store);
        s.Start();
        await WaitFor(() => store.Snapshot().Count == 1);

        current = 2;
        s.RequestPoll("slow");

        await WaitFor(() => store.Snapshot().Single().Value == 2);
    }

    [Fact]
    public void RequestPoll_UnknownProviderIsIgnored()
    {
        var s = new MetricScheduler([], new MetricStore());
        s.RequestPoll("nope"); // must not throw
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the poll");
            await Task.Delay(10);
        }
    }


    [Fact]
    public async Task PollOnce_StoresTheResult()
    {
        var store = new MetricStore();
        var p = new FakeProvider("p", _ => Ok(M("a", 1)));
        await using var s = new MetricScheduler([p], store);

        await s.PollOnceAsync(p, CancellationToken.None);

        Assert.Equal(1, Assert.Single(store.Snapshot()).Value);
    }

    [Fact]
    public async Task PollOnce_ProviderThrows_KeepsOldValuesAsStale()
    {
        var store = new MetricStore();
        var fail = false;
        var p = new FakeProvider("p", _ => fail ? throw new InvalidOperationException("boom") : Ok(M("a", 7)));
        await using var s = new MetricScheduler([p], store);

        await s.PollOnceAsync(p, CancellationToken.None);
        fail = true;
        await s.PollOnceAsync(p, CancellationToken.None); // must not throw

        var m = Assert.Single(store.Snapshot());
        Assert.Equal(7, m.Value);
        Assert.True(m.Stale);
    }

    [Fact]
    public async Task PollOnce_FirstPollFails_LeavesStoreEmpty()
    {
        var store = new MetricStore();
        var p = new FakeProvider("p", _ => throw new InvalidOperationException());
        await using var s = new MetricScheduler([p], store);

        await s.PollOnceAsync(p, CancellationToken.None);

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task PollOnce_Timeout_MarksStaleInsteadOfHanging()
    {
        var store = new MetricStore();
        var hang = false;
        var p = new FakeProvider("p", async ct =>
        {
            if (hang) await Task.Delay(Timeout.Infinite, ct);
            return [M("a", 3)];
        });
        await using var s = new MetricScheduler([p], store, TimeSpan.FromMilliseconds(50));

        await s.PollOnceAsync(p, CancellationToken.None);
        hang = true;
        await s.PollOnceAsync(p, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(Assert.Single(store.Snapshot()).Stale);
    }

    [Fact]
    public async Task PollOnce_CallerCancellation_Propagates()
    {
        var store = new MetricStore();
        var p = new FakeProvider("p", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        await using var s = new MetricScheduler([p], store);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.PollOnceAsync(p, cts.Token));
    }

    [Fact]
    public async Task StartedScheduler_PollsRepeatedlyAndStopsOnDispose()
    {
        var store = new MetricStore();
        var calls = 0;
        var p = new FakeProvider("p", _ => Ok(M("a", Interlocked.Increment(ref calls))));
        var s = new MetricScheduler([p], store);

        s.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref calls) < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        await s.DisposeAsync();

        var after = Volatile.Read(ref calls);
        Assert.True(after >= 3, $"expected at least 3 polls, saw {after}");
        await Task.Delay(100);
        Assert.Equal(after, Volatile.Read(ref calls)); // no polls after dispose
    }

    [Fact]
    public void Store_KeepsProviderOrderAndRaisesChanged()
    {
        var store = new MetricStore();
        var changes = 0;
        store.Changed += () => changes++;

        store.Set("second", [M("b", 2, "second")]);
        store.Set("first", [M("a", 1, "first")]);
        store.Set("second", [M("b", 3, "second")]);

        Assert.Equal(["b", "a"], store.Snapshot().Select(m => m.Id));
        Assert.Equal(3, store.Snapshot().First().Value);
        Assert.Equal(3, changes);
    }
}
