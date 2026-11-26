using Widgets.Core.Settings;

namespace Widgets.Core.Appearance;

/// <summary>The options of the preview that only some layouts can use.</summary>
public enum PreviewOption
{
    Icons,
    MetricLabels,
    PercentSymbol,
    Separators,
    ChargingBolt,
    ColouredLabelsAndIcons,
    ColouredValues,
    Spacing,
    Alignment,
    ValueSize,
    BatteryColours,
}

/// <summary>
/// Which layout uses which option. The layouts read it to know what to ignore and the settings page reads it to grey out
/// the rows that would change nothing, so there is one table and no option quietly does nothing.
/// </summary>
public static class LayoutSupport
{
    public static bool Honours(PreviewLayout layout, PreviewOption option) => option switch
    {
        PreviewOption.Icons => layout == PreviewLayout.MinimalIcons,         // the other layouts draw no device icon
        PreviewOption.Separators => layout == PreviewLayout.MinimalText,   // the other layouts are tiles with their own gap
        _ => true,
    };

    /// <summary>
    /// Why <paramref name="option"/> would change nothing with these settings, or null when it is used. The layout is
    /// checked first, then the settings that make the option pointless: alignment with no spare width, a colour for a
    /// label that is not drawn, and the battery palette while one accent colour overrides it.
    /// </summary>
    public static string? Unused(AppearanceSettings look, PreviewOption option)
    {
        if (!Honours(look.Layout, option)) return "Not used by this layout";
        return option switch
        {
            PreviewOption.Alignment when look.WidthAdjustment == 0 => "Used when the width adjustment adds room",
            PreviewOption.ColouredLabelsAndIcons when !look.ShowNames && look.Layout != PreviewLayout.MinimalIcons => "Needs metric labels or icons",
            PreviewOption.BatteryColours when look.UseAccentColour => "Not used while Windows accent color is on",
            _ => null,
        };
    }
}
