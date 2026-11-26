using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class DeviceColoursTests
{
    private static readonly Rgb DarkBar = new(32, 32, 32);
    private static readonly Rgb Neutral = new(255, 255, 255);
    private static readonly Rgb LightBlue = new(110, 220, 255); // readable on the dark bar, so never nudged

    private static DeviceInk Resolve(BatteryWidgetSettings s, double? percent = 75, string id = "a", Rgb? windowsAccent = null) =>
        DeviceColours.Resolve(s, id, percent, dark: true, DarkBar, Neutral, windowsAccent);

    private static Rgb Palette(BatteryWidgetSettings s, double percent) =>
        BatteryColors.For(s.Appearance, BatteryColors.BandFor(percent, s.LowThreshold), dark: true, DarkBar);

    [Fact]
    public void ThePaletteColoursADeviceWithAReading()
    {
        var s = new BatteryWidgetSettings();
        Assert.Equal(Palette(s, 75), Resolve(s, 75).Accent);
    }

    [Fact]
    public void NoReadingIsNeutral()
    {
        var ink = Resolve(new BatteryWidgetSettings(), percent: null);
        Assert.Equal(Neutral, ink.Accent);
        Assert.Equal(Neutral, ink.Label);
        Assert.Equal(Neutral, ink.Icon);
    }

    [Fact]
    public void TheWindowsAccentBeatsThePalette()
    {
        var s = new BatteryWidgetSettings();
        s.Appearance.UseAccentColour = true;
        Assert.Equal(LightBlue, Resolve(s, 75, windowsAccent: LightBlue).Accent);
    }

    [Fact]
    public void TheWindowsAccentOptionWithoutAnAccentUsesThePalette()
    {
        var s = new BatteryWidgetSettings();
        s.Appearance.UseAccentColour = true;
        Assert.Equal(Palette(s, 75), Resolve(s, 75, windowsAccent: null).Accent);
    }

    [Fact]
    public void TheAccentIsIgnoredWhileTheOptionIsOff()
    {
        var s = new BatteryWidgetSettings();
        Assert.Equal(Palette(s, 75), Resolve(s, 75, windowsAccent: LightBlue).Accent);
    }

    [Fact]
    public void ADevicesOwnColourBeatsTheAccentAndThePalette()
    {
        var s = new BatteryWidgetSettings { Colours = new() { ["a"] = "#FFC864" } };
        s.Appearance.UseAccentColour = true;
        Assert.Equal(new Rgb(0xFF, 0xC8, 0x64), Resolve(s, 75, id: "a", windowsAccent: LightBlue).Accent);
        Assert.Equal(LightBlue, Resolve(s, 75, id: "b", windowsAccent: LightBlue).Accent); // another device has no override
    }

    [Fact]
    public void ADevicesOwnColourStillAppliesWithoutAReading()
    {
        var s = new BatteryWidgetSettings { Colours = new() { ["a"] = "#FFC864" } };
        Assert.Equal(new Rgb(0xFF, 0xC8, 0x64), Resolve(s, percent: null, id: "a").Accent);
    }

    [Fact]
    public void LabelAndIconFollowTheDeviceColourByDefaultAndTheValueDoesNot()
    {
        var s = new BatteryWidgetSettings();
        var ink = Resolve(s, 75);
        Assert.Equal(ink.Accent, ink.Label);
        Assert.Equal(ink.Accent, ink.Icon);
        Assert.Equal(Neutral, ink.Value);
    }

    [Fact]
    public void TurningColouredLabelsAndIconsOffMakesThemNeutral()
    {
        var s = new BatteryWidgetSettings();
        s.Appearance.ColouredLabelsAndIcons = false;
        var ink = Resolve(s, 75);
        Assert.Equal(Neutral, ink.Label);
        Assert.Equal(Neutral, ink.Icon);
        Assert.Equal(Palette(s, 75), ink.Accent); // the colour itself is still known, for the meter or the ring
    }

    [Fact]
    public void ColouredValuesMakesTheValueTheDeviceColour()
    {
        var s = new BatteryWidgetSettings();
        s.Appearance.ColouredValues = true;
        var ink = Resolve(s, 75);
        Assert.Equal(ink.Accent, ink.Value);
    }

    [Fact]
    public void AColourTooCloseToTheBarIsNudgedUntilReadable()
    {
        var s = new BatteryWidgetSettings { Colours = new() { ["a"] = "#202020" } }; // exactly the bar colour
        var accent = Resolve(s, 75, id: "a").Accent;
        Assert.NotEqual(DarkBar, accent);
        Assert.True(Contrast.Ratio(accent, DarkBar) >= 4.5);
    }

    [Fact]
    public void ALowBatteryGetsTheLowColour()
    {
        var s = new BatteryWidgetSettings { LowThreshold = 20 };
        Assert.Equal(Palette(s, 10), Resolve(s, 10).Accent);
        Assert.NotEqual(Resolve(s, 10).Accent, Resolve(s, 90).Accent);
    }
}
