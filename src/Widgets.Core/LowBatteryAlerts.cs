namespace Widgets.Core;

/// <summary>What the alerts need to know: the level to alert at, devices nobody wants to see, and the names the user gave.</summary>
/// <param name="LowAt">Alert when a device is at or below this percent.</param>
/// <param name="HiddenEverywhere">Metric ids turned off in every copy of the widget; they never alert.</param>
/// <param name="Labels">The user's name for a device, by metric id, used in the message instead of the device's own name.</param>
public sealed record AlertRules(int LowAt, IReadOnlySet<string> HiddenEverywhere, IReadOnlyDictionary<string, string> Labels);

/// <summary>One device that just crossed the low level.</summary>
public sealed record Alert(string MetricId, string Label, int Percent);

/// <summary>
/// Decides when a device gets a low-battery alert: once when a live reading first reaches the low level, and again only
/// after the device has charged or recovered. Pure apart from the memory of who was already alerted, so the rules are
/// tested without any UI. Safe to call from different threads.
/// </summary>
public sealed class LowBatteryAlerts
{
    /// <summary>How far above the low level a device must climb to be alerted again. Stops 20/21/20 flicker from repeating.</summary>
    public const int Recovery = 5;

    private readonly object _gate = new();
    private readonly HashSet<string> _alerted = new(StringComparer.Ordinal);

    public IReadOnlyList<Alert> Evaluate(IReadOnlyList<Metric> metrics, AlertRules rules)
    {
        var alerts = new List<Alert>();
        lock (_gate)
        {
            foreach (var m in metrics)
            {
                if (m.Kind != MetricKind.Battery || m.Superseded || rules.HiddenEverywhere.Contains(m.Id)) continue;

                // Only a live reading says anything. A stale, remembered or switched-off device keeps its state.
                if (m.Stale || m.Value is not { } value) continue;

                if (m.Charging || value > rules.LowAt + Recovery)
                {
                    _alerted.Remove(m.Id);
                    continue;
                }
                if (value > rules.LowAt) continue; // in the recovery band: neither alerts nor re-arms

                if (_alerted.Add(m.Id))
                    alerts.Add(new Alert(m.Id, rules.Labels.GetValueOrDefault(m.Id, m.Label), (int)Math.Round(value)));
            }
        }
        return alerts;
    }
}

/// <summary>
/// What each placed copy of the widget wants from the alerts. The copies share one poller, so alerts are on when any copy
/// wants them, fire at the highest level asked for, and skip a device only when every copy that wants alerts hides it.
/// Safe to call from different threads.
/// </summary>
public sealed class AlertVotes
{
    private sealed record Vote(int LowAt, IReadOnlySet<string> Hidden, IReadOnlyDictionary<string, string> Labels);

    private readonly object _gate = new();
    private readonly Dictionary<string, Vote> _votes = new(StringComparer.Ordinal);

    /// <summary>Records (or replaces) what <paramref name="instanceId"/> wants. <paramref name="enabled"/> false withdraws its vote.</summary>
    public void Set(string instanceId, bool enabled, int lowAt, IEnumerable<string> hidden, IReadOnlyDictionary<string, string> labels)
    {
        lock (_gate)
        {
            if (!enabled) _votes.Remove(instanceId);
            else _votes[instanceId] = new Vote(lowAt, hidden.ToHashSet(StringComparer.Ordinal), new Dictionary<string, string>(labels, StringComparer.Ordinal));
        }
    }

    public void Remove(string instanceId)
    {
        lock (_gate) _votes.Remove(instanceId);
    }

    /// <summary>The rules to apply, or null when no copy wants alerts.</summary>
    public AlertRules? Effective()
    {
        lock (_gate)
        {
            if (_votes.Count == 0) return null;

            var hidden = new HashSet<string>(_votes.Values.First().Hidden, StringComparer.Ordinal);
            foreach (var vote in _votes.Values.Skip(1)) hidden.IntersectWith(vote.Hidden);

            // A name from the first copy that gives one wins; a copy that hides the device does not get a say.
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var vote in _votes.Values)
                foreach (var (id, label) in vote.Labels)
                    if (!vote.Hidden.Contains(id)) labels.TryAdd(id, label);

            return new AlertRules(_votes.Values.Max(v => v.LowAt), hidden, labels);
        }
    }
}
