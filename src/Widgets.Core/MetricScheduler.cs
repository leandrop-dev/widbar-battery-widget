namespace Widgets.Core;

/// <summary>
/// Polls each provider on its own interval and writes the results to a <see cref="MetricStore"/>.
/// A provider that throws or exceeds the per-poll timeout keeps its previous values, flagged stale.
/// </summary>
public sealed class MetricScheduler : IAsyncDisposable
{
    public static readonly TimeSpan DefaultPollTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IProvider> _providers;
    private readonly MetricStore _store;
    private readonly TimeSpan _pollTimeout;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = new();
    private readonly Dictionary<string, SemaphoreSlim> _nudges = new(StringComparer.Ordinal);

    public MetricScheduler(IEnumerable<IProvider> providers, MetricStore store, TimeSpan? pollTimeout = null)
    {
        _providers = providers.ToList();
        _store = store;
        _pollTimeout = pollTimeout ?? DefaultPollTimeout;
        foreach (var p in _providers) _nudges[p.Id] = new SemaphoreSlim(0, 1);
    }

    public void Start()
    {
        if (_loops.Count > 0) throw new InvalidOperationException("Already started.");
        foreach (var p in _providers)
            _loops.Add(Task.Run(() => RunLoopAsync(p, _stop.Token)));
    }

    /// <summary>Asks for an early poll of one provider (for example when a device was plugged in). Several requests before the poll runs count as one.</summary>
    public void RequestPoll(string providerId)
    {
        if (!_nudges.TryGetValue(providerId, out var nudge)) return;
        try { nudge.Release(); }
        catch (SemaphoreFullException) { } // a request is already waiting
        catch (ObjectDisposedException) { }
    }

    /// <summary>One poll with timeout and error containment. Only cancellation by <paramref name="ct"/> escapes.</summary>
    public async Task PollOnceAsync(IProvider provider, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_pollTimeout);
        try
        {
            var result = await provider.PollAsync(timeout.Token).ConfigureAwait(false);
            _store.Set(provider.Id, result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) // includes the per-poll timeout; the provider contract says "never throws", so be defensive
        {
            _store.MarkStale(provider.Id);
        }
    }

    private async Task RunLoopAsync(IProvider provider, CancellationToken ct)
    {
        try
        {
            await PollOnceAsync(provider, ct).ConfigureAwait(false);
            var nudge = _nudges[provider.Id];
            while (true)
            {
                await nudge.WaitAsync(provider.Interval, ct).ConfigureAwait(false); // returns at the interval, or earlier when nudged
                await PollOnceAsync(provider, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_loops).ConfigureAwait(false);
        _stop.Dispose();
    }
}
