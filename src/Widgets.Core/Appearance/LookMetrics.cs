using Widgets.Core.Settings;

namespace Widgets.Core.Appearance;

/// <summary>Sizes (in device-independent pixels) of one tile for a pill style and size setting.</summary>
public readonly record struct LookMetrics(double Height, double PadX, double Gap, double FontSize, double IconSize, double IconGap, double Radius, bool Background, bool Dividers)
{
    /// <summary>
    /// Pill height is the height the taskbar allows, scaled by the size setting and kept within <paramref name="maxHeight"/>.
    /// Padding and gaps follow the reference project's per-style values.
    /// </summary>
    public static LookMetrics For(PillStyle style, int scalePercent, double baseHeight, double maxHeight)
    {
        var k = scalePercent / 100.0;
        var height = Math.Max(16, Math.Min(baseHeight * k, maxHeight));
        var pad = style switch { PillStyle.Classic => 8, PillStyle.Soft => 8, PillStyle.Transparent => 6, _ => 3 };
        var gap = style switch { PillStyle.Compact => 2, _ => 4 };
        return new LookMetrics(
            Height: height,
            PadX: Math.Max(3, pad * k),
            Gap: Math.Max(1, gap * k),
            FontSize: Math.Max(12, Math.Round(height * 0.45)),
            IconSize: Math.Max(12, Math.Round(height * 0.58)),
            IconGap: Math.Max(3, 5 * k),
            Radius: style == PillStyle.Soft ? 4 : 7,
            Background: style is PillStyle.Classic or PillStyle.Soft,
            Dividers: style is PillStyle.Transparent or PillStyle.Compact);
    }
}

/// <summary>The tint drawn behind a pill (Classic and Soft) and its low-battery variant, as colour plus opacity.</summary>
public static class PillTint
{
    /// <summary>Opacity (0..1) of the neutral tint; white on dark themes, black on light ones.</summary>
    public static double Neutral(PillStyle style, bool dark) => style switch
    {
        PillStyle.Classic => dark ? 0.09 : 0.085,
        PillStyle.Soft => dark ? 0.05 : 0.047,
        _ => 0,
    };

    public static Rgb NeutralColor(bool dark) => dark ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
    public static Rgb LowColor(bool dark) => dark ? new Rgb(255, 105, 105) : new Rgb(176, 0, 0);
}
