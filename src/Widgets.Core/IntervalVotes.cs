namespace Widgets.Core;

/// <summary>
/// How often each placed copy of a widget wants its data refreshed. The copies share one poller, so it runs at the
/// smallest interval any copy asks for. Safe to call from different threads.
/// </summary>
public sealed class IntervalVotes
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TimeSpan> _votes = new(StringComparer.Ordinal);

    /// <summary>Records (or replaces) the interval that <paramref name="instanceId"/> wants.</summary>
    public void Set(string instanceId, TimeSpan interval)
    {
        lock (_gate) _votes[instanceId] = interval;
    }

    /// <summary>Withdraws a copy's vote, for example when it is removed from the taskbar.</summary>
    public void Remove(string instanceId)
    {
        lock (_gate) _votes.Remove(instanceId);
    }

    /// <summary>The smallest interval anyone asked for, or <paramref name="fallback"/> when nobody did.</summary>
    public TimeSpan Effective(TimeSpan fallback)
    {
        lock (_gate) return _votes.Count == 0 ? fallback : _votes.Values.Min();
    }
}
