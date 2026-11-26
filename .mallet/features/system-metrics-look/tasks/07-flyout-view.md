# Task: flyout-view
Status: pending
Deps: 05, 06

## Goal
Rebuild the flyout with a System Metrics style tab strip and cards, one card per device with a level bar.

## Steps
1. In `src/Widgets.WidBar/MainPlugin.cs` set `FlyoutWidth` to 560 and `FlyoutHeight` to 520 (System Metrics' flyout is 560 wide).
2. Rewrite `src/Widgets.WidBar/FlyoutView.cs`: a root `Grid` with two rows and no background (WidBar supplies the material). Row 0 is a `SelectorBar` (`Microsoft.UI.Xaml.Controls`) with a "Summary" item plus one item per `DeviceGroups.For(metrics)` title; hide the whole strip when there is only one device kind. Row 1 is a `ScrollViewer` holding the selected page.
3. Summary page: a wrap of `Cards.Card` tiles, two per row. Each tile has the device icon (`TileIcons.Build`, Outline, 20) and name (`BatteryWidgetSettings.LabelFor`), a 35 px percentage in the device's band colour (`BatteryColors.For`, with the `ColourFor` override first), a 6 px `ProgressBar` level bar, and the `DeviceStatus.Describe` line in `{ThemeResource TextFillColorSecondaryBrush}`. Stale devices render at opacity 0.65, as today (`FlyoutView.cs:75`).
4. Kind pages: one wide card per device in that kind: a title row (icon, name, right-aligned state text such as "Charging"), then three label-over-value columns (15 px label, 22 px value): "Level", "State", "Last seen" (from `Metric.Updated`).
5. Keep the selected tab across `Refresh(...)` calls: store the selected key and fall back to Summary when its group disappears. Keep `_root.ActualThemeChanged` re-rendering for the band colours.
6. Replace the `Ink`-based text colours in the flyout with `ThemeResource` brushes via `Cards`; keep `Ink` only for the bar colours that `BatteryColors.For` needs.
7. Build, package and install as in task 04 step 4. The user opens the flyout and keeps it open (it can close when focus changes). Capture it with `winapp ui screenshot -w <hwnd>` and compare against the System Metrics flyout capture: tab underline, card radius and fill, spacing, 35 px value size.

## Notes
The gear button in System Metrics' flyout is not reproduced: `FlyoutHostWindow.ShowsSettingsButton` belongs to the host.
