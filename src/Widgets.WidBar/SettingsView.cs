using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidBar.SDK;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>
/// The settings page, laid out like WidBar's System Metrics page: a live preview card, then rows and expanders under
/// "Presentation", "Devices and colors" and "Data refresh". Every change is handed to WidBar as a draft, so the
/// taskbar follows along; WidBar stores it on Save and restores the original on Cancel.
/// </summary>
internal sealed class SettingsView
{
    private const double PageWidth = 1056;
    private const double SlotHeight = 48; // the preview card draws the taskbar's normal height

    private readonly IWidgetSettingsContext _context;
    private readonly IReadOnlyList<Metric> _devices;
    private readonly StackPanel _previewRow = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _previewWidth = Cards.Secondary("");
    private readonly StackPanel _deviceList = new() { Spacing = 6 };
    private readonly HashSet<string> _openDevices = new(StringComparer.Ordinal);
    private readonly ScrollViewer _root;
    private Expander? _customColours;
    private BatteryWidgetSettings _draft;

    public SettingsView(IWidgetSettingsContext context, IReadOnlyList<Metric> devices)
    {
        _context = context;
        _devices = devices;
        _draft = BatteryWidgetSettings.FromJson(context.SettingsJson);

        var page = new StackPanel { Spacing = 6, MaxWidth = PageWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 0, 0, 12) };
        page.Children.Add(PreviewCard());
        BuildPresentation(page);
        BuildDevicesAndColours(page);
        BuildDataRefresh(page);
        page.Children.Add(Cards.Secondary("Changes are previewed live and saved only for this widget instance."));

