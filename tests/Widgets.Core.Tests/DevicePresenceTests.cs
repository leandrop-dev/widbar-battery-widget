using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class DevicePresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Half = TimeSpan.FromMinutes(30);

    private static Metric Device(DateTimeOffset? offlineSince = null, bool superseded = false, bool stale = false, DateTimeOffset? updated = null) =>
        new("battery:x", "X", MetricKind.Battery, 50, "%", "battery", updated, stale, OfflineSince: offlineSince, Superseded: superseded);

    [Fact]
    public void AConnectedDeviceIsAlwaysShown() =>
        Assert.True(DevicePresence.IsShown(Device(), Now, Half));

    [Fact]
    public void ADeviceDisconnectedForLessThanTheLimitStaysShownDimmed() =>
        Assert.True(DevicePresence.IsShown(Device(offlineSince: Now.AddMinutes(-29)), Now, Half));

    [Fact]
    public void ADeviceDisconnectedExactlyTheLimitIsStillShown() =>
        Assert.True(DevicePresence.IsShown(Device(offlineSince: Now - Half), Now, Half));

    [Fact]
    public void ADeviceDisconnectedLongerThanTheLimitIsHidden() =>
        Assert.False(DevicePresence.IsShown(Device(offlineSince: Now.AddMinutes(-31)), Now, Half));

    [Fact]
    public void WithNoLimitADisconnectedDeviceIsNeverHiddenForTime() =>
        Assert.True(DevicePresence.IsShown(Device(offlineSince: Now.AddDays(-20)), Now, null));

    [Fact]
    public void ASupersededDeviceIsHiddenWhateverTheLimit()
    {
        var m = Device(offlineSince: Now, superseded: true);
        Assert.False(DevicePresence.IsShown(m, Now, Half));
        Assert.False(DevicePresence.IsShown(m, Now, null));
    }

    [Fact]
    public void AStaleReadingWithNoOfflineTimeCountsFromWhenItWasTaken()
    {
        Assert.False(DevicePresence.IsShown(Device(stale: true, updated: Now.AddHours(-2)), Now, Half));
        Assert.True(DevicePresence.IsShown(Device(stale: true, updated: Now.AddMinutes(-5)), Now, Half));
    }

    [Fact]
    public void AClockThatMovedBackDoesNotHideAnything() =>
        Assert.True(DevicePresence.IsShown(Device(offlineSince: Now.AddHours(1)), Now, Half));

    [Fact]
    public void WhyHiddenNamesTheReasonOrIsNullForAShownDevice()
    {
        Assert.Null(DevicePresence.WhyHidden(Device(), Now, Half));
        Assert.Contains("another controller", DevicePresence.WhyHidden(Device(superseded: true), Now, Half));
        Assert.Contains("disconnected", DevicePresence.WhyHidden(Device(offlineSince: Now.AddHours(-1)), Now, Half));
    }

    [Fact]
    public void HideAfterDefaultsToThirtyMinutesAndZeroMeansNever()
    {
        Assert.Equal(Half, new BatteryWidgetSettings().HideAfter);
        Assert.Null(new BatteryWidgetSettings { HideAfterMinutes = 0 }.HideAfter);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(99999, 1440)]
    public void Sanitize_ClampsHideAfterMinutes(int input, int expected) =>
        Assert.Equal(expected, BatteryWidgetSettings.Sanitize(new BatteryWidgetSettings { HideAfterMinutes = input }).HideAfterMinutes);

    [Fact]
    public void SettingsFromBeforeTheOptionExistedGetTheDefaultAndTheValueRoundTrips()
    {
        Assert.Equal(30, BatteryWidgetSettings.FromJson("""{ "RefreshMinutes": 2 }""").HideAfterMinutes);
        Assert.Equal(0, BatteryWidgetSettings.FromJson(new BatteryWidgetSettings { HideAfterMinutes = 0 }.ToJson()).HideAfterMinutes);
        Assert.DoesNotContain("\"HideAfter\":", new BatteryWidgetSettings().ToJson().Replace("HideAfterMinutes", ""));
    }
}
