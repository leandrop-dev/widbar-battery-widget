using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class PreviewLayoutSettingsTests
{
    [Theory]
    [InlineData(PreviewLayout.VerticalMeters)]
    [InlineData(PreviewLayout.MinimalIcons)]
    [InlineData(PreviewLayout.MinimalText)]
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
        Assert.Equal(PreviewLayout.MinimalIcons,
            BatteryWidgetSettings.FromJson("""{ "Appearance": { "Layout": 2 } }""").Appearance.Layout);
}

public class LayoutSupportTests
{
    private static readonly PreviewLayout[] Layouts = Enum.GetValues<PreviewLayout>();

    [Fact]
    public void IconsAreOnlyUsedByTheMinimalIcons()
    {
        foreach (var l in Layouts)
            Assert.Equal(l == PreviewLayout.MinimalIcons, LayoutSupport.Honours(l, PreviewOption.Icons));
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

