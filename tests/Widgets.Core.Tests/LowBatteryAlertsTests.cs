using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class LowBatteryAlertsTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    private static AlertRules Rules(int lowAt = 20, IReadOnlySet<string>? hidden = null, IReadOnlyDictionary<string, string>? labels = null) =>
        new(lowAt, hidden ?? None, labels ?? NoLabels);

    private static Metric Dev(double? value, string id = "battery:a", bool charging = false, bool stale = false, bool superseded = false) =>
        new(id, "Mouse", MetricKind.Battery, value, "%", "battery", Stale: stale, Charging: charging, Superseded: superseded);

    [Fact]
    public void AlertsOnceWhenALiveReadingReachesTheLowLevel()
    {
        var alerts = new LowBatteryAlerts();
        var first = Assert.Single(alerts.Evaluate([Dev(20)], Rules()));
        Assert.Equal(new Alert("battery:a", "Mouse", 20), first);
        Assert.Empty(alerts.Evaluate([Dev(19)], Rules()));
        Assert.Empty(alerts.Evaluate([Dev(5)], Rules()));
    }

    [Fact]
    public void ADeviceAboveTheLevelDoesNotAlert() =>
        Assert.Empty(new LowBatteryAlerts().Evaluate([Dev(21)], Rules()));

    [Fact]
    public void AChargingDeviceNeverAlertsAndChargingRearmsIt()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Empty(alerts.Evaluate([Dev(10, charging: true)], Rules()));
        alerts.Evaluate([Dev(15)], Rules());
        alerts.Evaluate([Dev(18, charging: true)], Rules());
        Assert.Single(alerts.Evaluate([Dev(15)], Rules())); // drained again after charging: alerts again
    }

    [Fact]
    public void ClimbingAboveTheRecoveryBandRearmsButFlickerDoesNot()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Single(alerts.Evaluate([Dev(20)], Rules()));

        // 21 to 25 is the recovery band: still the same discharge, so no second alert.
        foreach (var v in new double[] { 21, 25, 20, 22 })
            Assert.Empty(alerts.Evaluate([Dev(v)], Rules()));

        Assert.Empty(alerts.Evaluate([Dev(26)], Rules())); // recovered (no alert for being high)...
        Assert.Single(alerts.Evaluate([Dev(20)], Rules())); // ...so the next drop alerts again
    }

    [Fact]
    public void AStaleOrSwitchedOffDeviceSaysNothingAndKeepsItsState()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Empty(alerts.Evaluate([Dev(10, stale: true)], Rules()));
        Assert.Empty(alerts.Evaluate([Dev(null)], Rules()));

        Assert.Single(alerts.Evaluate([Dev(10)], Rules()));
        Assert.Empty(alerts.Evaluate([Dev(null)], Rules()));  // switched off and on again at the same level
        Assert.Empty(alerts.Evaluate([Dev(10)], Rules()));
    }

    [Fact]
    public void ASupersededDeviceAndAHiddenEverywhereDeviceNeverAlert()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Empty(alerts.Evaluate([Dev(5, superseded: true)], Rules()));
        Assert.Empty(alerts.Evaluate([Dev(5)], Rules(hidden: new HashSet<string> { "battery:a" })));
        Assert.Single(alerts.Evaluate([Dev(5)], Rules())); // shown again: it alerts now
    }

    [Fact]
    public void OnlyBatteriesAlertAndEachDeviceHasItsOwnState()
    {
        var alerts = new LowBatteryAlerts();
        var cpu = new Metric("system:cpu", "CPU", MetricKind.Percent, 3, "%", "system");
        var found = alerts.Evaluate([cpu, Dev(10, "battery:a"), Dev(12, "battery:b")], Rules());
        Assert.Equal(["battery:a", "battery:b"], found.Select(a => a.MetricId));
    }

    [Fact]
    public void TheMessageUsesTheUsersNameForTheDevice()
    {
        var labels = new Dictionary<string, string> { ["battery:a"] = "Work mouse" };
        Assert.Equal("Work mouse", Assert.Single(new LowBatteryAlerts().Evaluate([Dev(10)], Rules(labels: labels))).Label);
    }

    [Fact]
    public void ARoundedLevelIsReported() =>
        Assert.Equal(20, Assert.Single(new LowBatteryAlerts().Evaluate([Dev(19.6)], Rules())).Percent);

    // ---- votes

    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    [Fact]
    public void NoCopyWantingAlertsMeansNoRules()
    {
        var votes = new AlertVotes();
        Assert.Null(votes.Effective());
        votes.Set("one", enabled: false, 20, [], NoNames);
        Assert.Null(votes.Effective());
    }

    [Fact]
    public void AlertsFireAtTheHighestLevelAskedFor()
    {
        var votes = new AlertVotes();
        votes.Set("one", true, 20, [], NoNames);
        votes.Set("two", true, 30, [], NoNames);
        Assert.Equal(30, votes.Effective()!.LowAt);
    }

    [Fact]
    public void ADeviceIsSkippedOnlyWhenEveryCopyThatWantsAlertsHidesIt()
    {
        var votes = new AlertVotes();
        votes.Set("one", true, 20, ["battery:a", "battery:b"], NoNames);
        votes.Set("two", true, 20, ["battery:a"], NoNames);
        Assert.Equal(["battery:a"], votes.Effective()!.HiddenEverywhere);
    }

    [Fact]
    public void ACopyWithAlertsOffDoesNotCountAndRemovingACopyWithdrawsItsVote()
    {
        var votes = new AlertVotes();
        votes.Set("one", true, 20, ["battery:a"], NoNames);
        votes.Set("two", false, 50, [], NoNames);
        Assert.Equal(20, votes.Effective()!.LowAt);
        Assert.Equal(["battery:a"], votes.Effective()!.HiddenEverywhere);

        votes.Remove("one");
        Assert.Null(votes.Effective());
    }

    [Fact]
    public void TheNameComesFromACopyThatShowsTheDevice()
    {
        var votes = new AlertVotes();
        votes.Set("one", true, 20, ["battery:a"], new Dictionary<string, string> { ["battery:a"] = "Hidden name" });
        votes.Set("two", true, 20, [], new Dictionary<string, string> { ["battery:a"] = "Desk mouse" });
        Assert.Equal("Desk mouse", votes.Effective()!.Labels["battery:a"]);
    }

    // ---- settings

    [Fact]
    public void AlertsAreOnByDefaultAndSurviveARoundTripAndOldSettings()
    {
        Assert.True(new BatteryWidgetSettings().LowBatteryAlerts);
        Assert.True(BatteryWidgetSettings.FromJson("""{ "RefreshMinutes": 2 }""").LowBatteryAlerts);
        var off = BatteryWidgetSettings.FromJson(new BatteryWidgetSettings { LowBatteryAlerts = false }.ToJson());
        Assert.False(off.LowBatteryAlerts);
        Assert.False(off.Clone().LowBatteryAlerts);
    }
}
