using Widgets.Core.Settings;

namespace Widgets.Core.Appearance;

/// <summary>The colours one device is drawn in. <see cref="Accent"/> is the device's own colour, for a meter, capsule or ring.</summary>
public readonly record struct DeviceInk(Rgb Label, Rgb Icon, Rgb Value, Rgb Accent);

/// <summary>
/// The one rule for which colour a device gets, used by the taskbar layouts, the settings preview and the flyout so they
/// cannot disagree. The first of these wins: the colour the user gave this device, the Windows accent colour (when that
/// option is on), the battery palette for its level, and the neutral text colour when there is no reading.
/// </summary>
public static class DeviceColours
{
    /// <summary>Contrast every chosen colour keeps against the bar, as for the Custom palette.</summary>
    private const double MinContrast = 4.5;

    public static DeviceInk Resolve(
        BatteryWidgetSettings settings, string deviceId, double? percent, bool dark, Rgb bar, Rgb neutral, Rgb? windowsAccent)
    {
        var look = settings.Appearance;

        Rgb accent;
        if (settings.ColourFor(deviceId) is { } own) accent = own;
        else if (look.UseAccentColour && windowsAccent is { } system) accent = system;
        else if (percent is not null)
            accent = BatteryColors.For(look, BatteryColors.BandFor(percent, settings.LowThreshold), dark, bar);
        else accent = neutral;

        // A colour the user typed, or the system accent, may sit too close to the bar to read: nudge it, as for Custom.
        accent = Contrast.Ensure(accent, bar, MinContrast);

        var decoration = look.ColouredLabelsAndIcons ? accent : neutral;
        var value = look.ColouredValues ? accent : neutral;
        return new DeviceInk(Label: decoration, Icon: decoration, Value: value, Accent: accent);
    }
}
