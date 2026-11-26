using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class AppearanceSettingsTests
{
    [Fact]
    public void Defaults_AreTransparentOutlineClassicAuto()
    {
        var a = new AppearanceSettings();
        Assert.Equal(PillStyle.Transparent, a.Pill);
        Assert.Equal(IconStyle.Outline, a.Icons);
        Assert.Equal(BatteryPalette.Classic, a.Palette);
        Assert.Equal(ThemeChoice.Auto, a.Theme);
        Assert.Equal(BackgroundStyle.Acrylic, a.Background);
        Assert.Equal(100, a.Scale);
        Assert.Equal(55, a.BackgroundOpacity);
        Assert.True(a.ShowNames);
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(115, 115)]
    [InlineData(97, 100)] // not offered: snaps to the nearest size
    [InlineData(500, 130)]
    [InlineData(-5, 90)]
    public void Sanitize_SnapsTheScaleToAnOfferedSize(int input, int expected) =>
        Assert.Equal(expected, AppearanceSettings.Sanitize(new AppearanceSettings { Scale = input }).Scale);

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(40, 40)]
    [InlineData(250, 100)]
    public void Sanitize_ClampsTheOpacity(int input, int expected) =>
        Assert.Equal(expected, AppearanceSettings.Sanitize(new AppearanceSettings { BackgroundOpacity = input }).BackgroundOpacity);

    [Fact]
    public void Sanitize_ReplacesBadColoursAndUpperCasesGoodOnes()
    {
        var s = AppearanceSettings.Sanitize(new AppearanceSettings { CustomHigh = "#abcdef", CustomMedium = "red", CustomLow = "#12345" });
        Assert.Equal("#ABCDEF", s.CustomHigh);
        Assert.Equal("#F5C842", s.CustomMedium);
        Assert.Equal("#FF9999", s.CustomLow);
    }

    [Fact]
    public void Sanitize_ReplacesUnknownEnumValues()
    {
        var s = AppearanceSettings.Sanitize(new AppearanceSettings { Pill = (PillStyle)99, Icons = (IconStyle)99, Palette = (BatteryPalette)99, Theme = (ThemeChoice)99, Background = (BackgroundStyle)99 });
        Assert.Equal(PillStyle.Transparent, s.Pill);
        Assert.Equal(IconStyle.Outline, s.Icons);
        Assert.Equal(BatteryPalette.Classic, s.Palette);
        Assert.Equal(ThemeChoice.Auto, s.Theme);
        Assert.Equal(BackgroundStyle.Acrylic, s.Background);
    }

    [Fact]
    public void Clone_IsIndependentOfTheOriginal()
    {
        var s = new WidgetSettings();
        var c = s.Clone();
        c.Appearance.Pill = PillStyle.Compact;
        Assert.Equal(PillStyle.Transparent, s.Appearance.Pill);
    }

    [Fact]
    public void Defaults_FollowSystemMetrics()
    {
        var a = new AppearanceSettings();
        Assert.Equal(PreviewLayout.VerticalMeters, a.Layout);
        Assert.Equal(TileSpacing.Standard, a.Spacing);
        Assert.Equal(ContentAlignment.Center, a.Alignment);
        Assert.Equal(0, a.WidthAdjustment);
        Assert.True(a.ShowPercentSymbol);
        Assert.True(a.ShowSeparators);
        Assert.True(a.ColouredLabelsAndIcons);
        Assert.False(a.ColouredValues);
        Assert.True(a.ShowChargingBolt);
        Assert.False(a.UseAccentColour);
    }

    [Theory]
    [InlineData(999, 400)]
    [InlineData(-999, -120)]
    [InlineData(-120, -120)]
    [InlineData(37, 37)]
    public void Sanitize_ClampsTheWidthAdjustment(int input, int expected) =>
        Assert.Equal(expected, AppearanceSettings.Sanitize(new AppearanceSettings { WidthAdjustment = input }).WidthAdjustment);

    [Fact]
    public void Sanitize_ReplacesUnknownLayoutSpacingAndAlignment()
    {
        var s = AppearanceSettings.Sanitize(new AppearanceSettings { Layout = (PreviewLayout)99, Spacing = (TileSpacing)99, Alignment = (ContentAlignment)99 });
        Assert.Equal(PreviewLayout.VerticalMeters, s.Layout);
        Assert.Equal(TileSpacing.Standard, s.Spacing);
        Assert.Equal(ContentAlignment.Center, s.Alignment);
    }
}

