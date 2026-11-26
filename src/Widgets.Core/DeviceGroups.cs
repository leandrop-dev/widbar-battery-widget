namespace Widgets.Core;

/// <summary>One tab of the flyout: the devices of one kind. <see cref="Key"/> is stable, so a tab stays selected across refreshes.</summary>
public sealed record DeviceGroup(string Key, string Title, IReadOnlyList<Metric> Devices);

/// <summary>Splits the shown devices into the per-kind tabs of the flyout. The Summary tab is not a group; the flyout adds it.</summary>
public static class DeviceGroups
{
    // The fixed order of the tabs; "other" always comes last.
    private static readonly (string Key, string Title)[] Kinds =
    [
        (MetricIcons.Headset, "Headsets"),
        (MetricIcons.Mouse, "Mice"),
        (MetricIcons.Keyboard, "Keyboards"),
        (MetricIcons.Gamepad, "Controllers"),
    ];

    public const string OtherKey = "other";

    public static IReadOnlyList<DeviceGroup> For(IReadOnlyList<Metric> metrics)
    {
        var known = Kinds.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var groups = new List<DeviceGroup>();
        foreach (var (key, title) in Kinds)
        {
            var devices = metrics.Where(m => m.Icon == key).ToList();
            if (devices.Count > 0) groups.Add(new DeviceGroup(key, title, devices));
        }
        var other = metrics.Where(m => m.Icon is null || !known.Contains(m.Icon)).ToList();
        if (other.Count > 0) groups.Add(new DeviceGroup(OtherKey, "Other", other));
        return groups;
    }
}
