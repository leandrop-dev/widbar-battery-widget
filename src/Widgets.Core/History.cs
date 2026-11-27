namespace Widgets.Core;

/// <summary>One remembered reading of a device's battery.</summary>
public sealed record HistorySample(DateTimeOffset At, int Percent, bool Charging);

/// <summary>
/// "About how long until it is empty", from a device's recent readings. Pure, so the rules are tested without any UI.
/// It says nothing rather than guess: it needs a steady, finely graded discharge it can fit a line to.
/// </summary>
public static class DischargeEstimate
{
    /// <summary>Readings must differ by at least this many distinct levels. Rules out XInput's four coarse steps.</summary>
    public const int MinLevels = 5;

    /// <summary>The level must have fallen at least this many points over the run.</summary>
    public const int MinDrop = 4;

    /// <summary>A reading may sit this many points below the one after it before it counts as a battery swap or a charge.</summary>
    public const int Noise = 2;

    public static readonly TimeSpan MinSpan = TimeSpan.FromHours(1);
    /// <summary>The newest reading must be this fresh, so a sleeping or unplugged device gets no estimate.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);
    /// <summary>An estimate beyond this is a flat line, not a forecast.</summary>
    public static readonly TimeSpan MaxLeft = TimeSpan.FromDays(60);

    /// <summary>The time left at the current rate, or null when the readings do not support an estimate.</summary>
    public static TimeSpan? TimeLeft(IReadOnlyList<HistorySample> samples, DateTimeOffset now)
    {
        if (samples.Count < 2) return null;
        var ordered = samples.OrderBy(s => s.At).ToList();
        var last = ordered[^1];
        if (last.Charging || last.Percent <= 0 || now - last.At > MaxAge) return null;

        // The latest unbroken discharge: walk back until a charging reading or a rise (a swapped battery).
        var start = ordered.Count - 1;
        while (start > 0)
        {
            var before = ordered[start - 1];
            if (before.Charging || before.Percent < ordered[start].Percent - Noise) break;
            start--;
        }
        var run = ordered.GetRange(start, ordered.Count - start);

        if (last.At - run[0].At < MinSpan) return null;
        if (run.Select(s => s.Percent).Distinct().Count() < MinLevels) return null;
        if (run.Max(s => s.Percent) - last.Percent < MinDrop) return null;

        // Least-squares line through (hours since the run began, percent).
        double n = run.Count, sx = 0, sy = 0, sxy = 0, sxx = 0;
        foreach (var s in run)
        {
            var x = (s.At - run[0].At).TotalHours;
            sx += x; sy += s.Percent; sxy += x * s.Percent; sxx += x * x;
        }
        var denominator = n * sxx - sx * sx;
        if (denominator <= 0) return null;
        var rate = -(n * sxy - sx * sy) / denominator; // percent per hour, positive while draining
        if (rate < 0.01) return null;

        var left = TimeSpan.FromHours(last.Percent / rate);
        return left > MaxLeft ? null : left;
    }

    /// <summary>"under 1 h", "about 6 h", "about 3 d".</summary>
    public static string Describe(TimeSpan left)
    {
        if (left < TimeSpan.FromHours(1)) return "under 1 h";
        if (left < TimeSpan.FromHours(48)) return $"about {(int)Math.Round(left.TotalHours)} h";
        return $"about {(int)Math.Round(left.TotalDays)} d";
    }
}

/// <summary>A point of a sparkline, both axes from 0 to 1. Y grows downwards, like a screen: 0 is full, 1 is empty.</summary>
public sealed record SparkPoint(double X, double Y);

/// <summary>A stretch of a sparkline drawn in one style: discharging, or charging (drawn fainter).</summary>
public sealed record SparkSegment(bool Charging, IReadOnlyList<SparkPoint> Points);

/// <summary>The shape of a battery's last day as lines, without any pixels, so it is tested without any UI.</summary>
public static class Sparkline
{
    /// <summary>How far past its last reading a connected device's line is carried to the right edge. A sleeping device stops short.</summary>
    public static readonly TimeSpan Carry = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Segments covering the last <paramref name="window"/> up to <paramref name="now"/>. The reading just before the window is
    /// carried to its left edge so the line starts at the edge, and consecutive segments share their boundary point.
    /// </summary>
    public static IReadOnlyList<SparkSegment> Segments(IReadOnlyList<HistorySample> samples, DateTimeOffset now, TimeSpan window)
    {
        var start = now - window;
        var ordered = samples.Where(s => s.At <= now).OrderBy(s => s.At).ToList();

        var points = new List<(SparkPoint P, bool Charging)>();
        if (ordered.LastOrDefault(s => s.At < start) is { } before) points.Add((Point(0, before.Percent), before.Charging));
        foreach (var s in ordered.Where(s => s.At >= start))
            points.Add((Point((s.At - start) / window, s.Percent), s.Charging));
        if (points.Count == 0) return [];

        var newest = ordered[^1];
        if (now - newest.At <= Carry && points[^1].P.X < 1) points.Add((Point(1, newest.Percent), newest.Charging));

        var segments = new List<SparkSegment>();
        var current = new List<SparkPoint> { points[0].P };
        var charging = points[0].Charging;
        for (var i = 1; i < points.Count; i++)
        {
            // The state of the span between two readings is the state of the earlier one.
            if (points[i - 1].Charging != charging)
            {
                segments.Add(new SparkSegment(charging, current));
                charging = points[i - 1].Charging;
                current = [points[i - 1].P];
            }
            current.Add(points[i].P);
        }
        segments.Add(new SparkSegment(charging, current));
        return segments;
    }

    /// <summary>A line narrower than this share of the window (about half an hour of 24) is a dot, not history yet.</summary>
    public const double MinLineSpan = 0.02;

    /// <summary>True when there is a line worth drawing: points that reach across at least <see cref="MinLineSpan"/> of the window.</summary>
    public static bool HasLine(IReadOnlyList<SparkSegment> segments)
    {
        var xs = segments.SelectMany(s => s.Points).Select(p => p.X).ToList();
        return xs.Count >= 2 && xs.Max() - xs.Min() >= MinLineSpan;
    }

    private static SparkPoint Point(double x, int percent) =>
        new(Math.Clamp(x, 0, 1), 1 - Math.Clamp(percent, 0, 100) / 100.0);
}
