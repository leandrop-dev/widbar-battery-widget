using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.Core.Tests;

public class SettingsTests
{
    private static Metric M(string id) => new(id, id, MetricKind.Percent, 1, "%", "s");
    private static IReadOnlyList<Metric> Metrics(params string[] ids) => ids.Select(M).ToList();
    private static string[] Ids(IEnumerable<Metric> m) => m.Select(x => x.Id).ToArray();

    [Fact]
    public void Defaults_ShowEverythingInProviderOrder() =>
        Assert.Equal(["a", "b", "c"], Ids(TileLayout.Apply(Metrics("a", "b", "c"), new WidgetSettings())));

    [Fact]
    public void Hidden_AreRemoved() =>
        Assert.Equal(["a", "c"], Ids(TileLayout.Apply(Metrics("a", "b", "c"), new WidgetSettings { Hidden = ["b"] })));

    [Fact]
    public void Order_PutsListedFirstAndUnlistedLastInOriginalOrder() =>
        Assert.Equal(["c", "a", "b", "d"], Ids(TileLayout.Apply(Metrics("a", "b", "c", "d"), new WidgetSettings { Order = ["c", "a"] })));

    [Fact]
    public void SavedIdsThatAreNotPresentAreIgnoredWhenShowing() =>
        Assert.Equal(["b", "a"], Ids(TileLayout.Apply(Metrics("a", "b"), new WidgetSettings { Order = ["gone", "b", "a"] })));

    [Fact]
    public void DuplicateIdsInOrderUseTheFirstPosition() =>
        Assert.Equal(["b", "a"], Ids(TileLayout.Apply(Metrics("a", "b"), new WidgetSettings { Order = ["b", "a", "b"] })));

    [Fact]
    public void Ordered_ListsEveryPresentIdIncludingHiddenOnes()
    {
        var s = new WidgetSettings { Order = ["b"], Hidden = ["a"] };
        Assert.Equal(["b", "a", "c"], TileLayout.Ordered(["a", "b", "c"], s));
    }

    [Fact]
    public void Move_SwapsWithTheNeighbourAndKeepsAbsentIds()
    {
        var s = new WidgetSettings { Order = ["gone"] };
        var moved = TileLayout.Move(s, ["a", "b", "c"], "c", -1);
        Assert.Equal(["a", "c", "b", "gone"], moved.Order);
        Assert.Equal(["gone"], s.Order); // the original is not modified
    }

    [Fact]
    public void Move_AtTheEdgesOrForUnknownIdChangesNothing()
    {
        var s = new WidgetSettings();
        Assert.Same(s, TileLayout.Move(s, ["a", "b"], "a", -1));
        Assert.Same(s, TileLayout.Move(s, ["a", "b"], "b", +1));
        Assert.Same(s, TileLayout.Move(s, ["a", "b"], "zzz", +1));
    }

    [Fact]
    public void SetVisible_TogglesWithoutDuplicates()
    {
        var s = TileLayout.SetVisible(new WidgetSettings(), "a", false);
        s = TileLayout.SetVisible(s, "a", false);
        Assert.Equal(["a"], s.Hidden);
        Assert.Empty(TileLayout.SetVisible(s, "a", true).Hidden);
    }
}
