using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Widgets.Core;
using Widgets.Core.Settings;
using Widgets.WidBar.Layouts;

namespace Widgets.WidBar;

/// <summary>
/// Draws the taskbar preview in the layout the user chose. The taskbar and the live preview card on the settings page
/// both use it, so what the page shows is what the taskbar gets, including the width.
/// </summary>
internal static class PreviewBuilder
{
    /// <summary>Space kept free at each end of the row.</summary>
    public const double SidePadding = 4;

    /// <summary>The narrowest preview WidBar is given, whatever the content and the width adjustment.</summary>
    public const int MinWidth = 60;

    private static ITileLayout LayoutFor(PreviewLayout layout) => layout switch
    {
        PreviewLayout.MinimalIcons => new MinimalIconsLayout(),
        PreviewLayout.MinimalText => new MinimalTextLayout(),
        _ => new VerticalMetersLayout(),
    };

    /// <summary>
    /// Fills <paramref name="row"/> with the preview for <paramref name="shown"/> and returns the width it will have,
    /// side padding included: the content's width plus the "Preview width adjustment", never below <see cref="MinWidth"/>.
    /// The content sits left, centred or right inside that width as the user chose, and is clipped when the width is
    /// narrower than it. UI thread only.
    /// </summary>
    public static int Fill(StackPanel row, BatteryWidgetSettings settings, IReadOnlyList<Metric> shown, double slotHeight, bool dark)
    {
        var context = new TileContext(settings, dark, slotHeight, TileContext.ReadWindowsAccent());
        var content = shown.Count == 0 ? TileParts.NoBatteries(context) : LayoutFor(settings.Appearance.Layout).Build(shown, context);

        content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = (int)Math.Ceiling(content.DesiredSize.Width + SidePadding * 2);
        var width = Math.Max(MinWidth, natural + settings.Appearance.WidthAdjustment);
        var inner = Math.Max(0, width - SidePadding * 2);

        content.HorizontalAlignment = settings.Appearance.Alignment switch
        {
            ContentAlignment.Center => HorizontalAlignment.Center,
            ContentAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };
        content.VerticalAlignment = VerticalAlignment.Center;

        var frame = new Grid
        {
            Width = inner,
            Height = slotHeight,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, inner, slotHeight) },
            Children = { content },
        };

        row.Children.Clear();
        row.Children.Add(frame);
        return width;
    }
}
