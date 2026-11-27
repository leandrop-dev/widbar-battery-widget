using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class FlagVotesTests
{
    [Fact]
    public void ItIsOffUntilACopyWantsItAndOnWhileAnyDoes()
    {
        var votes = new FlagVotes();
        Assert.False(votes.Any);

        votes.Set("one", true);
        votes.Set("two", true);
        Assert.True(votes.Any);

        votes.Set("one", false);
        Assert.True(votes.Any); // two still wants it
        votes.Set("two", false);
        Assert.False(votes.Any);
    }

    [Fact]
    public void SetReportsOnlyWhenTheOverallAnswerChanged()
    {
        var votes = new FlagVotes();
        Assert.True(votes.Set("one", true));    // off to on
        Assert.False(votes.Set("one", true));   // repeat
        Assert.False(votes.Set("two", true));   // already on
        Assert.False(votes.Set("one", false));  // two still wants it
        Assert.True(votes.Set("two", false));   // on to off
        Assert.False(votes.Set("two", false));
    }

    [Fact]
    public void RemovingACopyWithdrawsItsVote()
    {
        var votes = new FlagVotes();
        votes.Set("one", true);
        votes.Remove("one");
        votes.Remove("never-seen");
        Assert.False(votes.Any);
    }

    [Fact]
    public void TheBluetoothPadSettingIsOffByDefaultAndSurvivesARoundTripAndOldSettings()
    {
        Assert.False(new BatteryWidgetSettings().ReadDualSenseBluetooth);
        Assert.False(BatteryWidgetSettings.FromJson("""{ "RefreshMinutes": 2 }""").ReadDualSenseBluetooth);

        var on = BatteryWidgetSettings.FromJson(new BatteryWidgetSettings { ReadDualSenseBluetooth = true }.ToJson());
        Assert.True(on.ReadDualSenseBluetooth);
        Assert.True(on.Clone().ReadDualSenseBluetooth);
    }

    [Fact]
    public void TheExperimentalReadersSettingIsOffByDefaultAndSurvivesARoundTripAndOldSettings()
    {
        Assert.False(new BatteryWidgetSettings().ExperimentalReaders);
        Assert.False(BatteryWidgetSettings.FromJson("""{ "RefreshMinutes": 2 }""").ExperimentalReaders);

        var on = BatteryWidgetSettings.FromJson(new BatteryWidgetSettings { ExperimentalReaders = true }.ToJson());
        Assert.True(on.ExperimentalReaders);
        Assert.True(on.Clone().ExperimentalReaders);
        Assert.False(on.Clone().ReadDualSenseBluetooth); // the two switches are independent
    }
}
