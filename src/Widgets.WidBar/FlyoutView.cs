using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;
using Widgets.WidBar.Layouts;

namespace Widgets.WidBar;

/// <summary>
/// The panel that opens when the preview is clicked, laid out like WidBar's System Metrics flyout: a strip of tabs
/// (Summary, then one per kind of device) over a scrolling page of rounded cards.
/// </summary>
internal sealed class FlyoutView
{
    private const string SummaryKey = "summary";

    private readonly Grid _root = new() { Padding = new Thickness(16, 12, 16, 16), RowSpacing = 8 };
    private readonly SelectorBar _tabs = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Border _divider;
    private readonly StackPanel _page = new() { Spacing = 8 };
    private readonly ScrollViewer _scroll;

    private IReadOnlyList<Metric> _metrics = [];
    private BatteryWidgetSettings _settings = new();
    private string _selected = SummaryKey;
    private List<string> _tabKeys = [];
    private bool _building;

    public FlyoutView()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _divider = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Height='1' Background='{ThemeResource DividerStrokeColorDefaultBrush}'/>");

        _scroll = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(_divider, 1);
        Grid.SetRow(_scroll, 2);
        _root.Children.Add(_tabs);
        _root.Children.Add(_divider);
        _root.Children.Add(_scroll);

        _tabs.SelectionChanged += (_, _) =>
        {
            if (_building || _tabs.SelectedItem?.Tag is not string key || key == _selected) return;
            _selected = key;
            RenderPage();
        };

