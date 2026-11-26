using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>
/// The small vector icons in front of each value, drawn on a 24 x 24 grid and scaled. They are original drawings in
/// the three styles the reference project offers (outline, filled, simple), plus a battery level gauge.
/// </summary>
internal static class TileIcons
{
    /// <summary>Shape is a closed outline that the Filled style fills; Line stays a stroke; Detail is dropped by Simple and cut out of the fill by Filled.</summary>
    private enum Role { Shape, Line, Detail }

    private readonly record struct Part(Role Role, string? Path = null, double X = 0, double Y = 0, double W = 0, double H = 0, double Radius = 0, bool Ellipse = false);

    private static Part Rect(double x, double y, double w, double h, double r, Role role = Role.Shape) => new(role, null, x, y, w, h, r);
    private static Part Path_(string data, Role role) => new(role, data);
    private static Part Dot(double cx, double cy, double r) => new(Role.Detail, null, cx - r, cy - r, r * 2, r * 2, 0, true);

    private static readonly Dictionary<string, Part[]> Icons = new()
    {
        [MetricIcons.Headset] =
        [
            Rect(3, 12.5, 4.5, 7.5, 1.8), Rect(16.5, 12.5, 4.5, 7.5, 1.8),
            Path_("M4.5,13 L4.5,11.5 A7.5,7.5 0 0 1 19.5,11.5 L19.5,13", Role.Line),
            Path_("M19.5,20 C19.5,21.6 17.5,22.4 14.5,22.4", Role.Detail),
        ],
        [MetricIcons.Mouse] =
        [
            Rect(6, 2.5, 12, 19, 6),
            Path_("M12,2.8 L12,10 M6.2,10 L17.8,10", Role.Detail),
        ],
        [MetricIcons.Gamepad] =
        [
            Path_("M7,8 H17 A4,4 0 0 1 21,12 V13.5 A3,3 0 0 1 15.8,15.5 L14.5,14 H9.5 L8.2,15.5 A3,3 0 0 1 3,13.5 V12 A4,4 0 0 1 7,8 Z", Role.Shape),
            Path_("M8,10.3 L8,13.3 M6.5,11.8 L9.5,11.8", Role.Detail), Dot(16, 11, 0.7), Dot(18, 12.8, 0.7),
        ],
        [MetricIcons.Keyboard] =
        [
            Rect(2.5, 6, 19, 12, 2),
            Path_("M6,10 L6.01,10 M10,10 L10.01,10 M14,10 L14.01,10 M18,10 L18.01,10 M7,14 L17,14", Role.Detail),
        ],
        [MetricIcons.Generic] =
        [
            Rect(7, 2.5, 10, 19, 2.5),
            Path_("M10.5,18 L13.5,18", Role.Detail),
        ],
        [MetricIcons.Cpu] =
        [
            Rect(6, 6, 12, 12, 2),
            Path_("M9,3 L9,6 M15,3 L15,6 M9,18 L9,21 M15,18 L15,21 M3,9 L6,9 M3,15 L6,15 M18,9 L21,9 M18,15 L21,15", Role.Line),
            Rect(9.5, 9.5, 5, 5, 1, Role.Detail),
        ],
        [MetricIcons.Gpu] =
        [
            Rect(2.5, 6.5, 19, 10.5, 2),
            Path_("M5.5,17 L5.5,19.5", Role.Line),
            Dot(9, 11.7, 2.8), Path_("M15.5,9.7 L15.5,13.7", Role.Detail),
        ],
        [MetricIcons.Ram] =
        [
            Rect(3, 6.5, 18, 10.5, 1.6),
            Path_("M6,17 L6,19.5 M10,17 L10,19.5 M14,17 L14,19.5 M18,17 L18,19.5", Role.Line),
            Path_("M7,10 L7,13.2 M11,10 L11,13.2 M15,10 L15,13.2", Role.Detail),
        ],
        [MetricIcons.Thermometer] =
        [
            Path_("M10,14.5 V5 A2,2 0 0 1 14,5 V14.5 A4,4 0 1 1 10,14.5 Z", Role.Shape),
            Path_("M12,9 L12,16", Role.Detail),
        ],
    };

    /// <summary>The icon for <paramref name="kind"/> in <paramref name="style"/>, or null when icons are off or the kind is unknown.</summary>
    /// <param name="cut">The colour behind the icon, used for the cut-out details of the Filled style.</param>
    public static UIElement? Build(string? kind, IconStyle style, double size, Brush foreground, Brush cut)
    {
        if (style == IconStyle.None || kind is null) return null;
        if (!Icons.TryGetValue(kind, out var parts)) return null;

        var canvas = new Canvas { Width = 24, Height = 24 };
        var thickness = style == IconStyle.Simple ? 2.4 : 1.7;
        foreach (var part in parts)
        {
            if (style == IconStyle.Simple && part.Role == Role.Detail) continue;
            var filled = style == IconStyle.Filled;
            Shape shape = part.Path is not null
                ? new Microsoft.UI.Xaml.Shapes.Path { Data = (Microsoft.UI.Xaml.Media.Geometry)XamlBindingHelper.ConvertValue(typeof(Microsoft.UI.Xaml.Media.Geometry), part.Path) }
                : part.Ellipse ? new Ellipse { Width = part.W, Height = part.H } : new Rectangle { Width = part.W, Height = part.H, RadiusX = part.Radius, RadiusY = part.Radius };

            shape.StrokeThickness = filled && part.Role == Role.Shape ? 1 : filled && part.Role == Role.Detail ? 1.4 : thickness;
            shape.StrokeStartLineCap = PenLineCap.Round;
            shape.StrokeEndLineCap = PenLineCap.Round;
            shape.StrokeLineJoin = PenLineJoin.Round;
            shape.Stroke = filled && part.Role == Role.Detail ? cut : foreground;
            shape.Fill = filled && part.Role == Role.Shape ? foreground : filled && part.Ellipse ? cut : null;
            if (part.Path is null)
            {
                Canvas.SetLeft(shape, part.X);
                Canvas.SetTop(shape, part.Y);
            }
            canvas.Children.Add(shape);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>A horizontal battery whose fill follows the level.</summary>
    public static UIElement Gauge(double? percent, double size, Brush foreground, Brush fill)
    {
        double w = Math.Round(size * 1.35), h = Math.Round(size * 0.62);
        var canvas = new Canvas { Width = w + 3, Height = h };
        var body = new Rectangle { Width = w - 1.6, Height = h - 1.6, RadiusX = 2.2, RadiusY = 2.2, Stroke = foreground, StrokeThickness = 1.5 };
        Canvas.SetLeft(body, 0.8); Canvas.SetTop(body, 0.8);
        var nub = new Rectangle { Width = 2.2, Height = h * 0.36, RadiusX = 1, RadiusY = 1, Fill = foreground };
        Canvas.SetLeft(nub, w - 0.2); Canvas.SetTop(nub, h * 0.32);
        canvas.Children.Add(body);
        canvas.Children.Add(nub);
        var level = Math.Clamp(percent ?? 0, 0, 100);
        if (percent is not null && level > 0)
        {
            var bar = new Rectangle { Width = Math.Max(1, level / 100 * (w - 7)), Height = h - 6, RadiusX = 1, RadiusY = 1, Fill = fill };
            Canvas.SetLeft(bar, 3); Canvas.SetTop(bar, 3);
            canvas.Children.Add(bar);
        }
        return canvas;
    }
}
