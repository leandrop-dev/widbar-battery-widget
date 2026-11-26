namespace Widgets.Core;

/// <summary>Latest metrics per provider. Thread safe; raises <see cref="Changed"/> after every update.</summary>
public sealed class MetricStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<Metric>> _byProvider = new();
    private readonly List<string> _order = new();

    /// <summary>Raised on the thread that updated the store, after the lock is released.</summary>
    public event Action? Changed;

    public void Set(string providerId, IReadOnlyList<Metric> metrics)
    {
        lock (_gate)
        {
            if (!_byProvider.ContainsKey(providerId)) _order.Add(providerId);
            _byProvider[providerId] = metrics;
        }
        Changed?.Invoke();
    }

    /// <summary>Keep the provider's last values but flag them as stale (a poll failed or timed out).</summary>
    public void MarkStale(string providerId)
    {
        lock (_gate)
        {
            if (!_byProvider.TryGetValue(providerId, out var old)) return;
            _byProvider[providerId] = old.Select(m => m with { Stale = true }).ToList();
        }
        Changed?.Invoke();
    }

    /// <summary>All metrics, grouped by provider in the order the providers first reported.</summary>
    public IReadOnlyList<Metric> Snapshot()
    {
        lock (_gate)
            return _order.SelectMany(id => _byProvider[id]).ToList();
    }
}
