namespace Widgets.Core.Settings;

/// <summary>User choices that are stored on disk. Unknown ids are kept, so a device that is unplugged today keeps its place.</summary>
public sealed class WidgetSettings
{
    /// <summary>Metric ids in the order the user wants them. Ids that are not listed follow, in provider order.</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>Metric ids the user turned off.</summary>
    public List<string> Hidden { get; set; } = [];

    public AppearanceSettings Appearance { get; set; } = new();

    public WidgetSettings Clone() => new() { Order = [.. Order], Hidden = [.. Hidden], Appearance = Appearance.Clone() };
}

/// <summary>Chooses and orders the tiles. Pure, so it can be tested without any UI.</summary>
public static class TileLayout
{
    /// <summary>The metrics to show: hidden ones removed, the rest in the user's order, unlisted ones last in their original order.</summary>
    public static IReadOnlyList<Metric> Apply(IReadOnlyList<Metric> metrics, WidgetSettings settings)
    {
        var hidden = new HashSet<string>(settings.Hidden, StringComparer.Ordinal);
        var rank = Rank(settings.Order);
        return metrics
            .Where(m => !hidden.Contains(m.Id))
            .Select((m, i) => (m, i))
            .OrderBy(x => rank.TryGetValue(x.m.Id, out var r) ? r : int.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.m)
            .ToList();
    }

    /// <summary>
    /// The full list of ids in display order (hidden ones included), for a settings screen to show and edit.
    /// Ids that were saved but are not present right now are kept in their saved position.
    /// </summary>
    public static IReadOnlyList<string> Ordered(IEnumerable<string> presentIds, WidgetSettings settings)
    {
        var present = presentIds.Distinct(StringComparer.Ordinal).ToList();
        var rank = Rank(settings.Order);
        var known = present.Where(rank.ContainsKey).OrderBy(id => rank[id]);
        var unlisted = present.Where(id => !rank.ContainsKey(id));
        return [.. known, .. unlisted];
    }

    /// <summary>Returns settings where <paramref name="id"/> moved one place earlier (-1) or later (+1) among <paramref name="shownIds"/>.</summary>
    public static WidgetSettings Move(WidgetSettings settings, IReadOnlyList<string> shownIds, string id, int direction)
    {
        var list = Ordered(shownIds, settings).ToList();
        var from = list.IndexOf(id);
        var to = from + Math.Sign(direction);
        if (from < 0 || to < 0 || to >= list.Count) return settings;

        (list[from], list[to]) = (list[to], list[from]);
        var next = settings.Clone();
        // Keep ids that are saved but not present right now at the end, so they are not lost.
        next.Order = [.. list, .. settings.Order.Where(o => !list.Contains(o, StringComparer.Ordinal))];
        return next;
    }

    public static WidgetSettings SetVisible(WidgetSettings settings, string id, bool visible)
    {
        var next = settings.Clone();
        next.Hidden.RemoveAll(h => h == id);
        if (!visible) next.Hidden.Add(id);
        return next;
    }

    private static Dictionary<string, int> Rank(IEnumerable<string> order)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in order) rank.TryAdd(id, rank.Count);
        return rank;
    }
}
