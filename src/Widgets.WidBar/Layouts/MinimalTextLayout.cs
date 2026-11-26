using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.WidBar.Layouts;

/// <summary>Minimal text: a row of devices, each a small label beside its value, with optional thin separators.</summary>
internal sealed class MinimalTextLayout : ITileLayout
{
    private const double NaturalHeight = 20;

    public FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext c)
    {
        var k = TileParts.Fit(NaturalHeight, c);
        var gap = TileParts.Gap(c, 8, 14, 22) * k;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        for (var i = 0; i < shown.Count; i++)
        {
            var tile = Tile(shown[i], c, k);
            if (i > 0)
            {
                if (c.Look.ShowSeparators)
                {
                    var divider = TileParts.Divider(c, 14 * k);
                    ((FrameworkElement)divider).Margin = new Thickness(gap / 2, 0, 0, 0);
                    row.Children.Add(divider);
                    tile.Margin = new Thickness(gap / 2, 0, 0, 0);
                }
                else tile.Margin = new Thickness(gap, 0, 0, 0);
            }
            row.Children.Add(tile);
        }
        return row;
    }

    private static FrameworkElement Tile(Metric m, TileContext c, double k)
    {
        var ink = TileParts.InkFor(m, c);
        var tile = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (c.Look.ShowNames)
        {
            var label = TileParts.Text(TileParts.LabelOf(m, c), Math.Round(9 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Label));
            label.VerticalAlignment = VerticalAlignment.Top; // raised beside the value, as in System Metrics
            label.Margin = new Thickness(0, 2 * k, 3 * k, 0);
            tile.Children.Add(label);
        }

        if (TileParts.LowMark(m, c, Math.Round(14 * k, 1), ink.Accent) is { } low) tile.Children.Add(low);
        tile.Children.Add(TileParts.ValueBlock(m, c, Math.Round(14 * k, 1), FontWeights.SemiBold, TileParts.Brush(ink.Value)));
        if (TileParts.Bolt(m, c, Math.Round(12 * k)) is { } bolt) tile.Children.Add(bolt);
        return TileParts.Finish(tile, m, c);
    }
}
