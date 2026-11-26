using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>
/// Draws the taskbar preview: one tile per shown device in a row. The taskbar and the live preview card on the
/// settings page both use it, so what the page shows is what the taskbar gets.
/// </summary>
internal static class PreviewBuilder
{
    /// <summary>Space kept free at each end of the row.</summary>
    public const double SidePadding = 4;

    /// <summary>
    /// Fills <paramref name="row"/> with the tiles for <paramref name="shown"/> and returns the width the row needs,
    /// side padding included. UI thread only.
    /// </summary>
    public static int Fill(StackPanel row, BatteryWidgetSettings settings, IReadOnlyList<Metric> shown, double slotHeight, bool dark)
    {
        var room = Math.Max(16, slotHeight - 4);
        var metrics = LookMetrics.For(PillStyle.Transparent, settings.Appearance.Scale, Math.Min(32, room), room);
        var tiles = new TileFactory(settings, metrics, dark);

        row.Children.Clear();
        if (shown.Count == 0)
        {
            row.Children.Add(new TextBlock
            {
                Text = "No batteries",
                FontSize = metrics.FontSize,
                Foreground = new Ink(dark).Secondary,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        foreach (var m in shown)
        {
            if (row.Children.Count > 0) row.Children.Add(tiles.Divider());
            row.Children.Add(tiles.Build(m));
        }

        row.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return (int)Math.Ceiling(row.DesiredSize.Width + SidePadding * 2);
    }
}
