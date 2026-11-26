namespace Widgets.Core.Appearance;

/// <summary>Geometry of a gauge ring that starts at 12 o'clock and runs clockwise.</summary>
public static class Arc
{
    /// <summary>The point on the ring where an arc of <paramref name="fraction"/> (0 to 1) of the full circle ends. Y grows downwards.</summary>
    public static (double X, double Y) PointAt(double cx, double cy, double radius, double fraction)
    {
        var angle = Math.Clamp(fraction, 0, 1) * 2 * Math.PI;
        return (cx + radius * Math.Sin(angle), cy - radius * Math.Cos(angle));
    }

    /// <summary>True when the arc is longer than half the circle, which an arc segment needs to know to pick the long way round.</summary>
    public static bool IsLargeArc(double fraction) => fraction > 0.5;
}
