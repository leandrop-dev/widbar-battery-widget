using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;

namespace Widgets.WidBar.Layouts;

/// <summary>Vertical meters: each device is a thin bar that fills from the bottom, with its label above its value.</summary>
internal sealed class VerticalMetersLayout : ITileLayout
{
    private const double NaturalHeight = 34;

    public FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext c)
    {
        var k = TileParts.Fit(NaturalHeight, c);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = TileParts.Gap(c, 6, 10, 16) * k,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var m in shown) row.Children.Add(Tile(m, c, k));
        return row;
    }

    private static FrameworkElement Tile(Metric m, TileContext c, double k)
    {
        var ink = TileParts.InkFor(m, c);
        var height = Math.Round(NaturalHeight * k);

        // The bar: a faint track with the level drawn from the bottom up.
        var barWidth = Math.Max(3, Math.Round(6 * k));
        var level = Math.Clamp(m.Value ?? 0, 0, 100) / 100.0;
        var track = new Grid { Width = barWidth, Height = height, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Rectangle
        {
            Fill = TileParts.Brush(ink.Accent, 0.25),
            RadiusX = barWidth / 2, RadiusY = barWidth / 2,
        });
        if (m.Value is not null && level > 0)
        {
            track.Children.Add(new Rectangle
            {
                Fill = TileParts.Brush(ink.Accent),
                Height = Math.Max(barWidth, Math.Round(height * level)), // never thinner than a dot, so a low level still shows
                VerticalAlignment = VerticalAlignment.Bottom,
                RadiusX = barWidth / 2, RadiusY = barWidth / 2,
            });
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (c.Look.ShowNames)
            text.Children.Add(TileParts.Text(TileParts.LabelOf(m, c), Math.Round(9 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Label)));

        var valueLine = new StackPanel { Orientation = Orientation.Horizontal };
        valueLine.Children.Add(TileParts.ValueBlock(m, c, Math.Round(14 * k, 1), FontWeights.SemiBold, TileParts.Brush(ink.Value)));
        if (TileParts.Bolt(m, c, Math.Round(12 * k)) is { } bolt) valueLine.Children.Add(bolt);
        text.Children.Add(valueLine);

        var tile = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 * k, VerticalAlignment = VerticalAlignment.Center };
        tile.Children.Add(track);
        tile.Children.Add(text);
        return TileParts.Finish(tile, m, c);
    }
}
