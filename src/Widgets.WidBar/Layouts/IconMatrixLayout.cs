using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar.Layouts;

/// <summary>Icon matrix: devices in two rows, filled column by column, each with its icon, a small label and its value.</summary>
internal sealed class IconMatrixLayout : ITileLayout
{
    private const double NaturalHeight = 34; // two rows of 16 and the gap between them

    public FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext c)
    {
        var k = TileParts.Fit(NaturalHeight, c);
        var rows = Math.Min(2, Math.Max(1, shown.Count));
        var columns = PreviewGrid.Columns(shown.Count, rows);

        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnSpacing = TileParts.Gap(c, 8, 14, 22) * k,
            RowSpacing = 2 * k,
        };
        for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var col = 0; col < columns; col++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < shown.Count; i++)
        {
            var (column, row) = PreviewGrid.Cell(i, shown.Count, rows);
            var cell = Cell(shown[i], c, k);
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static FrameworkElement Cell(Metric m, TileContext c, double k)
    {
        var ink = TileParts.InkFor(m, c);
        var look = c.Look;
        var cell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var iconBrush = TileParts.Brush(ink.Icon);
        var size = Math.Round(13 * k);
        // The battery gauge only makes sense with a level; a device with none gets the plain outline icon instead.
        UIElement? icon = look.Icons == IconStyle.Battery && m.Value is not null
            ? TileIcons.Gauge(m.Value, size, iconBrush, iconBrush)
            : TileIcons.Build(m.Icon, look.Icons == IconStyle.Battery ? IconStyle.Outline : look.Icons, size, iconBrush, TileParts.Brush(c.Ink.Bar));
        if (icon is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(0, 0, 4 * k, 0);
            cell.Children.Add(fe);
        }

        if (look.ShowNames)
        {
            var label = TileParts.Text(TileParts.LabelOf(m, c), Math.Round(9 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Label));
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 4 * k, 0);
            cell.Children.Add(label);
        }

        cell.Children.Add(TileParts.ValueBlock(m, c, Math.Round(14 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Value)));
        if (TileParts.Bolt(m, c, Math.Round(12 * k)) is { } bolt) cell.Children.Add(bolt);
        return TileParts.Finish(cell, m, c);
    }
}
