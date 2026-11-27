using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Appearance;
using Windows.Foundation;

namespace Widgets.WidBar;

/// <summary>Draws the last day of a battery as a thin line. The shape comes from <see cref="Sparkline"/>; this only scales it to the card.</summary>
internal static class SparklineView
{
    public const double Height = 32;
    private const double Inset = 2; // keeps the stroke inside the box at 0% and 100%

    public static FrameworkElement Build(IReadOnlyList<SparkSegment> segments, Rgb colour)
    {
        var canvas = new Canvas { Height = Height, Margin = new Thickness(0, 4, 0, 0) };

        // Charging stretches are fainter, so a climb reads as a charge and not as a fault.
        var lines = new List<(Polyline Line, SparkSegment Segment)>();
        foreach (var segment in segments.Where(s => s.Points.Count >= 2))
        {
            var line = new Polyline
            {
                Stroke = Ink.Solid(colour, segment.Charging ? 0.4 : 1),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            canvas.Children.Add(line);
            lines.Add((line, segment));
        }

        void Layout()
        {
            var width = canvas.ActualWidth;
            if (width <= 0) return;
            foreach (var (line, segment) in lines)
            {
                var points = new PointCollection();
                foreach (var p in segment.Points)
                    points.Add(new Point(p.X * (width - 2 * Inset) + Inset, p.Y * (Height - 2 * Inset) + Inset));
                line.Points = points;
            }
        }
        canvas.SizeChanged += (_, _) => Layout();
        return canvas;
    }
}
