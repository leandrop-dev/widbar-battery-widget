using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Appearance;

namespace Widgets.WidBar.Layouts;

/// <summary>Radial gauges: each device is a ring that fills clockwise from 12 o'clock, with its label above its value inside.</summary>
internal sealed class RadialGaugesLayout : ITileLayout
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
        foreach (var m in shown) row.Children.Add(Gauge(m, c, k));
        return row;
    }

    private static FrameworkElement Gauge(Metric m, TileContext c, double k)
    {
        var ink = TileParts.InkFor(m, c);
        var size = Math.Round(NaturalHeight * k);
        var stroke = Math.Max(2.5, Math.Round(3 * k, 1));
        var radius = (size - stroke) / 2;
        var centre = size / 2;

        var ring = new Grid { Width = size, Height = size };
        ring.Children.Add(new Ellipse
        {
            Width = size - stroke, Height = size - stroke,
            Stroke = TileParts.Brush(ink.Accent, 0.25), StrokeThickness = stroke,
        });

        var fraction = Math.Clamp(m.Value ?? 0, 0, 100) / 100.0;
        if (m.Value is not null && fraction >= 0.999)
        {
            // An arc cannot end exactly where it started, so a full level is a complete circle instead.
            ring.Children.Add(new Ellipse
            {
                Width = size - stroke, Height = size - stroke,
                Stroke = TileParts.Brush(ink.Accent), StrokeThickness = stroke,
            });
        }
        else if (m.Value is not null && fraction > 0)
        {
            var end = Arc.PointAt(centre, centre, radius, fraction);
            var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(centre, centre - radius), IsClosed = false };
            figure.Segments.Add(new ArcSegment
            {
                Point = new Windows.Foundation.Point(end.X, end.Y),
                Size = new Windows.Foundation.Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = Arc.IsLargeArc(fraction),
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            ring.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Stroke = TileParts.Brush(ink.Accent),
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }

        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (c.Look.ShowNames)
        {
            var label = TileParts.Text(TileParts.LabelOf(m, c), Math.Round(7 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Label));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.MaxWidth = size - 2 * stroke;
            text.Children.Add(label);
        }

        var value = TileParts.Text(TileParts.ValueOf(m, c), Math.Round(10 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Value));
        value.HorizontalAlignment = HorizontalAlignment.Center;
        text.Children.Add(value);
        ring.Children.Add(text);

        // The charging bolt sits at the ring's lower right, where it does not cover the figures.
        if (TileParts.Bolt(m, c, Math.Round(9 * k)) is FrameworkElement bolt)
        {
            bolt.HorizontalAlignment = HorizontalAlignment.Right;
            bolt.VerticalAlignment = VerticalAlignment.Bottom;
            bolt.Margin = new Thickness(0, 0, -1, -1);
            ring.Children.Add(bolt);
        }

        return TileParts.Finish(ring, m, c);
    }
}