public class BatteryColorsTests
{
    private static readonly Rgb Dark = new(32, 32, 32), Light = new(243, 243, 243);

    [Theory]
    [InlineData(100, BatteryBand.High)]
    [InlineData(60, BatteryBand.High)]
    [InlineData(59, BatteryBand.Medium)]
    [InlineData(30, BatteryBand.Medium)]
    [InlineData(29, BatteryBand.Warning)]
    [InlineData(21, BatteryBand.Warning)]
    [InlineData(20, BatteryBand.Low)]
    [InlineData(0, BatteryBand.Low)]
    public void BandFor_UsesTheFixedBandsAndTheLowThreshold(double percent, BatteryBand band) =>
        Assert.Equal(band, BatteryColors.BandFor(percent, 20));

    [Fact]
    public void BandFor_NoReadingIsUnknown() => Assert.Equal(BatteryBand.Unknown, BatteryColors.BandFor(null, 20));

    [Fact]
    public void BandFor_ALowThresholdAboveThirtyWins()
    {
        Assert.Equal(BatteryBand.Low, BatteryColors.BandFor(45, 50));
        Assert.Equal(BatteryBand.Medium, BatteryColors.BandFor(45, 20));
    }

    [Fact]
    public void Classic_UsesTheReferenceColoursPerTheme()
    {
        var a = new AppearanceSettings();
        Assert.Equal(new Rgb(110, 220, 120), BatteryColors.For(a, BatteryBand.High, dark: true, Dark));
        Assert.Equal(new Rgb(17, 97, 40), BatteryColors.For(a, BatteryBand.High, dark: false, Light));
        Assert.Equal(new Rgb(255, 153, 153), BatteryColors.For(a, BatteryBand.Low, dark: true, Dark));
    }

    [Fact]
    public void Mono_KeepsLowRedAndTheRestNeutral()
    {
        var a = new AppearanceSettings { Palette = BatteryPalette.Mono };
        Assert.Equal(BatteryColors.For(a, BatteryBand.High, true, Dark), BatteryColors.For(a, BatteryBand.Warning, true, Dark));
        Assert.NotEqual(BatteryColors.For(a, BatteryBand.High, true, Dark), BatteryColors.For(a, BatteryBand.Low, true, Dark));
    }

    [Theory]
    [InlineData(BatteryPalette.Classic)]
    [InlineData(BatteryPalette.ColourBlind)]
    [InlineData(BatteryPalette.Pastel)]
    [InlineData(BatteryPalette.HighContrast)]
    [InlineData(BatteryPalette.Mono)]
    public void EveryBuiltInColourIsReadableOnItsTheme(BatteryPalette palette)
    {
        var a = new AppearanceSettings { Palette = palette };
        foreach (var band in Enum.GetValues<BatteryBand>())
        {
            Assert.True(Contrast.Ratio(BatteryColors.For(a, band, true, Dark), Dark) >= 4.5, $"{palette} {band} dark");
            Assert.True(Contrast.Ratio(BatteryColors.For(a, band, false, Light), Light) >= 4.5, $"{palette} {band} light");
        }
    }

    [Fact]
    public void Custom_IsNudgedUntilReadable()
    {
        var a = new AppearanceSettings { Palette = BatteryPalette.Custom, CustomHigh = "#202020" }; // nearly the dark bar colour
        var c = BatteryColors.For(a, BatteryBand.High, dark: true, Dark);
        Assert.True(Contrast.Ratio(c, Dark) >= 4.5);
        Assert.NotEqual(new Rgb(32, 32, 32), c);
    }

    [Fact]
    public void Custom_WarningSitsHalfwayBetweenMediumAndLow()
    {
        var a = new AppearanceSettings { Palette = BatteryPalette.Custom, CustomMedium = "#F0F000", CustomLow = "#F00000" };
        Assert.Equal(new Rgb(240, 120, 0), BatteryColors.For(a, BatteryBand.Warning, true, Dark));
    }