        _root = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _root.Loaded += (_, _) => RefreshPreview();
        _root.ActualThemeChanged += (_, _) => RefreshPreview();
    }

    public UIElement Root => _root;

    // ---- saving

    private void Save()
    {
        _draft = BatteryWidgetSettings.Sanitize(_draft);
        _context.SaveSettings(_draft.ToJson());
        _context.RequestPreviewRefresh(); // the width depends on every one of these settings
        RefreshPreview();
    }

    private void Change(Action<AppearanceSettings> change)
    {
        change(_draft.Appearance);
        Save();
    }

    private void ChangeSettings(Action<BatteryWidgetSettings> change)
    {
        change(_draft);
        Save();
    }

    // ---- the page

    private UIElement PreviewCard()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Cards.Primary("Live preview", 14, semiBold: true));
        _previewWidth.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_previewWidth, 1);
        header.Children.Add(_previewWidth);

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(header);
        body.Children.Add(Cards.Well(_previewRow));
        return Cards.Card(body);
    }

    private void RefreshPreview()
    {
        var shown = TileLayout.Apply(_devices, _draft.AsLayout());
        var width = PreviewBuilder.Fill(_previewRow, _draft, shown, SlotHeight, _root.ActualTheme == ElementTheme.Dark);
        _previewWidth.Text = $"{width} px - fits content";
    }

    private void BuildPresentation(StackPanel page)
    {
        var look = _draft.Appearance;
        page.Children.Add(Cards.Section("Presentation"));

        page.Children.Add(Cards.Row("", "Layout", null,
            Combo("Layout", [("Vertical meters", nameof(PreviewLayout.VerticalMeters)), ("Row", nameof(PreviewLayout.Row))],
                look.Layout.ToString(), tag => Change(a => a.Layout = Enum.Parse<PreviewLayout>(tag)))));

        page.Children.Add(Cards.Row("", "Icons", "Used by the Row layout",
            Combo("Icons", [("Outline", nameof(IconStyle.Outline)), ("Filled", nameof(IconStyle.Filled)), ("Simple", nameof(IconStyle.Simple)),
                    ("Battery gauge", nameof(IconStyle.Battery)), ("None", nameof(IconStyle.None))],
                look.Icons.ToString(), tag => Change(a => a.Icons = Enum.Parse<IconStyle>(tag)))));

        page.Children.Add(Cards.Expander("", "Size and spacing", "Value size, spacing, alignment and width",
        [
            Cards.Field("Value size", Combo("Value size",
                [("Small", "90"), ("Medium", "100"), ("Large", "115"), ("Extra large", "130")],
                look.Scale.ToString(), tag => Change(a => a.Scale = int.Parse(tag)))),
            Cards.Field("Spacing", Combo("Spacing",
                [("Compact", nameof(TileSpacing.Compact)), ("Standard", nameof(TileSpacing.Standard)), ("Relaxed", nameof(TileSpacing.Relaxed))],
                look.Spacing.ToString(), tag => Change(a => a.Spacing = Enum.Parse<TileSpacing>(tag)))),
            Cards.Field("Alignment", Combo("Alignment",
                [("Left", nameof(ContentAlignment.Left)), ("Center", nameof(ContentAlignment.Center)), ("Right", nameof(ContentAlignment.Right))],
                look.Alignment.ToString(), tag => Change(a => a.Alignment = Enum.Parse<ContentAlignment>(tag)))),
            Cards.Field("Preview width adjustment",
                Number("Preview width adjustment", -120, 400, look.WidthAdjustment, 10, v => Change(a => a.WidthAdjustment = v)),
                "Widen or narrow the taskbar widget. 0 keeps the automatic width."),
        ]));

        page.Children.Add(Cards.Expander("", "Labels and colors", "What appears next to each value, and in which color",
        [
            Cards.Field("Metric labels", Cards.Toggle("Metric labels", look.ShowNames, on => Change(a => a.ShowNames = on))),
            Cards.Field("Percent symbol", Cards.Toggle("Percent symbol", look.ShowPercentSymbol, on => Change(a => a.ShowPercentSymbol = on)), "Show %"),
            Cards.Field("Metric separators", Cards.Toggle("Metric separators", look.ShowSeparators, on => Change(a => a.ShowSeparators = on))),
            Cards.Field("Colored labels and icons", Cards.Toggle("Colored labels and icons", look.ColouredLabelsAndIcons, on => Change(a => a.ColouredLabelsAndIcons = on)), "Use device color"),
            Cards.Field("Colored values", Cards.Toggle("Colored values", look.ColouredValues, on => Change(a => a.ColouredValues = on)), "Off keeps neutral text"),
            Cards.Field("Activity indicators", Cards.Toggle("Activity indicators", look.ShowChargingBolt, on => Change(a => a.ShowChargingBolt = on)), "Show the charging bolt"),
        ]));
    }

    private void BuildDevicesAndColours(StackPanel page)
    {
        var look = _draft.Appearance;
        page.Children.Add(Cards.Section("Devices and colors"));

        page.Children.Add(Cards.ToggleRow("", "Windows accent color", "One color for every device instead of the battery colors",
            look.UseAccentColour, on => Change(a => a.UseAccentColour = on)));

        page.Children.Add(Cards.Row("", "Battery colors", "How a level is colored: high, medium and low",
            Combo("Battery colors",
                [("Classic", nameof(BatteryPalette.Classic)), ("Colour-blind", nameof(BatteryPalette.ColourBlind)), ("Pastel", nameof(BatteryPalette.Pastel)),
                    ("High contrast", nameof(BatteryPalette.HighContrast)), ("Mono", nameof(BatteryPalette.Mono)), ("Custom", nameof(BatteryPalette.Custom))],
                look.Palette.ToString(),
                tag =>
                {
                    Change(a => a.Palette = Enum.Parse<BatteryPalette>(tag));
                    if (_customColours is not null) _customColours.Visibility = tag == nameof(BatteryPalette.Custom) ? Visibility.Visible : Visibility.Collapsed;
                })));

        _customColours = Cards.Expander("", "Custom colors", "#RRGGBB for each level",
        [
            ColourField("High (60% and up)", _draft.Appearance.CustomHigh, (a, v) => a.CustomHigh = v),
            ColourField("Medium (30 to 59%)", _draft.Appearance.CustomMedium, (a, v) => a.CustomMedium = v),
            ColourField("Low", _draft.Appearance.CustomLow, (a, v) => a.CustomLow = v),
        ]);
        _customColours.Visibility = look.Palette == BatteryPalette.Custom ? Visibility.Visible : Visibility.Collapsed;
        page.Children.Add(_customColours);

        page.Children.Add(Cards.Row("", "Low battery", "Flag a device at or below this level (%)",
            Number("Flag a device as low at or below (%)", 5, 50, _draft.LowThreshold, 5, v => ChangeSettings(s => s.LowThreshold = v))));

        page.Children.Add(_deviceList);
        RebuildDevices();
    }

    private void BuildDataRefresh(StackPanel page)
    {
        page.Children.Add(Cards.Section("Data refresh"));
        page.Children.Add(Cards.Row("", "Refresh interval", "A slower interval reduces background activity. Minutes, 1 to 10.",
            Number("Refresh interval (minutes)", 1, 10, _draft.RefreshMinutes, 1, v => ChangeSettings(s => s.RefreshMinutes = v))));
    }

    // ---- controls

    private static ComboBox Combo(string name, (string Label, string Tag)[] items, string current, Action<string> picked)
    {
        var box = new ComboBox { Width = 208 };
        AutomationProperties.SetName(box, name);
        var selected = 0;
        for (var i = 0; i < items.Length; i++)
        {
            box.Items.Add(new ComboBoxItem { Content = items[i].Label, Tag = items[i].Tag });
            if (items[i].Tag == current) selected = i;
        }
        box.SelectedIndex = selected;
        // Attached after the starting selection is set, so building the page does not count as a change.
        box.SelectionChanged += (_, _) =>
        {
            if ((box.SelectedItem as ComboBoxItem)?.Tag is string tag) picked(tag);
        };
        return box;
    }

    private static NumberBox Number(string name, int min, int max, int value, int step, Action<int> changed)
    {
        var box = new NumberBox
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = Math.Clamp(value, min, max),
            Width = 160,
        };
        AutomationProperties.SetName(box, name);
        box.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue)) return;
            changed((int)Math.Round(args.NewValue));
        };
        return box;
    }

    private UIElement ColourField(string label, string current, Action<AppearanceSettings, string> set)
    {
        var box = new TextBox { Text = current, MaxLength = 7, Width = 120 };
        AutomationProperties.SetName(box, label);
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
        return Cards.Field(label, box);
    }

    // ---- devices

    private static string GlyphFor(string? icon) => icon switch
    {
        MetricIcons.Headset => "",
        MetricIcons.Mouse => "",
        MetricIcons.Keyboard => "",
        MetricIcons.Gamepad => "",
        _ => "",
    };

    private void RebuildDevices()
    {
        _deviceList.Children.Clear();
        var ids = _devices.Select(d => d.Id).ToList();
        if (ids.Count == 0)
        {
            _deviceList.Children.Add(Cards.Card(Cards.Secondary("No devices found yet. Connect or wake one, then reopen this page.", 14)));
            return;
        }

        var layout = _draft.AsLayout();
        var ordered = TileLayout.Ordered(ids, layout);
        for (var i = 0; i < ordered.Count; i++)
        {
            var id = ordered[i];
            var device = _devices.First(d => d.Id == id);
            var shown = Cards.Toggle($"Show {device.Label}", !layout.Hidden.Contains(id), on =>
            {
                _draft = _draft.WithLayout(TileLayout.SetVisible(_draft.AsLayout(), id, on));
                Save();
            });

            var expander = Cards.Expander(GlyphFor(device.Icon), device.Label, MetricFormat.Value(device),
            [
                Cards.Field("Label", LabelBox(id, device.Label)),
                Cards.Field("Color", ColourBox(id)),
                Cards.Field("Order", MoveButtons(id, device.Label, ids, i > 0, i < ordered.Count - 1)),
            ], shown);
            expander.IsExpanded = _openDevices.Contains(id);
            expander.Expanding += (_, _) => _openDevices.Add(id);
            expander.Collapsed += (_, _) => _openDevices.Remove(id);
            _deviceList.Children.Add(expander);
        }
    }

    private TextBox LabelBox(string id, string deviceName)
    {
        var box = new TextBox
        {
            Text = _draft.Labels.TryGetValue(id, out var label) ? label : "",
            PlaceholderText = deviceName,
            MaxLength = BatteryWidgetSettings.MaxLabelLength,
            Width = 200,
        };
        AutomationProperties.SetName(box, $"Label for {deviceName}");
        void Commit() => ChangeSettings(s =>
        {
            var text = box.Text.Trim();
            if (text.Length == 0) s.Labels.Remove(id);
            else s.Labels[id] = text;
        });
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Commit();
        };
        return box;
    }

    private UIElement ColourBox(string id)
    {
        var swatch = new Microsoft.UI.Xaml.Controls.Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(swatch, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var box = new TextBox
        {
            Text = _draft.Colours.TryGetValue(id, out var hex) ? hex : "",
            PlaceholderText = "#RRGGBB",
            MaxLength = 7,
            Width = 120,
        };
        AutomationProperties.SetName(box, "Color");

        void Paint() => swatch.Background = Hex.TryParse(box.Text, out var c) ? Ink.Solid(c) : null;
        void Commit()
        {
            var text = box.Text.Trim();
            if (text.Length == 0 || !Hex.TryParse(text, out var c))
            {
                box.Text = ""; // blank, or not a colour: back to the palette
                ChangeSettings(s => s.Colours.Remove(id));
            }
            else
            {
                box.Text = c.ToHex();
                ChangeSettings(s => s.Colours[id] = c.ToHex());
            }
            Paint();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Commit();
        };
        Paint();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(swatch);
        row.Children.Add(box);
        return row;
    }

    private UIElement MoveButtons(string id, string name, IReadOnlyList<string> ids, bool canMoveEarlier, bool canMoveLater)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(MoveButton("", $"Move {name} earlier", -1, id, ids, canMoveEarlier));
        row.Children.Add(MoveButton("", $"Move {name} later", +1, id, ids, canMoveLater));
        return row;
    }

    private Button MoveButton(string glyph, string name, int direction, string id, IReadOnlyList<string> ids, bool enabled)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, IsEnabled = enabled, Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(button, name);
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