        // The flyout keeps its content for good, so the colours are rebuilt whenever the theme changes.
        _root.ActualThemeChanged += (_, _) => Refresh(_metrics, _settings);
    }

    /// <summary>The flyout content. Its root has no background, so the material the user picked in WidBar shows through.</summary>
    public UIElement Root => _root;

    public void Refresh(IReadOnlyList<Metric> metrics, BatteryWidgetSettings settings)
    {
        _metrics = metrics;
        _settings = settings;
        RebuildTabs();
        RenderPage();
    }

    // ---- tabs

    private void RebuildTabs()
    {
        var groups = DeviceGroups.For(_metrics);
        var keys = new List<string> { SummaryKey };
        keys.AddRange(groups.Select(g => g.Key));

        // Keep the user on the tab they chose; fall back to Summary when its devices are gone.
        if (!keys.Contains(_selected)) _selected = SummaryKey;

        _tabs.Visibility = _divider.Visibility = _metrics.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (!keys.SequenceEqual(_tabKeys))
        {
            _building = true;
            _tabs.Items.Clear();
            _tabs.Items.Add(new SelectorBarItem { Text = "Summary", Tag = SummaryKey });
            foreach (var g in groups) _tabs.Items.Add(new SelectorBarItem { Text = g.Title, Tag = g.Key });
            _tabKeys = keys;
            _building = false;
        }

        _building = true;
        _tabs.SelectedItem = _tabs.Items.OfType<SelectorBarItem>().FirstOrDefault(i => (i.Tag as string) == _selected);
        _building = false;
    }

    // ---- pages

    private void RenderPage()
    {
        _page.Children.Clear();
        if (_metrics.Count == 0)
        {
            _page.Children.Add(Cards.Secondary("No batteries to show. Connect or wake a headset, mouse or controller.", 14));
            return;
        }

        var ink = new Ink(_root.ActualTheme == ElementTheme.Dark);
        var accentColour = TileContext.ReadWindowsAccent();
        var now = DateTimeOffset.Now;

        if (_selected == SummaryKey)
        {
            _page.Children.Add(SummaryGrid(_metrics, ink, accentColour, now));
            return;
        }

        var group = DeviceGroups.For(_metrics).FirstOrDefault(g => g.Key == _selected);
        foreach (var m in group?.Devices ?? _metrics) _page.Children.Add(DetailCard(m, ink, accentColour, now));
    }

    private DeviceInk InkFor(Metric m, Ink ink, Rgb? windowsAccent) =>
        DeviceColours.Resolve(_settings, m.Id, m.Value, ink.Dark, ink.Bar, ink.NeutralRgb, windowsAccent);

    private UIElement Icon(Metric m, double size, Rgb colour, Ink ink)
    {
        // The flyout always shows outline icons, whatever the taskbar uses: it is detail, not a glance.
        var icon = TileIcons.Build(m.Icon, IconStyle.Outline, size, Ink.Solid(colour), Ink.Solid(ink.Bar));
        if (icon is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        return icon ?? new Border { Width = size, Height = size };
    }

    /// <summary>A level bar: a faint track with the level filled from the left, rounded like the meters on the taskbar.</summary>
    private static UIElement LevelBar(Metric m, Rgb colour)
    {
        var level = Math.Clamp(m.Value ?? 0, 0, 100);
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 2) };
        bar.Children.Add(new Border { Background = Ink.Solid(colour, 0.25), CornerRadius = new CornerRadius(3) });
        if (m.Value is not null && level > 0)
        {
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(level, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - level, GridUnitType.Star) });
            fill.Children.Add(new Border { Background = Ink.Solid(colour), CornerRadius = new CornerRadius(3) });
            bar.Children.Add(fill);
        }
        return bar;
    }

    /// <summary>Small cards, three to a row: icon and name, a large level, a level bar and the state line.</summary>
    private UIElement SummaryGrid(IReadOnlyList<Metric> metrics, Ink ink, Rgb? windowsAccent, DateTimeOffset now)
    {
        const int perRow = 3;
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var c = 0; c < perRow; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < (metrics.Count + perRow - 1) / perRow; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < metrics.Count; i++)
        {
            var card = SummaryCard(metrics[i], ink, windowsAccent, now);
            Grid.SetColumn(card, i % perRow);
            Grid.SetRow(card, i / perRow);
            grid.Children.Add(card);
        }
        return grid;
    }

    private FrameworkElement SummaryCard(Metric m, Ink ink, Rgb? windowsAccent, DateTimeOffset now)
    {
        var colours = InkFor(m, ink, windowsAccent);
        var name = _settings.LabelFor(m.Id, m.Label);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(Icon(m, 18, colours.Icon, ink));
        var title = Cards.Primary(name, 14, semiBold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        var value = Cards.Primary(MetricFormat.Value(m), 30, semiBold: true);
        value.Foreground = Ink.Solid(colours.Accent);

        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(header);
        body.Children.Add(value);
        if (m.Value is not null) body.Children.Add(LevelBar(m, colours.Accent));
        var status = DeviceStatus.Describe(m, now, _settings.LowThreshold);
        if (status.Length > 0)
        {
            var line = Cards.Secondary(status);
            line.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(line);
        }

        return Finish(Cards.Card(body, new Thickness(12)), m, name, status);
    }

    /// <summary>One wide card for a device: title and state, then Level, State and Last seen as label-over-value columns.</summary>
    private FrameworkElement DetailCard(Metric m, Ink ink, Rgb? windowsAccent, DateTimeOffset now)
    {
        var colours = InkFor(m, ink, windowsAccent);
        var name = _settings.LabelFor(m.Id, m.Label);

        var title = new Grid { ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icon(m, 18, colours.Icon, ink);
        title.Children.Add(icon);
        var nameText = Cards.Primary(name, 14, semiBold: true);
        nameText.VerticalAlignment = VerticalAlignment.Center;
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(nameText, 1);
        title.Children.Add(nameText);
        var state = DeviceStatus.State(m, _settings.LowThreshold);
        var stateText = Cards.Secondary(state);
        stateText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stateText, 2);
        title.Children.Add(stateText);

        var seen = m.Updated is { } at ? DeviceStatus.Ago(now - at) : "--";
        var columns = new Grid { ColumnSpacing = 12 };
        for (var c = 0; c < 3; c++) columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddStat(columns, 0, "Level", MetricFormat.Value(m), colours.Accent);
        AddStat(columns, 1, "State", state, null);
        AddStat(columns, 2, "Last seen", seen, null);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(title);
        body.Children.Add(columns);
        if (m.Value is not null) body.Children.Add(LevelBar(m, colours.Accent));

        var status = DeviceStatus.Describe(m, now, _settings.LowThreshold);
        return Finish(Cards.Card(body, new Thickness(16, 12, 16, 12)), m, name, status);
    }

    private static void AddStat(Grid grid, int column, string label, string value, Rgb? colour)
    {
        var stat = new StackPanel { Spacing = 0 };
        stat.Children.Add(Cards.Secondary(label));
        var text = Cards.Primary(value, 18, semiBold: true);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        if (colour is { } c) text.Foreground = Ink.Solid(c);
        stat.Children.Add(text);
        Grid.SetColumn(stat, column);
        grid.Children.Add(stat);
    }

    private static FrameworkElement Finish(FrameworkElement card, Metric m, string name, string status)
    {
        card.Opacity = m.Stale ? 0.65 : 1.0;
        var spoken = $"{name} {MetricFormat.Value(m)}";
        AutomationProperties.SetName(card, status.Length > 0 ? $"{spoken}, {status}" : spoken);
        return card;
    }
}
