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


public class OptionUseTests
{
    private static AppearanceSettings Look(Action<AppearanceSettings>? change = null)
    {
        var a = new AppearanceSettings();
        change?.Invoke(a);
        return a;
    }

    [Fact]
    public void AlignmentNeedsRoomToMoveIn()
    {
        Assert.Equal("Used when the width adjustment adds room", LayoutSupport.Unused(Look(), PreviewOption.Alignment));
        Assert.Null(LayoutSupport.Unused(Look(a => a.WidthAdjustment = 40), PreviewOption.Alignment));
        Assert.Null(LayoutSupport.Unused(Look(a => a.WidthAdjustment = -40), PreviewOption.Alignment)); // decides which side is clipped
    }

    [Theory]
    [InlineData(PreviewLayout.VerticalMeters, true, false)]
    [InlineData(PreviewLayout.VerticalMeters, false, true)]
    [InlineData(PreviewLayout.MinimalText, false, true)]
    [InlineData(PreviewLayout.MinimalIcons, false, false)] // the icon still takes the colour
    public void ColouredLabelsAndIconsNeedsALabelOrAnIcon(PreviewLayout layout, bool names, bool unused)
    {
        var a = Look(x => { x.Layout = layout; x.ShowNames = names; });
        Assert.Equal(unused, LayoutSupport.Unused(a, PreviewOption.ColouredLabelsAndIcons) is not null);
    }

    [Fact]
    public void BatteryColoursAreUnusedWhileTheWindowsAccentIsOn()
    {
        Assert.Null(LayoutSupport.Unused(Look(), PreviewOption.BatteryColours));
        Assert.Equal("Not used while Windows accent color is on",
            LayoutSupport.Unused(Look(a => a.UseAccentColour = true), PreviewOption.BatteryColours));
    }

    [Fact]
    public void ALayoutReasonComesFirst() =>
        Assert.Equal("Not used by this layout", LayoutSupport.Unused(Look(a => a.Layout = PreviewLayout.VerticalMeters), PreviewOption.Icons));

    [Fact]
    public void HonoursByLayoutAloneStillAnswersForBatteryColours()
    {
        foreach (var l in Enum.GetValues<PreviewLayout>())
            Assert.True(LayoutSupport.Honours(l, PreviewOption.BatteryColours));
    }
}
