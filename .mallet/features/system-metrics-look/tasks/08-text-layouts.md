# Task: text-layouts
Status: done
Deps: 06, 07

## Goal
Draw the Minimal text and Icon matrix layouts like System Metrics' screenshots, honouring the options that apply to them.

## Steps
1. Create `src/Widgets.WidBar/Layouts/TileContext.cs`: `internal sealed record TileContext(BatteryWidgetSettings Settings, bool Dark, double SlotHeight, Windows.UI.Color? WindowsAccent)` plus `internal interface ITileLayout { FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext context); }`. Add a static helper `TileContext.ReadWindowsAccent()` returning `new Windows.UI.ViewManagement.UISettings().GetColorValue(UIColorType.Accent)`.
2. Create `src/Widgets.WidBar/Layouts/TileParts.cs`: shared helpers used by every layout: `Ink` for a device via `DeviceColours.Resolve`, the label text (`BatteryWidgetSettings.LabelFor`), the value text (`MetricFormat.Value(m, settings.Appearance.ShowPercentSymbol)`: add the overload `MetricFormat.Value(Metric, bool percentSymbol)` in `src/Widgets.Core/MetricFormat.cs` first, with its test), the charging bolt (`FontIcon` glyph ``, shown only when `ShowChargingBolt` and `m.Charging`), the stale dimming (opacity 0.5) and the accessible name (`AutomationProperties.SetName` with label, value and `DeviceStatus.Describe`).
3. Create `src/Widgets.WidBar/Layouts/MinimalTextLayout.cs`: a horizontal `StackPanel`; per device a small bold label (9 px, `Label` colour, aligned to the top of the value, hidden when `ShowNames` is off) beside a 14 px semibold value (`Value` colour), the bolt after it; a 1 px divider between devices only when `ShowSeparators`; the gap between devices comes from `Spacing` (Compact 8, Standard 14, Relaxed 22 px). Font sizes scale with `Appearance.Scale / 100.0`, capped so a tile never exceeds the slot height.
4. Create `src/Widgets.WidBar/Layouts/IconMatrixLayout.cs`: a `Grid` of two rows; devices placed with `PreviewGrid.Cell`; each cell holds the device icon (`TileIcons.Build(m.Icon, settings.Appearance.Icons, 13, icon colour, bar brush)`; `IconStyle.Battery` uses `TileIcons.Gauge` as `TileFactory` does today; `IconStyle.None` hides it), then the 9 px bold label (hidden when `ShowNames` is off), then the 14 px bold value; column gap from `Spacing` (Compact 8, Standard 14, Relaxed 22), row gap 2.
5. Build with `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64`. Visual checking happens in task 10 once the layouts are wired.

## TDD Checklist
- [ ] Write failing test: `MetricFormat.Value(m, false)` gives "36" for a 36 % metric and leaves "--" and text values unchanged; `MetricFormat.Value(m, true)` equals the existing `MetricFormat.Value(m)`
- [ ] Confirm test fails (red)
- [ ] Implement
- [ ] Confirm test passes (green)
- [ ] Confirm no regressions

## Notes
The old `TileFactory` and its `LookMetrics`/pill code stay until task 10 removes the last use; nothing else should call it after that.
