using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.WidBar.Layouts;

/// <summary>Minimal icons: the Minimal text row with each device's icon in front of its value; the label is optional.</summary>
internal sealed class MinimalIconsLayout : ITileLayout
{
    private const double NaturalHeight = 20;

    public FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext c)
    {
        var k = TileParts.Fit(NaturalHeight, c);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = TileParts.Gap(c, 8, 14, 22) * k,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var m in shown) row.Children.Add(Tile(m, c, k));
        return row;
    }

    private static FrameworkElement Tile(Metric m, TileContext c, double k)
    {
        var ink = TileParts.InkFor(m, c);
        var look = c.Look;
        var tile = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var iconBrush = TileParts.Brush(ink.Icon);
        var size = Math.Round(14 * k);
        // The battery gauge needs a level; a device without one gets the outline icon instead.
        UIElement? icon = look.Icons == IconStyle.Battery && m.Value is not null
            ? TileIcons.Gauge(m.Value, size, iconBrush, iconBrush)
            : TileIcons.Build(m.Icon, look.Icons == IconStyle.Battery ? IconStyle.Outline : look.Icons, size, iconBrush, TileParts.Brush(c.Ink.Bar));
        if (icon is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(0, 0, 4 * k, 0);
            tile.Children.Add(fe);
        }

        if (look.ShowNames)
        {
            var label = TileParts.Text(TileParts.LabelOf(m, c), Math.Round(9 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Label));
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 4 * k, 0);
            tile.Children.Add(label);
        }

        tile.Children.Add(TileParts.ValueBlock(m, c, Math.Round(14 * k, 1), FontWeights.SemiBold, TileParts.Brush(ink.Value)));
        if (TileParts.Bolt(m, c, Math.Round(12 * k)) is { } bolt) tile.Children.Add(bolt);
        return TileParts.Finish(tile, m, c);
    }
}
