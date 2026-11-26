using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar.Layouts;

/// <summary>The pieces every layout shares, so they all treat a setting the same way.</summary>
internal static class TileParts
{
    public static DeviceInk InkFor(Metric m, TileContext c) =>
        DeviceColours.Resolve(c.Settings, m.Id, m.Value, c.Dark, c.Ink.Bar, c.Ink.NeutralRgb, c.WindowsAccent);

    /// <summary>The user's name for the device, or its own name.</summary>
    public static string LabelOf(Metric m, TileContext c) => c.Settings.LabelFor(m.Id, m.Label);

    /// <summary>The level as text, with or without the "%".</summary>
    public static string ValueOf(Metric m, TileContext c) => MetricFormat.Value(m, c.Look.ShowPercentSymbol);

    public static Brush Brush(Rgb colour, double opacity = 1) => Ink.Solid(colour, opacity);

    /// <summary>
    /// The factor a layout draws at: the user's value size, but never so large that a layout whose natural height is
    /// <paramref name="naturalHeight"/> would not fit the slot (two-row layouts in the small taskbar, for example).
    /// </summary>
    public static double Fit(double naturalHeight, TileContext c) =>
        Math.Min(c.Scale, Math.Max(0.5, (c.SlotHeight - 2) / naturalHeight));

    /// <summary>The space between tiles for the chosen "Spacing".</summary>
    public static double Gap(TileContext c, double compact, double standard, double relaxed) => c.Look.Spacing switch
    {
        TileSpacing.Compact => compact,
        TileSpacing.Relaxed => relaxed,
        _ => standard,
    };

    public static TextBlock Text(string text, double size, Windows.UI.Text.FontWeight weight, Brush brush) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        IsTextScaleFactorEnabled = false, // the taskbar's size, not the text-size setting, decides how big this is
    };

    /// <summary>
    /// The level as a text block that always has the width of a full "100%", so the tile does not change width as
    /// 9% becomes 10% or 100%.
    /// </summary>
    public static TextBlock ValueBlock(Metric m, TileContext c, double size, Windows.UI.Text.FontWeight weight, Brush brush)
    {
        var block = Text(ValueOf(m, c), size, weight, brush);
        block.VerticalAlignment = VerticalAlignment.Center;
        if (string.IsNullOrEmpty(m.Text))
        {
            var widest = Text(MetricFormat.Value(m with { Value = 100 }, c.Look.ShowPercentSymbol), size, weight, brush);
            widest.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            block.MinWidth = Math.Ceiling(widest.DesiredSize.Width);
            block.TextAlignment = TextAlignment.Right;
        }
        return block;
    }

    /// <summary>The charging bolt, or null when the device is not charging or "Activity indicators" is off.</summary>
    public static UIElement? Bolt(Metric m, TileContext c, double size)
    {
        if (!m.Charging || !c.Look.ShowChargingBolt) return null;
        return new FontIcon
        {
            Glyph = ((char)0xE945).ToString(), // Segoe Fluent lightning bolt: the standard Windows charging mark
            FontSize = Math.Max(10, size),
            Margin = new Thickness(-1, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = c.Ink.Success,
        };
    }

    /// <summary>A bold "!" before the value of a low device, so the warning does not depend on colour (the device may have its own colour or the accent).</summary>
    public static UIElement? LowMark(Metric m, TileContext c, double size, Rgb colour)
    {
        if (!DeviceStatus.IsLow(m, c.Settings.LowThreshold)) return null;
        return new TextBlock
        {
            Text = "!",
            FontSize = size,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush(colour),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            IsTextScaleFactorEnabled = false,
        };
    }

    /// <summary>A thin vertical line between two tiles.</summary>
    public static UIElement Divider(TileContext c, double height) => new Rectangle
    {
        Width = 1,
        Height = Math.Round(height),
        Fill = Ink.Solid(Ink.Neutral(c.Dark), 0.3),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Dims a device that has gone quiet and gives screen readers its name, level and state.</summary>
    public static FrameworkElement Finish(FrameworkElement tile, Metric m, TileContext c)
    {
        tile.Opacity = m.Stale ? 0.5 : 1.0;
        var spoken = $"{LabelOf(m, c)} {ValueOf(m, c)}";
        var status = DeviceStatus.Describe(m, DateTimeOffset.Now, c.Settings.LowThreshold);
        AutomationProperties.SetName(tile, string.IsNullOrEmpty(status) ? spoken : $"{spoken}, {status}");
        return tile;
    }

    /// <summary>What the preview shows when there is no device to show.</summary>
    public static FrameworkElement NoBatteries(TileContext c) => new TextBlock
    {
        Text = "No batteries",
        FontSize = Math.Round(13 * c.Scale),
        Foreground = c.Ink.Secondary,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
