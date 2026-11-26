using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>The panel that opens when the preview is clicked: every shown device with its level, a level bar and its state.</summary>
internal sealed class FlyoutView
{
    private readonly Grid _root = new() { Padding = new Thickness(16) };
    private readonly TextBlock _title = new()
    {
        Text = "Device batteries",
        FontSize = 16,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 12),
    };
    private readonly StackPanel _list = new() { Spacing = 14 };

    private IReadOnlyList<Metric> _metrics = [];
    private BatteryWidgetSettings _settings = new();

    public FlyoutView()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_title, 0);
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(_title);
        _root.Children.Add(scroll);

        // The flyout keeps its content for good, so the colours are rebuilt whenever the theme changes.
        _root.ActualThemeChanged += (_, _) => Refresh(_metrics, _settings);
    }

    /// <summary>The flyout content. Its root has no background, so the material the user picked in WidBar shows through.</summary>
    public UIElement Root => _root;

    public void Refresh(IReadOnlyList<Metric> metrics, BatteryWidgetSettings settings)
    {
        _metrics = metrics;
        _settings = settings;

        var ink = new Ink(_root.ActualTheme == ElementTheme.Dark);
        _title.Foreground = ink.Primary;
        _list.Children.Clear();

        if (metrics.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "No batteries to show. Connect or wake a headset, mouse or controller.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ink.Secondary,
            });
            return;
        }

        var now = DateTimeOffset.Now;
        foreach (var m in metrics) _list.Children.Add(Row(m, ink, now));
    }

    private UIElement Row(Metric m, Ink ink, DateTimeOffset now)
    {
        var look = _settings.Appearance;
        var band = BatteryColors.BandFor(m.Value, _settings.LowThreshold);
        var accent = m.Value is null
            ? ink.Primary
            : Ink.Solid(BatteryColors.For(look, band, ink.Dark, ink.Bar));

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6, Opacity = m.Stale ? 0.65 : 1.0 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The flyout always shows outline icons, whatever the taskbar tiles use: it is detail, not a glance.
        var icon = TileIcons.Build(m.Icon, IconStyle.Outline, 24, accent, Ink.Solid(ink.Bar));
        if (icon is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 0);
            grid.Children.Add(fe);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = m.Label, FontSize = 14, Foreground = ink.Primary });
        var status = DeviceStatus.Describe(m, now);
        if (status.Length > 0)
            text.Children.Add(new TextBlock { Text = status, FontSize = 12, Foreground = ink.Secondary, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var value = new TextBlock
        {
            Text = MetricFormat.Value(m),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        if (m.Value is { } level)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(level, 0, 100), Foreground = accent };
            Grid.SetRow(bar, 1);
            Grid.SetColumnSpan(bar, 3);
            grid.Children.Add(bar);
        }

        AutomationProperties.SetName(grid, status.Length > 0
            ? $"{m.Label} {MetricFormat.Value(m)}, {status}"
            : $"{m.Label} {MetricFormat.Value(m)}");
        return grid;
    }
}
