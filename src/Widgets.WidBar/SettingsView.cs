using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;
using Widgets.Core;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>
/// The settings page: which devices show and in what order, the icon style, the colours, the size and the low-battery
/// level. Every change is handed to WidBar as a draft, so the taskbar follows along; WidBar stores it on Save and
/// restores the original on Cancel.
/// </summary>
internal sealed class SettingsView
{
    private readonly IWidgetSettingsContext _context;
    private readonly IReadOnlyList<Metric> _devices;
    private readonly StackPanel _deviceList = new() { Spacing = 4 };
    private readonly StackPanel _customColours = new() { Spacing = 8 };
    private readonly ScrollViewer _root;
    private BatteryWidgetSettings _draft;

    public SettingsView(IWidgetSettingsContext context, IReadOnlyList<Metric> devices)
    {
        _context = context;
        _devices = devices;
        _draft = BatteryWidgetSettings.FromJson(context.SettingsJson);

        var page = new StackPanel { Spacing = 20, Padding = new Thickness(0, 0, 12, 0) };
        page.Children.Add(Section("Devices", _deviceList, "Choose which batteries show, and the order they appear in."));
        RebuildDevices();

        var look = _draft.Appearance;
        page.Children.Add(Choice("Icons", look.Icons.ToString(),
            [("Outline", "Outline"), ("Filled", "Filled"), ("Simple", "Simple"), ("Battery gauge", "Battery"), ("None", "None")],
            tag => Change(a => a.Icons = Enum.Parse<IconStyle>(tag))));

        page.Children.Add(Choice("Battery colours", look.Palette.ToString(),
            [("Classic", "Classic"), ("Colour-blind", "ColourBlind"), ("Pastel", "Pastel"), ("High contrast", "HighContrast"), ("Mono", "Mono"), ("Custom", "Custom")],
            tag =>
            {
                Change(a => a.Palette = Enum.Parse<BatteryPalette>(tag));
                _customColours.Visibility = tag == nameof(BatteryPalette.Custom) ? Visibility.Visible : Visibility.Collapsed;
            }));
        BuildCustomColours();
        page.Children.Add(_customColours);

        page.Children.Add(Choice("Size", look.Scale.ToString(),
            [("90%", "90"), ("100%", "100"), ("115%", "115"), ("130%", "130")],
            tag => Change(a => a.Scale = int.Parse(tag))));

        var names = new ToggleSwitch { Header = "Show the name beside each value", OnContent = "On", OffContent = "Off", IsOn = look.ShowNames };
        names.Toggled += (_, _) => Change(a => a.ShowNames = names.IsOn);
        page.Children.Add(names);

        var low = new NumberBox
        {
            Header = "Flag a device as low at or below (%)",
            Minimum = 5,
            Maximum = 50,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = _draft.LowThreshold,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 220,
        };
        low.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue)) return;
            _draft.LowThreshold = (int)Math.Round(args.NewValue);
            Save();
        };
        page.Children.Add(low);

        _root = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public UIElement Root => _root;

    private void Save()
    {
        _draft = BatteryWidgetSettings.Sanitize(_draft);
        _context.SaveSettings(_draft.ToJson());
        _context.RequestPreviewRefresh(); // the width depends on every one of these settings
    }

    private void Change(Action<AppearanceSettings> change)
    {
        change(_draft.Appearance);
        Save();
    }

    private static StackPanel Section(string title, UIElement body, string? hint = null)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (hint is not null) panel.Children.Add(new TextBlock { Text = hint, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(body);
        return panel;
    }

    private static RadioButtons Choice(string header, string current, (string Label, string Tag)[] items, Action<string> picked)
    {
        var choice = new RadioButtons { Header = header, MaxColumns = 3 };
        var index = 0;
        foreach (var (label, tag) in items)
        {
            choice.Items.Add(new RadioButton { Content = label, Tag = tag });
            if (tag == current) choice.SelectedIndex = index;
            index++;
        }
        // Attached after the starting selection is set, so building the page does not count as a change.
        choice.SelectionChanged += (_, _) =>
        {
            if ((choice.SelectedItem as RadioButton)?.Tag is string tag) picked(tag);
        };
        return choice;
    }

    private void BuildCustomColours()
    {
        _customColours.Visibility = _draft.Appearance.Palette == BatteryPalette.Custom ? Visibility.Visible : Visibility.Collapsed;
        _customColours.Children.Add(new TextBlock { Text = "Custom colours (#RRGGBB)", Opacity = 0.7 });
        _customColours.Children.Add(ColourBox("High (60% and up)", _draft.Appearance.CustomHigh, (a, v) => a.CustomHigh = v));
        _customColours.Children.Add(ColourBox("Medium (30 to 59%)", _draft.Appearance.CustomMedium, (a, v) => a.CustomMedium = v));
        _customColours.Children.Add(ColourBox("Low", _draft.Appearance.CustomLow, (a, v) => a.CustomLow = v));
    }

    private TextBox ColourBox(string header, string current, Action<AppearanceSettings, string> set)
    {
        var box = new TextBox { Header = header, Text = current, MaxLength = 7, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        void Commit()
        {
            var fixedText = Hex.Normalise(box.Text.Trim(), current);
            box.Text = fixedText; // shows the correction when what was typed is not a colour
            current = fixedText;
            Change(a => set(a, fixedText));
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Commit();
        };
        return box;
    }

    private void RebuildDevices()
    {
        _deviceList.Children.Clear();
        var ids = _devices.Select(d => d.Id).ToList();
        if (ids.Count == 0)
        {
            _deviceList.Children.Add(new TextBlock { Text = "No devices found yet. Connect or wake one, then reopen this page.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            return;
        }

        var layout = _draft.AsLayout();
        var ordered = TileLayout.Ordered(ids, layout);
        for (var i = 0; i < ordered.Count; i++)
        {
            var id = ordered[i];
            var device = _devices.First(d => d.Id == id);
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var shown = new CheckBox { Content = $"{device.Label}   {MetricFormat.Value(device)}", IsChecked = !layout.Hidden.Contains(id) };
            shown.Click += (_, _) =>
            {
                _draft = _draft.WithLayout(TileLayout.SetVisible(_draft.AsLayout(), id, shown.IsChecked == true));
                Save();
            };
            row.Children.Add(shown);

            row.Children.Add(MoveButton("", $"Move {device.Label} earlier", -1, id, ids, i > 0, 1));
            row.Children.Add(MoveButton("", $"Move {device.Label} later", +1, id, ids, i < ordered.Count - 1, 2));
            _deviceList.Children.Add(row);
        }
    }

    private Button MoveButton(string glyph, string name, int direction, string id, IReadOnlyList<string> ids, bool enabled, int column)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, IsEnabled = enabled, Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(button, name);
        Grid.SetColumn(button, column);
        button.Click += (_, _) =>
        {
            _draft = _draft.WithLayout(TileLayout.Move(_draft.AsLayout(), ids, id, direction));
            Save();
            // Rebuilt after this click has finished, not from inside it: the list replaces the very button that was clicked.
            button.DispatcherQueue.TryEnqueue(RebuildDevices);
        };
        return button;
    }
}
