using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class PreviewLayoutSettingsTests
{
    [Theory]
    [InlineData(PreviewLayout.VerticalMeters)]
    [InlineData(PreviewLayout.FillCapsules)]
    [InlineData(PreviewLayout.IconMatrix)]
    [InlineData(PreviewLayout.MinimalText)]
    [InlineData(PreviewLayout.RadialGauges)]
    public void EveryLayoutRoundTripsByName(PreviewLayout layout)
    {
        var s = new BatteryWidgetSettings();
        s.Appearance.Layout = layout;
        var json = s.ToJson();
        Assert.Contains($"\"{layout}\"", json);
        Assert.Equal(layout, BatteryWidgetSettings.FromJson(json).Appearance.Layout);
    }

    [Theory]
    [InlineData("\"Row\"")]          // the layout this widget used to offer
    [InlineData("\"nonsense\"")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{ \"a\": 1 }")]
    [InlineData("[1, 2]")]
    public void AnUnknownLayoutFallsBackAndKeepsEveryOtherSetting(string layoutJson)
    {
        var s = BatteryWidgetSettings.FromJson(
            $$"""{ "LowThreshold": 35, "RefreshMinutes": 4, "Appearance": { "Layout": {{layoutJson}}, "Scale": 115, "ShowNames": false } }""");
        Assert.Equal(PreviewLayout.VerticalMeters, s.Appearance.Layout);
        Assert.Equal(35, s.LowThreshold);
        Assert.Equal(4, s.RefreshMinutes);
        Assert.Equal(115, s.Appearance.Scale);
        Assert.False(s.Appearance.ShowNames);
    }

    [Fact]
    public void ANumericLayoutThatIsValidIsAccepted() =>
        Assert.Equal(PreviewLayout.RadialGauges,
            BatteryWidgetSettings.FromJson("""{ "Appearance": { "Layout": 4 } }""").Appearance.Layout);
}

public class LayoutSupportTests
{
    private static readonly PreviewLayout[] Layouts = Enum.GetValues<PreviewLayout>();

    [Fact]
    public void IconsAreOnlyUsedByTheIconMatrix()
    {
        foreach (var l in Layouts)
            Assert.Equal(l == PreviewLayout.IconMatrix, LayoutSupport.Honours(l, PreviewOption.Icons));
    }

    [Fact]
    public void SeparatorsAreOnlyUsedByMinimalText()
    {
        foreach (var l in Layouts)
            Assert.Equal(l == PreviewLayout.MinimalText, LayoutSupport.Honours(l, PreviewOption.Separators));
    }

    [Fact]
    public void EveryOtherOptionIsUsedByEveryLayout()
    {
        var shared = Enum.GetValues<PreviewOption>().Where(o => o is not (PreviewOption.Icons or PreviewOption.Separators));
        foreach (var l in Layouts)
            foreach (var o in shared)
                Assert.True(LayoutSupport.Honours(l, o), $"{l} should honour {o}");
    }

    [Fact]
    public void AnUnknownLayoutHonoursOnlyTheSharedOptions()
    {
        Assert.False(LayoutSupport.Honours((PreviewLayout)99, PreviewOption.Icons));
        Assert.True(LayoutSupport.Honours((PreviewLayout)99, PreviewOption.Spacing));
    }
}

public class PreviewGridTests
{
    [Fact]
    public void FourItemsFillColumnByColumn()
    {
        Assert.Equal((0, 0), PreviewGrid.Cell(0, 4));
        Assert.Equal((0, 1), PreviewGrid.Cell(1, 4));
        Assert.Equal((1, 0), PreviewGrid.Cell(2, 4));
        Assert.Equal((1, 1), PreviewGrid.Cell(3, 4));
    }

    [Fact]
    public void ThreeItemsLeaveTheLastCellEmpty()
    {
        Assert.Equal((0, 0), PreviewGrid.Cell(0, 3));
        Assert.Equal((0, 1), PreviewGrid.Cell(1, 3));
        Assert.Equal((1, 0), PreviewGrid.Cell(2, 3));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void ColumnsIsTheCountDividedByTheRowsRoundedUp(int count, int columns) =>
        Assert.Equal(columns, PreviewGrid.Columns(count));

    [Fact]
    public void ARowCountOfOneGivesASingleRow()
    {
        Assert.Equal((2, 0), PreviewGrid.Cell(2, 4, rows: 1));
        Assert.Equal(4, PreviewGrid.Columns(4, rows: 1));
    }
}

public class ArcTests
{
    private const double Tolerance = 1e-9;

    private static void AssertPoint((double X, double Y) expected, (double X, double Y) actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }

    [Fact]
    public void ARingStartsAtTwelveOClock() => AssertPoint((10, 0), Arc.PointAt(10, 10, 10, 0));

    [Fact]
    public void AQuarterIsAtThreeOClock() => AssertPoint((20, 10), Arc.PointAt(10, 10, 10, 0.25));

    [Fact]
    public void AHalfIsAtSixOClock() => AssertPoint((10, 20), Arc.PointAt(10, 10, 10, 0.5));

    [Fact]
    public void ThreeQuartersIsAtNineOClock() => AssertPoint((0, 10), Arc.PointAt(10, 10, 10, 0.75));

    [Fact]
    public void TheFractionIsClamped()
    {
        AssertPoint(Arc.PointAt(10, 10, 10, 0), Arc.PointAt(10, 10, 10, -3));
        AssertPoint(Arc.PointAt(10, 10, 10, 1), Arc.PointAt(10, 10, 10, 7));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.3, false)]
    [InlineData(0.5, false)]
    [InlineData(0.51, true)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void AnArcPastHalfARingIsLarge(double fraction, bool large) =>
        Assert.Equal(large, Arc.IsLargeArc(fraction));
}
