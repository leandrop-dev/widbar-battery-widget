using System.Globalization;

namespace Widgets.Core;

/// <summary>How a metric's value is written on a tile.</summary>
public static class MetricFormat
{
    /// <summary>The metric's own text when it has one, else "--" when there is no value, a leading "~" when the value is an estimate.</summary>
    public static string Value(Metric m, CultureInfo? culture = null)
    {
        if (!string.IsNullOrEmpty(m.Text)) return m.Text;
        culture ??= CultureInfo.CurrentCulture;
        if (m.Value is not { } v || !double.IsFinite(v)) return "--";

        var text = m.Kind switch
        {
            MetricKind.Cost => string.Create(culture, $"${v:0.00}"),
            MetricKind.Tokens => Compact(v, culture),
            _ => string.Create(culture, $"{Math.Round(v, MidpointRounding.AwayFromZero):0}{m.Unit}"),
        };
        return m.Approximate ? "~" + text : text;
    }

    /// <summary>1234 -> "1.2k", 1250000 -> "1.3M". Below 1000 the exact number is shown.</summary>
    public static string Compact(double v, CultureInfo culture)
    {
        var abs = Math.Abs(v);
        if (abs < 1_000) return string.Create(culture, $"{Math.Round(v, MidpointRounding.AwayFromZero):0}");
        if (abs < 999_950) return string.Create(culture, $"{v / 1_000:0.#}k");
        if (abs < 999_950_000) return string.Create(culture, $"{v / 1_000_000:0.#}M");
        return string.Create(culture, $"{v / 1_000_000_000:0.#}B");
    }
}
