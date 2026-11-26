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
}

/// <summary>
/// Which layout uses which option. The layouts read it to know what to ignore and the settings page reads it to grey out
/// the rows that would change nothing, so there is one table and no option quietly does nothing.
/// </summary>
public static class LayoutSupport
{
    public static bool Honours(PreviewLayout layout, PreviewOption option) => option switch
    {
        PreviewOption.Icons => layout == PreviewLayout.IconMatrix,         // the other layouts draw no device icon
        PreviewOption.Separators => layout == PreviewLayout.MinimalText,   // the others are tiles or a grid with their own gaps
        _ => true,
    };
}
