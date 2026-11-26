namespace Widgets.Core;

/// <summary>The one-line state of a device battery, for a detail view. Pure, so the wording is tested without any UI.</summary>
public static class DeviceStatus
{
    /// <summary>"Charging", "Switched off", "No signal, last seen 3 min ago", the metric's own text such as "USB", or an empty string for a normal live reading.</summary>
    public static string Describe(Metric m, DateTimeOffset now, int? lowAt = null)
    {
        if (!string.IsNullOrEmpty(m.Text)) return m.Text;
        if (m.Value is null) return "Switched off";
        if (m.Stale) return m.Updated is { } seen ? $"No signal, last seen {Ago(now - seen)}" : "No signal";
        if (m.Charging) return "Charging";
        if (lowAt is { } threshold && IsLow(m, threshold)) return "Low battery";
        return "";
    }

    /// <summary>One short word for a column: "Connected", "Charging", "Low battery", "No signal", "Switched off", or the metric's own text.</summary>
    public static string State(Metric m, int lowAt)
    {
        if (!string.IsNullOrEmpty(m.Text)) return m.Text;
        if (m.Value is null) return "Switched off";
        if (m.Stale) return "No signal";
        if (m.Charging) return "Charging";
        return IsLow(m, lowAt) ? "Low battery" : "Connected";
    }

    /// <summary>True for a fresh reading at or below <paramref name="lowAt"/> percent on a device that is not charging: the case that gets a "!" next to it.</summary>
    public static bool IsLow(Metric m, int lowAt) => m.Value is { } v && !m.Stale && !m.Charging && v <= lowAt;

    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago". A negative span (a clock that moved back) counts as just now.</summary>
    public static string Ago(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} d ago";
    }
}
