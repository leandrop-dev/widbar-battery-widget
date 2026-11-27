using Widgets.Core;
using Widgets.Core.Settings;
using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.WidBar;

/// <summary>
/// One battery poller for the whole widget process. Every placed copy of the widget (the user can put it on two
/// displays) reads from here, so the headset and mouse are queried once per poll no matter how many copies exist.
/// </summary>
internal static class BatteryHub
{
    private static readonly object Gate = new();
    private static MetricStore? _store;
    private static MetricScheduler? _scheduler;
    private static BatteryProvider? _provider;
    private static BatteryHistory? _history;
    private static readonly IntervalVotes Votes = new();
    private static readonly AlertVotes AlertWants = new();
    private static readonly LowBatteryAlerts Alerts = new();
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    /// <summary>Raised on a background thread after every poll. Marshal to the UI thread before touching XAML.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Metric> Snapshot() => _store?.Snapshot() ?? [];

    /// <summary>A device's readings from the last week, oldest first, by metric id (for example "battery:steelseries:1830").</summary>
    public static IReadOnlyList<HistorySample> History(string metricId) => _history?.Samples(metricId) ?? [];

    /// <summary>Starts polling. Safe to call from every copy: only the first call does anything.</summary>
    /// <param name="sharedDataDirectory">Where the last known levels are remembered between runs, so a sleeping mouse still has a value.</param>
    public static void Start(string sharedDataDirectory)
    {
        lock (Gate)
        {
            if (_scheduler is not null) return;
            var hid = new WindowsHid();
            _history = new BatteryHistory(Path.Combine(sharedDataDirectory, "battery-history.json"));
            _provider = new BatteryProvider(
                [new SteelSeriesReader(hid), new DualSenseReader(hid), new XInputReader(), new BluetoothLeBatteryReader()],
                interval: Votes.Effective(DefaultInterval),
                cache: new BatteryCache(Path.Combine(sharedDataDirectory, "battery-cache.json")),
                history: _history);
            _store = new MetricStore();
            _store.Changed += () =>
            {
                Changed?.Invoke();
                RaiseAlerts();
            };
            _scheduler = new MetricScheduler([_provider], _store);
            _scheduler.Start();
        }
    }

    /// <summary>
    /// Records how often one placed copy wants the batteries read (1 to 10 minutes). The shared poller runs at the smallest
    /// value among the copies. Safe to call before <see cref="Start"/>: the vote is used when polling begins.
    /// </summary>
    public static void SetRefresh(string instanceId, int minutes)
    {
        Votes.Set(instanceId, TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 10)));
        ApplyInterval();
    }

    /// <summary>Withdraws a copy's vote when it is removed from the taskbar.</summary>
    public static void ClearRefresh(string instanceId)
    {
        Votes.Remove(instanceId);
        ApplyInterval();
    }

    /// <summary>
    /// Records what one placed copy wants from the low-battery alerts. Alerts are on while any copy wants them, so two
    /// copies never show a notification each.
    /// </summary>
    public static void SetAlerts(string instanceId, BatteryWidgetSettings settings) =>
        AlertWants.Set(instanceId, settings.LowBatteryAlerts, settings.LowThreshold, settings.Hidden, settings.Labels);

    /// <summary>Withdraws a copy's wish when it is removed from the taskbar.</summary>
    public static void ClearAlerts(string instanceId) => AlertWants.Remove(instanceId);

    private static void RaiseAlerts()
    {
        if (AlertWants.Effective() is not { } rules) return;
        foreach (var alert in Alerts.Evaluate(Snapshot(), rules)) Notifier.Low(alert);
    }

    private static void ApplyInterval()
    {
        // Under the same lock as Start: a vote that arrives while the first copy is still building the provider is read
        // by Start, and two copies changing their votes at once cannot leave the poller on an out-of-date interval.
        lock (Gate)
        {
            if (_provider is null) return;
            var next = Votes.Effective(DefaultInterval);
            if (_provider.Interval == next) return;
            _provider.Interval = next;
            Refresh(); // the scheduler is waiting out the old interval; a nudge makes it wait the new one
        }
    }

    /// <summary>Asks for a poll now instead of waiting for the next one.</summary>
    public static void Refresh() => _scheduler?.RequestPoll("battery");
}
