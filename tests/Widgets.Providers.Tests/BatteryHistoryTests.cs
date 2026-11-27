using Widgets.Core;
using Widgets.Providers.Battery;

namespace Widgets.Providers.Tests;

public class BatteryHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static string HistoryFile(TempPath t) => Path.Combine(t.Dir, "sub", "battery-history.json");

    [Fact]
    public void ARecordedReadingComesBackAndASteadyOneIsNotRepeated()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));

        Assert.True(h.Record("battery:a", 80, false, T0));
        Assert.False(h.Record("battery:a", 80, false, T0.AddMinutes(1)));   // same level, same state, too soon
        Assert.False(h.Record("battery:a", 80, false, T0.AddMinutes(14)));
        Assert.True(h.Record("battery:a", 79, false, T0.AddMinutes(15)));   // level changed

        Assert.Equal([80, 79], h.Samples("battery:a").Select(s => s.Percent));
    }

    [Fact]
    public void AChangeOfChargingStateAndTheHeartbeatAreRecorded()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        h.Record("battery:a", 50, false, T0);

        Assert.True(h.Record("battery:a", 50, true, T0.AddMinutes(1)));                        // plugged in at the same level
        Assert.False(h.Record("battery:a", 50, true, T0.AddMinutes(2)));
        Assert.True(h.Record("battery:a", 50, true, T0.AddMinutes(1) + BatteryHistory.Heartbeat)); // a flat stretch still gets a point
        Assert.Equal(3, h.Samples("battery:a").Count);
    }

    [Fact]
    public void EachDeviceHasItsOwnHistoryAndAnUnknownOneIsEmpty()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        h.Record("battery:a", 50, false, T0);
        h.Record("battery:b", 70, false, T0);
        Assert.Equal(50, Assert.Single(h.Samples("battery:a")).Percent);
        Assert.Equal(70, Assert.Single(h.Samples("battery:b")).Percent);
        Assert.Empty(h.Samples("battery:none"));
    }

    [Fact]
    public void ReadingsOlderThanAWeekAreDropped()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        h.Record("battery:a", 90, false, T0);
        h.Record("battery:a", 40, false, T0 + BatteryHistory.Keep + TimeSpan.FromMinutes(1));
        Assert.Equal([40], h.Samples("battery:a").Select(s => s.Percent));
    }

    [Fact]
    public void NonsenseLevelsAndAClockThatMovedBackAreIgnored()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        Assert.False(h.Record("battery:a", -1, false, T0));
        Assert.False(h.Record("battery:a", 101, false, T0));
        h.Record("battery:a", 50, false, T0);
        Assert.False(h.Record("battery:a", 40, false, T0.AddMinutes(-5)));
        Assert.Equal(50, Assert.Single(h.Samples("battery:a")).Percent);
    }

    [Fact]
    public void SamplesIsACopyTheCallerCannotUseToChangeTheHistory()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        h.Record("battery:a", 50, false, T0);
        var copy = (List<HistorySample>)[.. h.Samples("battery:a")];
        copy.Clear();
        Assert.Single(h.Samples("battery:a"));
    }

    [Fact]
    public void HistorySurvivesARestart()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        h.Record("battery:a", 60, false, T0);
        h.Record("battery:a", 59, true, T0.AddHours(1));
        Assert.True(h.Save());

        var back = new BatteryHistory(HistoryFile(t), new FakeTime(T0.AddHours(2))).Samples("battery:a");
        Assert.Equal([60, 59], back.Select(s => s.Percent));
        Assert.Equal([false, true], back.Select(s => s.Charging));
        Assert.Equal(T0, back[0].At);
    }

    [Fact]
    public void ARestartDropsOldAndInvalidReadings()
    {
        using var t = new TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile(t))!);
        File.WriteAllText(HistoryFile(t), $$"""
            { "battery:a": [
                { "At": "{{T0.AddDays(-8):O}}", "Percent": 90, "Charging": false },
                { "At": "{{T0.AddHours(-3):O}}", "Percent": 70, "Charging": false },
                { "At": "{{T0.AddHours(-2):O}}", "Percent": 250, "Charging": false },
                { "At": "{{T0.AddHours(5):O}}", "Percent": 60, "Charging": false } ],
              "battery:gone": [ { "At": "{{T0.AddDays(-9):O}}", "Percent": 10, "Charging": false } ] }
            """);
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        Assert.Equal([70], h.Samples("battery:a").Select(s => s.Percent)); // the old, the impossible and the future are dropped
        Assert.Empty(h.Samples("battery:gone"));
    }

    [Fact]
    public void AMissingOrDamagedFileMeansNoHistoryAndStillWorks()
    {
        using var t = new TempPath();
        Assert.Empty(new BatteryHistory(HistoryFile(t), new FakeTime(T0)).Samples("battery:a"));

        Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile(t))!);
        File.WriteAllText(HistoryFile(t), "not json");
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        Assert.Empty(h.Samples("battery:a"));
        Assert.True(h.Record("battery:a", 50, false, T0));
        Assert.True(h.Save());
    }

    [Fact]
    public void SaveWritesOnlyWhenSomethingChanged()
    {
        using var t = new TempPath();
        var h = new BatteryHistory(HistoryFile(t), new FakeTime(T0));
        Assert.True(h.Save());
        Assert.False(File.Exists(HistoryFile(t))); // nothing recorded, nothing written

        h.Record("battery:a", 50, false, T0);
        Assert.True(h.Save());
        Assert.True(File.Exists(HistoryFile(t)));
        Assert.False(File.Exists(HistoryFile(t) + ".tmp"));

        File.Delete(HistoryFile(t));
        Assert.True(h.Save());                     // not dirty any more
        Assert.False(File.Exists(HistoryFile(t)));
    }

    // ---- the provider records what it sees

    private static BatteryReading Mouse(int? pct, bool connected = true, bool charging = false) =>
        new("steelseries:1830", "SteelSeries Rival 3 Wireless", DeviceKind.Mouse, pct, charging, connected);

    [Fact]
    public async Task TheProviderRecordsLiveReadingsUnderTheMetricId()
    {
        using var t = new TempPath();
        var time = new FakeTime(T0);
        var history = new BatteryHistory(HistoryFile(t), time);
        var level = 40;
        var p = new BatteryProvider([new Reader(() => [Mouse(level)])], time: time, history: history);

        await p.PollAsync(default);
        time.Now = T0.AddMinutes(5);
        level = 39;
        await p.PollAsync(default);

        Assert.Equal([40, 39], history.Samples("battery:steelseries:1830").Select(s => s.Percent));
        Assert.True(File.Exists(HistoryFile(t))); // saved after the poll that added a reading
    }

    [Fact]
    public async Task TheProviderDoesNotRecordASilentOrSwitchedOffDevice()
    {
        using var t = new TempPath();
        var time = new FakeTime(T0);
        var history = new BatteryHistory(HistoryFile(t), time);
        var steps = new Queue<IReadOnlyList<BatteryReading>>([[Mouse(40)], [], [Mouse(null, connected: false)]]);
        var p = new BatteryProvider([new Reader(steps.Dequeue)], time: time, history: history);

        await p.PollAsync(default);
        time.Now = T0.AddHours(1);
        await p.PollAsync(default); // silent: the tile shows the last value, dimmed
        time.Now = T0.AddHours(2);
        await p.PollAsync(default); // reported off

        Assert.Equal(40, Assert.Single(history.Samples("battery:steelseries:1830")).Percent);
    }

    [Fact]
    public async Task TheProviderRecordsChargingAndWorksWithoutAHistory()
    {
        using var t = new TempPath();
        var time = new FakeTime(T0);
        var history = new BatteryHistory(HistoryFile(t), time);
        await new BatteryProvider([new Reader(() => [Mouse(40, charging: true)])], time: time, history: history).PollAsync(default);
        Assert.True(Assert.Single(history.Samples("battery:steelseries:1830")).Charging);

        var m = Assert.Single(await new BatteryProvider([new Reader(() => [Mouse(40)])], time: time).PollAsync(default));
        Assert.Equal(40, m.Value);
    }
}
