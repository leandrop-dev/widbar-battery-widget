using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>Builds the tiles of the taskbar preview: icon, name, value and charging bolt for each device, in the chosen look.</summary>
internal sealed class TileFactory
{
    private readonly BatteryWidgetSettings _settings;
    private readonly LookMetrics _m;
    private readonly Ink _ink;

    public TileFactory(BatteryWidgetSettings settings, LookMetrics metrics, bool dark)
    {
        _settings = settings;
        _m = metrics;
        _ink = new Ink(dark);
    }

    /// <summary>A thin vertical line between two tiles.</summary>
    public UIElement Divider() => new Rectangle
    {
        Width = 1,
        Height = Math.Round(_m.Height * 0.5),
        Fill = Ink.Solid(PillTint.NeutralColor(_ink.Dark), 0.3),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(Math.Max(0, _m.Gap - 1), 0, Math.Max(0, _m.Gap - 1), 0),
    };

    public UIElement Build(Metric m)
    {
        var look = _settings.Appearance;
        var band = BatteryColors.BandFor(m.Value, _settings.LowThreshold);
        var low = band == BatteryBand.Low && !m.Charging && m.Value is not null && !m.Stale;

        Brush valueBrush = _ink.Primary;
        Brush iconBrush = _ink.Primary;
        if (m.Value is not null)
        {
            valueBrush = Ink.Solid(BatteryColors.For(look, band, _ink.Dark, _ink.Bar));
            iconBrush = valueBrush;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        UIElement? icon = look.Icons == IconStyle.Battery && m.Value is not null
            ? TileIcons.Gauge(m.Value, _m.IconSize, iconBrush, iconBrush)
            : TileIcons.Build(m.Icon, look.Icons == IconStyle.Battery ? IconStyle.Outline : look.Icons, _m.IconSize, iconBrush, Ink.Solid(_ink.Bar));
        if (icon is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(0, 0, _m.IconGap, 0);
            row.Children.Add(fe);
        }

        if (look.ShowNames)
        {
            row.Children.Add(new TextBlock
            {
                Text = m.Label,
                FontSize = _m.FontSize - 1,
                Foreground = _ink.Secondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, _m.IconGap, 0),
            });
        }

        if (low)
        {
            // No pill outline carries the warning here, so a "!" does.
            row.Children.Add(new TextBlock
            {
                Text = "!",
                FontSize = _m.FontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = iconBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
            });
        }

        var value = new TextBlock
        {
            Text = MetricFormat.Value(m),
            FontSize = _m.FontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = valueBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (string.IsNullOrEmpty(m.Text))
        {
            // Reserve the width of "100%", so the tile does not change width as 9% becomes 10% or 100%.
            var widest = new TextBlock { Text = MetricFormat.Value(m with { Value = 100 }), FontSize = value.FontSize, FontWeight = value.FontWeight };
            widest.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            value.MinWidth = Math.Ceiling(widest.DesiredSize.Width);
            value.TextAlignment = TextAlignment.Right;
        }
        row.Children.Add(value);

        if (m.Charging)
        {
            row.Children.Add(new FontIcon
            {
                Glyph = ((char)0xE945).ToString(), // Segoe Fluent lightning bolt: the standard Windows charging mark
                FontSize = Math.Max(12, _m.FontSize - 2),
                Margin = new Thickness(-1, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _ink.Success,
            });
        }

        var tile = new Border
        {
            Child = row,
            Height = _m.Height,
            Padding = new Thickness(_m.PadX, 0, _m.PadX, 0),
            Opacity = m.Stale ? 0.5 : 1.0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var status = DeviceStatus.Describe(m, DateTimeOffset.Now);
        var spoken = $"{m.Label} {MetricFormat.Value(m)}";
        AutomationProperties.SetName(tile, string.IsNullOrEmpty(status) ? spoken : $"{spoken}, {status}");
        return tile;
    }
}
