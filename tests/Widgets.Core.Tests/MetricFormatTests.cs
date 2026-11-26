using System.Globalization;

namespace Widgets.Core.Tests;

public class MetricFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1234, "1.2k")]
    [InlineData(999_949, "999.9k")]
    [InlineData(999_950, "1M")]
    [InlineData(1_250_000, "1.3M")]
    [InlineData(2_500_000_000, "2.5B")]
    public void Compact_ShortensLargeNumbers(double v, string expected) =>
        Assert.Equal(expected, MetricFormat.Compact(v, CultureInfo.InvariantCulture));

    [Fact]
    public void Approximate_ValuesGetALeadingTilde()
    {
        var m = new Metric("c", "Cost", MetricKind.Cost, 6.356, "$", "s", Approximate: true);
        Assert.Equal("~$6.36", MetricFormat.Value(m, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PercentSymbolCanBeLeftOff()
    {
        var level = new Metric("a", "A", MetricKind.Battery, 36, "%", "s");
        Assert.Equal("36", MetricFormat.Value(level, percentSymbol: false, CultureInfo.InvariantCulture));
        Assert.Equal("36%", MetricFormat.Value(level, percentSymbol: true, CultureInfo.InvariantCulture));
        Assert.Equal(MetricFormat.Value(level, CultureInfo.InvariantCulture), MetricFormat.Value(level, percentSymbol: true, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PercentSymbolOffLeavesTextAndMissingValuesAlone()
    {
        Assert.Equal("--", MetricFormat.Value(new Metric("a", "A", MetricKind.Battery, null, "%", "s"), percentSymbol: false, CultureInfo.InvariantCulture));
        Assert.Equal("USB", MetricFormat.Value(new Metric("a", "A", MetricKind.Battery, null, "%", "s", Text: "USB"), percentSymbol: false, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PercentSymbolOffOnlyDropsThePercentUnit()
    {
        var temp = new Metric("t", "T", MetricKind.Percent, 61, "°C", "s");
        Assert.Equal("61°C", MetricFormat.Value(temp, percentSymbol: false, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NoValueAndPercent()
    {
        Assert.Equal("--", MetricFormat.Value(new Metric("a", "A", MetricKind.Percent, null, "%", "s"), CultureInfo.InvariantCulture));
        Assert.Equal("43%", MetricFormat.Value(new Metric("a", "A", MetricKind.Percent, 42.5, "%", "s"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Text_ReplacesTheValue()
    {
        var m = new Metric("a", "A", MetricKind.Battery, null, "%", "s", Text: "USB");
        Assert.Equal("USB", MetricFormat.Value(m, CultureInfo.InvariantCulture));
    }
}
