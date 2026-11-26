using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Widgets.WidBar;

/// <summary>
/// The building blocks of the settings page and the flyout, drawn like WidBar's System Metrics widget: a card with a
/// thin outline, rows with an icon, a header, a description and a control on the right, and expanders that group rows.
/// Every brush comes from a <c>{ThemeResource}</c> inside XAML loaded here, because
/// <c>Application.Current.Resources</c> follows the app's theme and not the one WidBar picks for the window.
/// </summary>
internal static class Cards
{
    private const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
    private const double IconColumn = 40;
    private const double RowHeight = 66;

    private static T Themed<T>(string xaml) where T : class => (T)XamlReader.Load(xaml);

    /// <summary>A section heading such as "Presentation".</summary>
    public static TextBlock Section(string title)
    {
        var heading = Themed<TextBlock>(
            $"<TextBlock {Ns} FontSize='14' FontWeight='SemiBold' Margin='0,16,0,4' Foreground='{{ThemeResource TextFillColorPrimaryBrush}}'/>");
        heading.Text = title;
        AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        return heading;
    }

    /// <summary>Secondary text: a description under a header, or a note.</summary>
    public static TextBlock Secondary(string text, double size = 12)
    {
        var block = Themed<TextBlock>(
            $"<TextBlock {Ns} TextWrapping='Wrap' Foreground='{{ThemeResource TextFillColorSecondaryBrush}}'/>");
        block.Text = text;
        block.FontSize = size;
        return block;
    }

    /// <summary>Primary text of the given size and weight.</summary>
    public static TextBlock Primary(string text, double size = 14, bool semiBold = false)
    {
        var block = Themed<TextBlock>(
            $"<TextBlock {Ns} TextWrapping='Wrap' Foreground='{{ThemeResource TextFillColorPrimaryBrush}}'/>");
        block.Text = text;
        block.FontSize = size;
        if (semiBold) block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        return block;
    }

    /// <summary>A rounded card with a thin outline around <paramref name="content"/>.</summary>
    public static Border Card(UIElement content, Thickness? padding = null)
    {
        var card = Themed<Border>(
            $"<Border {Ns} CornerRadius='8' BorderThickness='1' Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}'/>");
        card.Padding = padding ?? new Thickness(16);
        card.Child = content;
        return card;
    }

    /// <summary>The inside of a row: icon, header with an optional description, and the control on the right.</summary>
    private static Grid RowContent(string glyph, string header, string? description, UIElement? control)
    {
        var grid = new Grid { MinHeight = RowHeight, ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (glyph.Length > 0)
        {
            var icon = Themed<FontIcon>(
                $"<FontIcon {Ns} FontSize='16' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' Foreground='{{ThemeResource TextFillColorPrimaryBrush}}'/>");
            icon.Glyph = glyph;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            grid.Children.Add(icon);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Primary(header));
        if (!string.IsNullOrEmpty(description)) text.Children.Add(Secondary(description));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(fe, 2);
            grid.Children.Add(fe);
        }
        return grid;
    }

    /// <summary>A full-width card holding one setting: icon, header, description and a control.</summary>
    public static Border Row(string glyph, string header, string? description, UIElement? control) =>
        Card(RowContent(glyph, header, description, control), new Thickness(16, 0, 16, 0));

    /// <summary>A row with an "On"/"Off" label and a switch. <paramref name="changed"/> runs when the user flips it.</summary>
    public static Border ToggleRow(string glyph, string header, string? description, bool isOn, Action<bool> changed) =>
        Row(glyph, header, description, Toggle(header, isOn, changed));

    /// <summary>The "On"/"Off" label with its switch, for use as a row's control or inside an expander's header.</summary>
    public static StackPanel Toggle(string name, bool isOn, Action<bool> changed)
    {
        var state = Primary(isOn ? "On" : "Off");
        state.VerticalAlignment = VerticalAlignment.Center;

        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, IsOn = isOn };
        AutomationProperties.SetName(toggle, name);
        // Attached after the starting state is set, so building the row does not count as a change.
        toggle.Toggled += (_, _) =>
        {
            state.Text = toggle.IsOn ? "On" : "Off";
            changed(toggle.IsOn);
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(state);
        panel.Children.Add(toggle);
        return panel;
    }

    /// <summary>
    /// A group of rows behind a header that opens and closes. <paramref name="headerControl"/> sits at the right of the
    /// header (a switch, or nothing); <paramref name="rows"/> are the rows inside, separated by thin lines.
    /// </summary>
    public static Expander Expander(string glyph, string header, string? description, IEnumerable<UIElement> rows, UIElement? headerControl = null)
    {
        var expander = Themed<Expander>(
            $"<Expander {Ns} HorizontalAlignment='Stretch' HorizontalContentAlignment='Stretch' CornerRadius='8' Padding='0' Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}'/>");
        expander.Header = RowContent(glyph, header, description, headerControl);
        AutomationProperties.SetName(expander, header);

        var body = new StackPanel();
        foreach (var row in rows)
        {
            var line = Themed<Border>(
                $"<Border {Ns} BorderThickness='0,1,0,0' BorderBrush='{{ThemeResource DividerStrokeColorDefaultBrush}}' Padding='56,10,16,10' MinHeight='52'/>");
            line.Child = row;
            body.Children.Add(line);
        }
        expander.Content = body;
        return expander;
    }

    /// <summary>A labelled line inside an expander: the label on the left, the control on the right.</summary>
    public static Grid Field(string label, UIElement control, string? description = null)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Primary(label));
        if (!string.IsNullOrEmpty(description)) text.Children.Add(Secondary(description));
        grid.Children.Add(text);

        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(fe, 1);
            AutomationProperties.SetName(fe, label);
            grid.Children.Add(fe);
        }
        return grid;
    }
}
