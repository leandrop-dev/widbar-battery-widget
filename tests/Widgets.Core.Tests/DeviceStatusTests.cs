namespace Widgets.Core.Tests;

public class DeviceStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static Metric Battery(double? value, bool stale = false, bool charging = false, string? text = null, DateTimeOffset? updated = null) =>
        new("battery:x", "X", MetricKind.Battery, value, "%", "battery", updated, stale, Charging: charging, Text: text);

    [Fact]
    public void Live_HasNoStatus() => Assert.Equal("", DeviceStatus.Describe(Battery(80), Now));

    [Fact]
    public void Charging_SaysSo() => Assert.Equal("Charging", DeviceStatus.Describe(Battery(80, charging: true), Now));

    [Fact]
    public void NoValue_IsSwitchedOff() => Assert.Equal("Switched off", DeviceStatus.Describe(Battery(null), Now));

    [Fact]
    public void OwnText_WinsOverEverything() => Assert.Equal("USB", DeviceStatus.Describe(Battery(null, text: "USB", charging: true), Now));

    [Fact]
    public void Stale_SaysWhenItWasLastSeen() =>
        Assert.Equal("No signal, last seen 3 min ago", DeviceStatus.Describe(Battery(33, stale: true, updated: Now.AddMinutes(-3)), Now));

    [Fact]
    public void Stale_WithoutATimestampStillSaysNoSignal() => Assert.Equal("No signal", DeviceStatus.Describe(Battery(33, stale: true), Now));

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(3600, "1 h ago")]
    [InlineData(23 * 3600, "23 h ago")]
    [InlineData(24 * 3600, "1 d ago")]
    [InlineData(-30, "just now")]
    public void Ago_ChoosesTheLargestSensibleUnit(int seconds, string expected) =>
        Assert.Equal(expected, DeviceStatus.Ago(TimeSpan.FromSeconds(seconds)));
}