    [Fact]
    public void Contrast_BlackOnWhiteIsTwentyOne() =>
        Assert.Equal(21, Contrast.Ratio(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 3);

    [Fact]
    public void Contrast_EnsureLeavesAReadableColourAlone()
    {
        var c = new Rgb(110, 220, 120);
        Assert.Equal(c, Contrast.Ensure(c, Dark, 4.5));
    }

    [Fact]
    public void Hex_ParsesAndFormats()
    {
        Assert.True(Hex.TryParse("#6EDC78", out var c));
        Assert.Equal(new Rgb(0x6E, 0xDC, 0x78), c);
        Assert.Equal("#6EDC78", c.ToHex());
        Assert.False(Hex.TryParse("6EDC78", out _));
        Assert.False(Hex.TryParse("#GGGGGG", out _));
        Assert.False(Hex.TryParse(null, out _));
    }
}

public class LookMetricsTests
{
    [Fact]
    public void Transparent_HasDividersAndNoBackground()
    {
        var m = LookMetrics.For(PillStyle.Transparent, 100, 32, 44);
        Assert.True(m.Dividers);
        Assert.False(m.Background);
        Assert.Equal(6, m.PadX);
    }

    [Fact]
    public void Classic_HasABackgroundAndNoDividers()
    {
        var m = LookMetrics.For(PillStyle.Classic, 100, 32, 44);
        Assert.True(m.Background);
        Assert.False(m.Dividers);
        Assert.Equal(7, m.Radius);
        Assert.Equal(4, LookMetrics.For(PillStyle.Soft, 100, 32, 44).Radius);
    }

    [Fact]
    public void Compact_IsTighterThanTransparent()
    {
        var c = LookMetrics.For(PillStyle.Compact, 100, 32, 44);
        var t = LookMetrics.For(PillStyle.Transparent, 100, 32, 44);
        Assert.True(c.PadX < t.PadX);
        Assert.True(c.Gap < t.Gap);
    }

    [Fact]
    public void Scale_GrowsTheHeightButNeverPastTheTaskbar()
    {
        Assert.Equal(32 * 1.15, LookMetrics.For(PillStyle.Classic, 115, 32, 60).Height, 6);
        Assert.Equal(40, LookMetrics.For(PillStyle.Classic, 130, 32, 40).Height); // capped at what the taskbar allows
    }

    [Fact]
    public void IconAndFontFollowTheHeightWithAFloor()
    {
        var m = LookMetrics.For(PillStyle.Classic, 100, 32, 44);
        Assert.Equal(Math.Round(32 * 0.58), m.IconSize);
        Assert.Equal(Math.Round(32 * 0.45), m.FontSize);
        var small = LookMetrics.For(PillStyle.Classic, 90, 20, 44);
        Assert.Equal(12, small.IconSize);
        Assert.Equal(12, small.FontSize);
    }

    [Fact]
    public void ClassicTintIsStrongerThanSoft()
    {
        Assert.True(PillTint.Neutral(PillStyle.Classic, true) > PillTint.Neutral(PillStyle.Soft, true));
        Assert.Equal(0, PillTint.Neutral(PillStyle.Transparent, true));
    }
}

public class BatteryWidgetSettingsTests
{
    [Fact]
    public void FromJson_EmptyDamagedOrNullMeansDefaults()
    {
        foreach (var json in new[] { null, "", "   ", "{", "not json", "[]" })
        {
            var s = BatteryWidgetSettings.FromJson(json);
            Assert.Empty(s.Hidden);
            Assert.Equal(20, s.LowThreshold);
            Assert.Equal(IconStyle.Outline, s.Appearance.Icons);
        }
    }

