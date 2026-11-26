using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Appearance;

namespace Widgets.WidBar.Layouts;

/// <summary>Fill capsules: devices in two rows, filled column by column; each a pill filled to its level with label and value on it.</summary>
internal sealed class FillCapsulesLayout : ITileLayout
{
    private const double NaturalHeight = 31; // two capsules of 14 and the gap between them
    private const double CapsuleWidth = 76;

    public FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext c)
    {
        var k = TileParts.Fit(NaturalHeight, c);
        var rows = Math.Min(2, Math.Max(1, shown.Count));
        var columns = PreviewGrid.Columns(shown.Count, rows);

        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnSpacing = TileParts.Gap(c, 4, 8, 14) * k,
            RowSpacing = 3 * k,
        };
        for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var col = 0; col < columns; col++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < shown.Count; i++)
        {
            var (column, row) = PreviewGrid.Cell(i, shown.Count, rows);
            var capsule = Capsule(shown[i], c, k);
            Grid.SetColumn(capsule, column);
            Grid.SetRow(capsule, row);
            grid.Children.Add(capsule);
        }
        return grid;
    }

    private static FrameworkElement Capsule(Metric m, TileContext c, double k)
    {
        var ink = TileParts.InkFor(m, c);
        var width = Math.Round(CapsuleWidth * k);
        var height = Math.Round(14 * k);
        var radius = height / 2;

        var capsule = new Grid { Width = width, Height = height };
        capsule.Children.Add(new Rectangle { Fill = TileParts.Brush(ink.Accent, 0.25), RadiusX = radius, RadiusY = radius });

        var level = Math.Clamp(m.Value ?? 0, 0, 100) / 100.0;
        if (m.Value is not null && level > 0)
        {
            capsule.Children.Add(new Rectangle
            {
                Fill = TileParts.Brush(ink.Accent, 0.7),
                Width = Math.Max(height, Math.Round(width * level)), // at least a full cap, so a low level still reads as a pill
                HorizontalAlignment = HorizontalAlignment.Left,
                RadiusX = radius, RadiusY = radius,
            });
        }

        var inner = new Grid { Margin = new Thickness(5 * k, 0, 5 * k, 0) };
        if (c.Look.ShowNames)
        {
            var label = TileParts.Text(TileParts.LabelOf(m, c), Math.Round(8 * k, 1), FontWeights.Bold, TileParts.Brush(ink.Label));
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.MaxWidth = width * 0.5;
            inner.Children.Add(label);
        }

        var value = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        value.Children.Add(TileParts.ValueBlock(m, c, Math.Round(11 * k, 1), FontWeights.SemiBold, TileParts.Brush(ink.Value)));
        if (TileParts.Bolt(m, c, Math.Round(10 * k)) is { } bolt) value.Children.Add(bolt);
        inner.Children.Add(value);
        capsule.Children.Add(inner);

        return TileParts.Finish(capsule, m, c);
    }
}
