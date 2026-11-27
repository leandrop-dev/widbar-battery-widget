namespace Widgets.Core;

/// <summary>Decides whether a device gets a tile on the taskbar right now. Pure, so the rule is tested without any UI.</summary>
public static class DevicePresence
{
    /// <summary>
    /// False for a device that stepped aside for another one, and for a device that has been disconnected for longer than
    /// <paramref name="hideAfter"/> (null means never hide for time). A device that is connected is always shown.
    /// </summary>
    public static bool IsShown(Metric m, DateTimeOffset now, TimeSpan? hideAfter)
    {
        if (m.Superseded) return false;
        if (hideAfter is not { } limit) return true;
        return OfflineFor(m, now) is not { } offline || offline <= limit;
    }

    /// <summary>
    /// How long the device has been disconnected, or null while it is connected. A reading that went stale without the
    /// provider knowing why (a failed poll) counts from when it was taken.
    /// </summary>
    public static TimeSpan? OfflineFor(Metric m, DateTimeOffset now)
    {
        var since = m.OfflineSince ?? (m.Stale ? m.Updated : null);
        return since is { } at ? now - at : null;
    }

    /// <summary>A text for a device that is not on the taskbar, or null when it is. Used by the settings list.</summary>
    public static string? WhyHidden(Metric m, DateTimeOffset now, TimeSpan? hideAfter)
    {
        if (IsShown(m, now, hideAfter)) return null;
        return m.Superseded ? "Hidden while another controller is connected" : "Hidden while disconnected";
    }
}
