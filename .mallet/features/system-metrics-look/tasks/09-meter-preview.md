# Task: meter-preview
Status: pending
Deps: 08

## Goal
Add a vertical-meter taskbar layout like System Metrics' preview and make the new options take effect in both layouts.

## Steps
1. In `src/Widgets.Core/MetricFormat.cs` add an overload `Value(Metric m, bool percentSymbol)` that drops the "%" when `percentSymbol` is false. Add tests in `tests/Widgets.Core.Tests/MetricFormatTests.cs` (red, implement, green).
2. Create `src/Widgets.WidBar/MeterTileFactory.cs`: one tile per device, about 50 px wide and as tall as the slot (System Metrics' tiles are 50x46 in a 48 px slot): a 5 px wide vertical meter filled bottom-up to the level in the device's colour, and a 10 px bold label in the device's colour above a 14 px semibold value. Colours come from `BatteryColors.For`, then `BatteryWidgetSettings.ColourFor`, then `UseAccentColour` (read with `new Windows.UI.ViewManagement.UISettings().GetColorValue(UIColorType.Accent)`).
3. In `src/Widgets.WidBar/PreviewBuilder.cs` (task 04) choose `MeterTileFactory` for `PreviewLayout.VerticalMeters` and the existing `TileFactory` for `PreviewLayout.Row`. Apply `Spacing` (Compact 6, Standard 10, Relaxed 16 px between tiles), `ShowSeparators` (the divider between tiles), `Alignment` (the row's `HorizontalAlignment`) and `WidthAdjustment` (added to the measured width, minimum 60).
4. Honour the remaining options in both factories (`src/Widgets.WidBar/TileFactory.cs` and `MeterTileFactory.cs`): `ShowNames` for labels with `LabelFor`, `ShowPercentSymbol`, `ColouredLabelsAndIcons`, `ColouredValues`, `ShowChargingBolt` and per-device colours.
5. Remove the hard-coded `PillStyle.Transparent` in `PreviewBuilder` only if the Row layout still looks identical to the current build; otherwise keep it.
6. Build, package and install as in task 04 step 4. Verify with a real screen capture of the taskbar taken by the user (this session cannot capture it, and window capture misses it): both layouts, light and dark, small taskbar buttons, a low and a charging device. Check that the settings window's live preview card matches the taskbar.

## TDD Checklist
- [ ] Write failing test: `MetricFormat.Value(m, false)` gives "36" for 36 %, and leaves "--" and text values unchanged
- [ ] Confirm test fails (red)
- [ ] Implement
- [ ] Confirm test passes (green)
- [ ] Confirm no regressions

## Notes
The user restyled the preview knowingly: it overrides the Transparent pill, Outline icon and Classic palette defaults chosen earlier. The Row layout keeps them available.
