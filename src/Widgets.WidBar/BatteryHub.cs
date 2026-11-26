using Widgets.Core;
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

    /// <summary>Raised on a background thread after every poll. Marshal to the UI thread before touching XAML.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Metric> Snapshot() => _store?.Snapshot() ?? [];

    /// <summary>Starts polling. Safe to call from every copy: only the first call does anything.</summary>
    /// <param name="sharedDataDirectory">Where the last known levels are remembered between runs, so a sleeping mouse still has a value.</param>
    public static void Start(string sharedDataDirectory)
    {
        lock (Gate)
        {
            if (_scheduler is not null) return;
            var hid = new WindowsHid();
            var provider = new BatteryProvider(
                [new SteelSeriesReader(hid), new DualSenseReader(hid), new XInputReader(), new BluetoothLeBatteryReader()],
                cache: new BatteryCache(Path.Combine(sharedDataDirectory, "battery-cache.json")));
            _store = new MetricStore();
            _store.Changed += () => Changed?.Invoke();
            _scheduler = new MetricScheduler([provider], _store);
            _scheduler.Start();
        }
    }

    /// <summary>Asks for a poll now instead of waiting for the next one.</summary>
    public static void Refresh() => _scheduler?.RequestPoll("battery");
}