    [Fact]
    public void RoundTrip_KeepsEverythingAndStoresEnumsByName()
    {
        var s = new BatteryWidgetSettings { Order = ["b", "a"], Hidden = ["c"], LowThreshold = 30 };
        s.Appearance.Icons = IconStyle.Filled;
        s.Appearance.Palette = BatteryPalette.Pastel;
        s.Appearance.Scale = 115;
        s.Appearance.ShowNames = false;

        var json = s.ToJson();
        Assert.Contains("\"Filled\"", json);

        var back = BatteryWidgetSettings.FromJson(json);
        Assert.Equal(["b", "a"], back.Order);
        Assert.Equal(["c"], back.Hidden);
        Assert.Equal(30, back.LowThreshold);
        Assert.Equal(IconStyle.Filled, back.Appearance.Icons);
        Assert.Equal(BatteryPalette.Pastel, back.Appearance.Palette);
        Assert.Equal(115, back.Appearance.Scale);
        Assert.False(back.Appearance.ShowNames);
    }

    [Fact]
    public void FromJson_ForcesValuesIntoRangeAndDropsBlankIds()
    {
        var s = BatteryWidgetSettings.FromJson("""{ "Order": ["a", "", "a", " "], "Hidden": ["x", "x"], "LowThreshold": 99, "Appearance": { "Scale": 97 } }""");
        Assert.Equal(["a"], s.Order);
        Assert.Equal(["x"], s.Hidden);
        Assert.Equal(50, s.LowThreshold);
        Assert.Equal(100, s.Appearance.Scale);
        Assert.Equal(5, BatteryWidgetSettings.Sanitize(new BatteryWidgetSettings { LowThreshold = -3 }).LowThreshold);
    }

    [Fact]
    public void Layout_RoundTripsThroughTileLayoutAndKeepsTheRest()
    {
        var s = new BatteryWidgetSettings { Order = ["a"], Hidden = ["b"], LowThreshold = 35 };
        var moved = TileLayout.Move(s.AsLayout(), ["a", "b", "c"], "c", -1);
        var next = s.WithLayout(moved);

        Assert.Equal(35, next.LowThreshold);
        Assert.Equal(["b"], next.Hidden);
        Assert.Equal(moved.Order, next.Order);
        Assert.Equal(["a"], s.Order); // the original is untouched
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var s = new BatteryWidgetSettings();
        var c = s.Clone();
        c.Hidden.Add("x");
        c.Appearance.Icons = IconStyle.None;
        Assert.Empty(s.Hidden);
        Assert.Equal(IconStyle.Outline, s.Appearance.Icons);
    }

    [Fact]
    public void OldJsonWithoutTheNewFieldsLoadsWithDefaults()
    {
        var s = BatteryWidgetSettings.FromJson("""{ "Order": ["a"], "Hidden": [], "LowThreshold": 25, "Appearance": { "Icons": "Filled", "Scale": 115 } }""");
        Assert.Equal(25, s.LowThreshold);
        Assert.Equal(IconStyle.Filled, s.Appearance.Icons);
        Assert.Equal(PreviewLayout.VerticalMeters, s.Appearance.Layout);
        Assert.Empty(s.Labels);
        Assert.Empty(s.Colours);
        Assert.Equal(1, s.RefreshMinutes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 10)]
    [InlineData(5, 5)]
    public void Sanitize_ClampsTheRefreshMinutes(int input, int expected) =>
        Assert.Equal(expected, BatteryWidgetSettings.Sanitize(new BatteryWidgetSettings { RefreshMinutes = input }).RefreshMinutes);

    [Fact]
    public void Sanitize_CleansTheDeviceLabelsAndColours()
    {
        var s = BatteryWidgetSettings.Sanitize(new BatteryWidgetSettings
        {
            Labels = new() { ["a"] = new string('x', 40), ["b"] = "   ", [""] = "no id", ["c"] = "  Mouse " },
            Colours = new() { ["a"] = "#abcdef", ["b"] = "red", ["c"] = "#12345", [" "] = "#FFFFFF" },
        });
        Assert.Equal(24, s.Labels["a"].Length);
        Assert.False(s.Labels.ContainsKey("b"));
        Assert.False(s.Labels.ContainsKey(""));
        Assert.Equal("Mouse", s.Labels["c"]);
        Assert.Equal("#ABCDEF", s.Colours["a"]);
        Assert.False(s.Colours.ContainsKey("b"));
        Assert.False(s.Colours.ContainsKey("c"));
        Assert.False(s.Colours.ContainsKey(" "));
    }

