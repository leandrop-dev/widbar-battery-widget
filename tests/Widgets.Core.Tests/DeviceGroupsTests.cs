namespace Widgets.Core.Tests;

public class DeviceGroupsTests
{
    private static Metric Device(string id, string? icon) =>
        new($"battery:{id}", id, MetricKind.Battery, 50, "%", "battery", Icon: icon);

    [Fact]
    public void NoDevicesGiveNoGroups() => Assert.Empty(DeviceGroups.For([]));

    [Fact]
    public void OneHeadsetGivesOneGroup()
    {
        var groups = DeviceGroups.For([Device("a", MetricIcons.Headset)]);
        var group = Assert.Single(groups);
        Assert.Equal("Headsets", group.Title);
        Assert.Equal("headset", group.Key);
    }

    [Fact]
    public void GroupsFollowAFixedOrderWhateverTheInputOrder()
    {
        var groups = DeviceGroups.For([Device("pad", MetricIcons.Gamepad), Device("mouse", MetricIcons.Mouse), Device("kb", MetricIcons.Keyboard), Device("hs", MetricIcons.Headset)]);
        Assert.Equal(["Headsets", "Mice", "Keyboards", "Controllers"], groups.Select(g => g.Title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("generic")]
    [InlineData("toaster")]
    public void AnythingElseLandsInOther(string? icon)
    {
        var group = Assert.Single(DeviceGroups.For([Device("x", icon)]));
        Assert.Equal("Other", group.Title);
    }

    [Fact]
    public void OtherComesLast()
    {
        var groups = DeviceGroups.For([Device("x", "generic"), Device("hs", MetricIcons.Headset)]);
        Assert.Equal(["Headsets", "Other"], groups.Select(g => g.Title));
    }

    [Fact]
    public void DeviceOrderInsideAGroupIsKept()
    {
        var group = Assert.Single(DeviceGroups.For([Device("b", MetricIcons.Mouse), Device("a", MetricIcons.Mouse), Device("c", MetricIcons.Mouse)]));
        Assert.Equal(["b", "a", "c"], group.Devices.Select(d => d.Label));
    }

    [Fact]
    public void NonBatteryMetricsAreGroupedByTheirIconToo()
    {
        var cpu = new Metric("cpu", "CPU", MetricKind.Percent, 10, "%", "s", Icon: MetricIcons.Cpu);
        Assert.Equal("Other", Assert.Single(DeviceGroups.For([cpu])).Title);
    }
}
