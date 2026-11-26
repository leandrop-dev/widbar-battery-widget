# Task: card-primitives
Status: pending
Deps: —

## Goal
Add theme-aware card, section, row and expander helpers so the settings page and flyout can share System Metrics' card look.

## Steps
1. Create `src/Widgets.WidBar/Cards.cs` with `internal static class Cards`. Build every element from `Microsoft.UI.Xaml.Markup.XamlReader.Load` strings that use `{ThemeResource ...}`, because `Application.Current.Resources` follows the app theme and not the theme the host picks for the window.
2. `Cards.Section(string title)`: a `TextBlock`, `FontWeight` SemiBold, `FontSize` 14, `Margin` 0,16,0,4, `Foreground` `{ThemeResource TextFillColorPrimaryBrush}`.
3. `Cards.Card(UIElement content)`: a `Border` with `Background` `{ThemeResource CardBackgroundFillColorDefaultBrush}`, `BorderBrush` `{ThemeResource CardStrokeColorDefaultBrush}`, `BorderThickness` 1, `CornerRadius` 8, `Padding` 16.
4. `Cards.Row(string glyph, string header, string? description, UIElement? control)`: a card of `MinHeight` 66 with a 40 px icon column (`FontIcon`, Segoe Fluent Icons, 16), a text column (header 14 px, description 12 px in `{ThemeResource TextFillColorSecondaryBrush}`), and the control right-aligned.
5. `Cards.ToggleRow(glyph, header, description, bool isOn, Action<bool> changed)`: a `Row` whose control is an "On"/"Off" `TextBlock` followed by a `ToggleSwitch` with empty On/Off content; set `AutomationProperties.Name` to the header.
6. `Cards.Expander(glyph, header, description, IEnumerable<UIElement> rows)`: a WinUI `Expander` with a `Row`-style header and the child rows inside, separated by 1 px `{ThemeResource DividerStrokeColorDefaultBrush}` lines.
7. Run `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64` and confirm 0 errors. Visual checking happens in task 04.

## Notes
No unit tests: this is XAML construction. It is exercised by the live capture in task 04.