    [Theory]
    [InlineData("""{ "Order": null }""")]
    [InlineData("""{ "Hidden": null }""")]
    [InlineData("""{ "Appearance": null }""")]
    [InlineData("""{ "Order": null, "Hidden": null, "Appearance": null, "Labels": null, "Colours": null }""")]
    public void FromJson_NullListsAndAppearanceMeanDefaults(string json)
    {
        var s = BatteryWidgetSettings.FromJson(json);
        Assert.Empty(s.Order);
        Assert.Empty(s.Hidden);
        Assert.Equal(PreviewLayout.VerticalMeters, s.Appearance.Layout);
        Assert.Equal(20, s.LowThreshold);
    }

    [Fact]
    public void Sanitize_KeepsALabelWhole_WhenAnEmojiStraddlesTheLimit()
    {
        var label = new string('x', BatteryWidgetSettings.MaxLabelLength - 1) + "\U0001F3A7"; // the emoji is two UTF-16 units
        var s = BatteryWidgetSettings.Sanitize(new BatteryWidgetSettings { Labels = new() { ["a"] = label } });
        Assert.False(char.IsHighSurrogate(s.Labels["a"][^1]));
        Assert.True(s.Labels["a"].Length <= BatteryWidgetSettings.MaxLabelLength);
    }

    [Fact]
    public void FromJson_NullDeviceOverridesMeanNone()
    {
        var s = BatteryWidgetSettings.FromJson("""{ "Labels": null, "Colours": null }""");
        Assert.Empty(s.Labels);
        Assert.Empty(s.Colours);
    }

    [Fact]
    public void RoundTrip_KeepsTheNewFieldsAndTheDeviceOverrides()
    {
        var s = new BatteryWidgetSettings { RefreshMinutes = 4, Labels = new() { ["a"] = "Headset" }, Colours = new() { ["a"] = "#112233" } };
        s.Appearance.Layout = PreviewLayout.IconMatrix;
        s.Appearance.Spacing = TileSpacing.Relaxed;
        s.Appearance.Alignment = ContentAlignment.Right;
        s.Appearance.WidthAdjustment = -40;
        s.Appearance.ColouredValues = true;
        s.Appearance.UseAccentColour = true;

        var json = s.ToJson();
        Assert.Contains("\"Relaxed\"", json);
        var back = BatteryWidgetSettings.FromJson(json);
        Assert.Equal(4, back.RefreshMinutes);
        Assert.Equal("Headset", back.Labels["a"]);
        Assert.Equal("#112233", back.Colours["a"]);
        Assert.Equal(PreviewLayout.IconMatrix, back.Appearance.Layout);
        Assert.Equal(TileSpacing.Relaxed, back.Appearance.Spacing);
        Assert.Equal(ContentAlignment.Right, back.Appearance.Alignment);
        Assert.Equal(-40, back.Appearance.WidthAdjustment);
        Assert.True(back.Appearance.ColouredValues);
        Assert.True(back.Appearance.UseAccentColour);
    }

    [Fact]
    public void Clone_DoesNotShareTheDeviceOverrides()
    {
        var s = new BatteryWidgetSettings { Labels = new() { ["a"] = "One" }, Colours = new() { ["a"] = "#112233" } };
        var c = s.Clone();
        c.Labels["a"] = "Two";
        c.Colours.Remove("a");
        Assert.Equal("One", s.Labels["a"]);
        Assert.Equal("#112233", s.Colours["a"]);
    }

    [Fact]
    public void LabelFor_UsesTheOverrideOrTheFallback()
    {
        var s = new BatteryWidgetSettings { Labels = new() { ["a"] = "Headset" } };
        Assert.Equal("Headset", s.LabelFor("a", "Arctis Nova 7"));
        Assert.Equal("Rival 3 Wireless", s.LabelFor("b", "Rival 3 Wireless"));
    }

    [Fact]
    public void ColourFor_UsesTheOverrideOrNull()
    {
        var s = new BatteryWidgetSettings { Colours = new() { ["a"] = "#6EDC78" } };
        Assert.Equal(new Rgb(0x6E, 0xDC, 0x78), s.ColourFor("a"));
        Assert.Null(s.ColourFor("b"));
    }
}
