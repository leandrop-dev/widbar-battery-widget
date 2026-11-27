using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidBar.SDK;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;
using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.WidBar;

/// <summary>
/// The settings page, laid out like WidBar's System Metrics page: a live preview card, then rows and expanders under
/// "Presentation", "Devices and colors" and "Data refresh". Every change is handed to WidBar as a draft, so the
/// taskbar follows along; WidBar stores it on Save and restores the original on Cancel.
/// </summary>
internal sealed class SettingsView
{
    private const double PageWidth = 1056;
    private readonly double _slotHeight; // the taskbar's real slot, so the card draws what the taskbar draws

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
    private readonly List<(PreviewOption Option, FrameworkElement Row, string? Normal)> _optionRows = [];
    private (string Name, int Direction)? _refocus;
    private readonly ScrollViewer _root;
    private Expander? _customColours;
    private BatteryWidgetSettings _draft;

    public SettingsView(IWidgetSettingsContext context, IReadOnlyList<Metric> devices, double slotHeight)
    {
        _context = context;
        _devices = devices;
        _slotHeight = slotHeight;
        _draft = BatteryWidgetSettings.FromJson(context.SettingsJson);

        var page = new StackPanel { Spacing = 6, MaxWidth = PageWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 0, 0, 12) };
        page.Children.Add(PreviewCard());
        BuildPresentation(page);
        ApplyLayoutSupport();
        BuildDevicesAndColours(page);
        BuildDataRefresh(page);
        BuildHelp(page);
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
        ApplyLayoutSupport(); // what a setting makes pointless depends on the other settings
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
        var now = DateTimeOffset.UtcNow;
        var present = _devices.Where(m => DevicePresence.IsShown(m, now, _draft.HideAfter)).ToList();
        var shown = TileLayout.Apply(present, _draft.AsLayout());
        var width = PreviewBuilder.Fill(_previewRow, _draft, shown, _slotHeight, _root.ActualTheme == ElementTheme.Dark);
        _previewWidth.Text = $"{width} px";
    }

    private void BuildPresentation(StackPanel page)
    {
        var look = _draft.Appearance;
        page.Children.Add(Cards.Section("Presentation"));

        page.Children.Add(Cards.Row("", "Layout", null,
            Combo("Layout",
                [("Vertical meters", nameof(PreviewLayout.VerticalMeters)), ("Minimal text", nameof(PreviewLayout.MinimalText)),
                    ("Minimal icons", nameof(PreviewLayout.MinimalIcons))],
                look.Layout.ToString(), tag => { Change(a => a.Layout = Enum.Parse<PreviewLayout>(tag)); ApplyLayoutSupport(); })));

        page.Children.Add(Track(PreviewOption.Icons, "Icon in front of each value", Cards.Row("", "Icons", null,
            Combo("Icons", [("Outline", nameof(IconStyle.Outline)), ("Filled", nameof(IconStyle.Filled)), ("Simple", nameof(IconStyle.Simple)),
                    ("Battery gauge", nameof(IconStyle.Battery)), ("None", nameof(IconStyle.None))],
                look.Icons.ToString(), tag => Change(a => a.Icons = Enum.Parse<IconStyle>(tag))))));

        page.Children.Add(Cards.Expander("", "Size and spacing", "Value size, spacing, alignment and width",
        [
            Track(PreviewOption.ValueSize, null, Cards.Field("Value size", Combo("Value size",
                [("Small", "90"), ("Medium", "100"), ("Large", "115"), ("Extra large", "130")],
                look.Scale.ToString(), tag => Change(a => a.Scale = int.Parse(tag))))),
            Track(PreviewOption.Spacing, null, Cards.Field("Spacing", Combo("Spacing",
                [("Compact", nameof(TileSpacing.Compact)), ("Standard", nameof(TileSpacing.Standard)), ("Relaxed", nameof(TileSpacing.Relaxed))],
                look.Spacing.ToString(), tag => Change(a => a.Spacing = Enum.Parse<TileSpacing>(tag))))),
            Track(PreviewOption.Alignment, null, Cards.Field("Alignment", Combo("Alignment",
                [("Left", nameof(ContentAlignment.Left)), ("Center", nameof(ContentAlignment.Center)), ("Right", nameof(ContentAlignment.Right))],
                look.Alignment.ToString(), tag => Change(a => a.Alignment = Enum.Parse<ContentAlignment>(tag))))),
            Cards.Field("Preview width adjustment",
                Number("Preview width adjustment", -120, 400, look.WidthAdjustment, 10, v => Change(a => a.WidthAdjustment = v)),
                "Widen or narrow the taskbar widget. 0 keeps the automatic width."),
        ]));

        page.Children.Add(Cards.Expander("", "Labels and colors", "What appears next to each value, and in which color",
        [
            Track(PreviewOption.MetricLabels, null, Cards.Field("Metric labels", Cards.Toggle("Metric labels", look.ShowNames, on => Change(a => a.ShowNames = on)))),
            Track(PreviewOption.PercentSymbol, "Show %", Cards.Field("Percent symbol", Cards.Toggle("Percent symbol", look.ShowPercentSymbol, on => Change(a => a.ShowPercentSymbol = on)), "Show %")),
            Track(PreviewOption.Separators, null, Cards.Field("Metric separators", Cards.Toggle("Metric separators", look.ShowSeparators, on => Change(a => a.ShowSeparators = on)))),
            Track(PreviewOption.ColouredLabelsAndIcons, "Use device color", Cards.Field("Colored labels and icons", Cards.Toggle("Colored labels and icons", look.ColouredLabelsAndIcons, on => Change(a => a.ColouredLabelsAndIcons = on)), "Use device color")),
            Track(PreviewOption.ColouredValues, "Off keeps neutral text", Cards.Field("Colored values", Cards.Toggle("Colored values", look.ColouredValues, on => Change(a => a.ColouredValues = on)), "Off keeps neutral text")),
            Track(PreviewOption.ChargingBolt, "Show the charging bolt", Cards.Field("Activity indicators", Cards.Toggle("Activity indicators", look.ShowChargingBolt, on => Change(a => a.ShowChargingBolt = on)), "Show the charging bolt")),
        ]));
    }

    /// <summary>Remembers an option's row so <see cref="ApplyLayoutSupport"/> can grey it out; returns the row.</summary>
    private T Track<T>(PreviewOption option, string? normalDescription, T row) where T : FrameworkElement
    {
        _optionRows.Add((option, row, normalDescription));
        return row;
    }

    /// <summary>Greys out, and says so, the options the chosen layout does not draw, so no switch quietly does nothing.</summary>
    private void ApplyLayoutSupport()
    {
        foreach (var (option, row, normal) in _optionRows)
        {
            var reason = LayoutSupport.Unused(_draft.Appearance, option);
            var used = reason is null;
            foreach (var control in Cards.Descendants(row).OfType<Control>()) control.IsEnabled = used;
            row.Opacity = used ? 1 : 0.5;
            Cards.SetDescription(row, reason ?? normal);
        }
    }

    private void BuildDevicesAndColours(StackPanel page)
    {
        var look = _draft.Appearance;
        page.Children.Add(Cards.Section("Devices and colors"));

        page.Children.Add(Cards.ToggleRow("", "Windows accent color", "One color for every device instead of the battery colors",
            look.UseAccentColour, on => Change(a => a.UseAccentColour = on)));

        page.Children.Add(Track(PreviewOption.BatteryColours, "How a level is colored: high, medium and low", Cards.Row("", "Battery colors", "How a level is colored: high, medium and low",
            Combo("Battery colors",
                [("Classic", nameof(BatteryPalette.Classic)), ("Colour-blind", nameof(BatteryPalette.ColourBlind)), ("Pastel", nameof(BatteryPalette.Pastel)),
                    ("High contrast", nameof(BatteryPalette.HighContrast)), ("Mono", nameof(BatteryPalette.Mono)), ("Custom", nameof(BatteryPalette.Custom))],
                look.Palette.ToString(),
                tag =>
                {
                    Change(a => a.Palette = Enum.Parse<BatteryPalette>(tag));
                    if (_customColours is not null) _customColours.Visibility = tag == nameof(BatteryPalette.Custom) ? Visibility.Visible : Visibility.Collapsed;
                }))));

        _customColours = Cards.Expander("", "Custom colors", "#RRGGBB for each level",
        [
            ColourField("High (60% and up)", _draft.Appearance.CustomHigh, (a, v) => a.CustomHigh = v),
            ColourField("Medium (30 to 59%)", _draft.Appearance.CustomMedium, (a, v) => a.CustomMedium = v),
            ColourField("Low", _draft.Appearance.CustomLow, (a, v) => a.CustomLow = v),
        ]);
        _customColours.Visibility = look.Palette == BatteryPalette.Custom ? Visibility.Visible : Visibility.Collapsed;
        page.Children.Add(Track(PreviewOption.BatteryColours, "#RRGGBB for each level", _customColours));

        page.Children.Add(Cards.Row("", "Low battery", "Flag a device at or below this level (%)",
            Number("Flag a device as low at or below (%)", 5, 50, _draft.LowThreshold, 5, v => ChangeSettings(s => s.LowThreshold = v))));

        page.Children.Add(Cards.ToggleRow("", "Low battery alerts", "Show a Windows notification once when a device reaches the low level, and again only after it has charged. Clicking it does nothing.",
            _draft.LowBatteryAlerts, on => ChangeSettings(s => s.LowBatteryAlerts = on)));

        BuildHideDisconnected(page);
        page.Children.Add(_deviceList);
        RebuildDevices();
    }

    private void BuildDataRefresh(StackPanel page)
    {
        page.Children.Add(Cards.Section("Data refresh"));
        page.Children.Add(Cards.Row("", "Refresh interval", "A slower interval reduces background activity. Minutes, 1 to 10. With several copies placed, the fastest one sets the pace.",
            Number("Refresh interval (minutes)", 1, 10, _draft.RefreshMinutes, 1, v => ChangeSettings(s => s.RefreshMinutes = v))));
        page.Children.Add(Cards.ToggleRow("", "PlayStation controllers over Bluetooth",
            "Read the battery of a DualSense or DualShock 4 connected by Bluetooth. To do this the widget asks the pad for a report that switches it to its extended report mode until it reconnects. A pad on a cable is always read.",
            _draft.ReadDualSenseBluetooth, on => ChangeSettings(s => s.ReadDualSenseBluetooth = on)));

        page.Children.Add(Cards.ToggleRow("", "Experimental device readers",
            "Also read devices whose support is unconfirmed, even in the project it was ported from. They send only fixed status requests, but a wrong guess for your model can show a wrong level or none.",
            _draft.ExperimentalReaders, on => ChangeSettings(s => s.ExperimentalReaders = on)));
    }

    private void BuildHelp(StackPanel page)
    {
        page.Children.Add(Cards.Section("Help"));

        var open = new Button { Content = "Open GitHub issue" };
        var copy = new Button { Content = "Copy report" };
        var status = Cards.Secondary("");
        AutomationProperties.SetName(open, "Open a GitHub issue to request support for a device");
        AutomationProperties.SetName(copy, "Copy the device report to the clipboard");
        open.Click += async (_, _) => await RequestDeviceAsync(open, copy, status, launch: true);
        copy.Click += async (_, _) => await RequestDeviceAsync(open, copy, status, launch: false);

        var controls = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, copy } });
        controls.Children.Add(status);
        page.Children.Add(Cards.Row("", "Request support for a device",
            "Lists the devices Windows sees (vendor and product ids and names, no serial numbers or addresses) in a new GitHub issue. Nothing is sent until you submit it in your browser. Copy report puts the same text on the clipboard.",
            controls));
    }

    private static async Task RequestDeviceAsync(Button open, Button copy, TextBlock status, bool launch)
    {
        open.IsEnabled = copy.IsEnabled = false;
        status.Text = "Looking for devices...";
        try
        {
            // Opening every HID interface to read its name is slow enough to keep off the UI thread.
            var report = await Task.Run(() => DeviceReport.Text(
                DeviceReport.Group(new WindowsHid().EnumerateAll()), WidgetVersion(), Environment.OSVersion.Version.ToString()));

            if (launch)
            {
                var opened = await Windows.System.Launcher.LaunchUriAsync(DeviceReport.IssueUri(report));
                status.Text = opened ? "Opened in your browser." : "Could not open the browser. Use Copy report instead.";
            }
            else
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(report);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                status.Text = "Copied. Paste it into the issue.";
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            status.Text = "That did not work. Try again.";
        }
        finally
        {
            open.IsEnabled = copy.IsEnabled = true;
        }
    }

    private static string WidgetVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return "unknown"; // not running as a package
        }
    }

    private void BuildHideDisconnected(StackPanel page)
    {
        page.Children.Add(Cards.Row("", "Hide disconnected devices", "A device that has been disconnected this many minutes leaves the taskbar and comes back when it reports again. 0 keeps it, dimmed. Checked at each refresh.",
            Number("Hide a disconnected device after (minutes, 0 = never)", 0, 1440, _draft.HideAfterMinutes, 5,
                v => { ChangeSettings(s => s.HideAfterMinutes = v); RebuildDevices(); })));
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
            if (string.Equals(fixedText, current, StringComparison.Ordinal)) return; // unchanged: nothing to save
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

            Expander? expander = null;
            var why = DevicePresence.WhyHidden(device, DateTimeOffset.UtcNow, _draft.HideAfter);
            expander = Cards.Expander(GlyphFor(device.Icon), _draft.LabelFor(id, device.Label),
                why is null ? MetricFormat.Value(device) : $"{MetricFormat.Value(device)} · {why}",
            [
                Cards.Field("Label", LabelBox(id, device.Label, name => Cards.SetHeader(expander!, name))),
                Cards.Field("Color", ColourBox(id, device.Label)),
                Cards.Field("Order", MoveButtons(id, device.Label, ids, i > 0, i < ordered.Count - 1)),
            ], shown);
            expander.IsExpanded = _openDevices.Contains(id);
            expander.Expanding += (_, _) => _openDevices.Add(id);
            expander.Collapsed += (_, _) => _openDevices.Remove(id);
            _deviceList.Children.Add(expander);
        }
    }

    private TextBox LabelBox(string id, string deviceName, Action<string> nameChanged)
    {
        var box = new TextBox
        {
            Text = _draft.Labels.TryGetValue(id, out var label) ? label : "",
            PlaceholderText = deviceName,
            MaxLength = BatteryWidgetSettings.MaxLabelLength,
            Width = 200,
        };
        AutomationProperties.SetName(box, $"Label for {deviceName}");
        void Commit()
        {
            var text = box.Text.Trim();
            _draft.Labels.TryGetValue(id, out var stored);
            if (text == (stored ?? "")) return; // unchanged: tabbing through the box must not save
            ChangeSettings(s =>
            {
                if (text.Length == 0) s.Labels.Remove(id);
                else s.Labels[id] = text;
            });
            nameChanged(_draft.LabelFor(id, deviceName)); // the header shows the name the taskbar now uses
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Commit();
        };
        return box;
    }

    private UIElement ColourBox(string id, string deviceName)
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
        AutomationProperties.SetName(box, $"Color for {deviceName}");

        void Paint() => swatch.Background = Hex.TryParse(box.Text, out var c) ? Ink.Solid(c) : null;
        void Commit()
        {
            var text = box.Text.Trim();
            _draft.Colours.TryGetValue(id, out var stored);
            if (text.Length == 0 || !Hex.TryParse(text, out var c))
            {
                box.Text = ""; // blank, or not a colour: back to the palette
                if (stored is not null) ChangeSettings(s => s.Colours.Remove(id));
            }
            else
            {
                box.Text = c.ToHex();
                if (!string.Equals(stored, c.ToHex(), StringComparison.Ordinal)) ChangeSettings(s => s.Colours[id] = c.ToHex());
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

    /// <summary>
    /// After a device moved, puts the keyboard focus back on its Move button (the other direction's when it can no longer
    /// move that way), because the list was rebuilt and the button that had focus is gone.
    /// </summary>
    private void RestoreMoveFocus()
    {
        if (_refocus is not { } wanted) return;
        _refocus = null;
        var buttons = Cards.Descendants(_deviceList).OfType<Button>().ToList();
        var same = buttons.FirstOrDefault(b => AutomationProperties.GetName(b) == wanted.Name);
        if (same is { IsEnabled: true }) { same.Focus(FocusState.Keyboard); return; }
        var other = wanted.Name.EndsWith("earlier", StringComparison.Ordinal)
            ? wanted.Name[..^"earlier".Length] + "later"
            : wanted.Name[..^"later".Length] + "earlier";
        buttons.FirstOrDefault(b => AutomationProperties.GetName(b) == other && b.IsEnabled)?.Focus(FocusState.Keyboard);
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
            _refocus = (name, direction);
            button.DispatcherQueue.TryEnqueue(() =>
            {
                RebuildDevices();
                button.DispatcherQueue.TryEnqueue(RestoreMoveFocus); // once the new buttons are in place
            });
        };
        return button;
    }
}
