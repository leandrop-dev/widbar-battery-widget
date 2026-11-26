# Task: colour-resolver
Status: done
Deps: 06

## Goal
Give the preview, the settings preview card and the flyout one tested rule for which colour a device's label, icon and value take.

## Steps
1. Create `src/Widgets.Core/Appearance/DeviceColours.cs`: `public readonly record struct DeviceInk(Rgb Label, Rgb Icon, Rgb Value, Rgb Accent)` and `public static class DeviceColours { public static DeviceInk Resolve(BatteryWidgetSettings settings, string deviceId, double? percent, bool stale, bool dark, Rgb bar, Rgb neutral, Rgb? windowsAccent) }`.
2. Rule for the device colour (`Accent`), first match wins: the user's per-device colour (`settings.ColourFor(deviceId)`); the Windows accent colour when `settings.Appearance.UseAccentColour` is true and `windowsAccent` is not null; the battery palette `BatteryColors.For(settings.Appearance, BatteryColors.BandFor(percent, settings.LowThreshold), dark, bar)` when there is a reading; `neutral` when there is none.
3. Pass the result through `Contrast.Ensure(color, bar, 4.5)` so a chosen colour stays readable on the bar colour (the same nudge the Custom palette already gets in `Custom_IsNudgedUntilReadable`).
4. `Label` and `Icon` are `Accent` when `settings.Appearance.ColouredLabelsAndIcons` is true, otherwise `neutral`. `Value` is `Accent` when `ColouredValues` is true, otherwise `neutral`.
5. Add tests in `tests/Widgets.Core.Tests/DeviceColoursTests.cs`; run `dotnet test tests/Widgets.Core.Tests`.

## TDD Checklist
- [ ] Write failing tests: a per-device colour beats the accent colour and the palette; the accent colour beats the palette; with `UseAccentColour` on but `windowsAccent` null the palette is used; no reading gives the neutral colour; `ColouredLabelsAndIcons` off makes label and icon neutral while the value follows `ColouredValues`; `ColouredValues` on makes the value the accent; a per-device colour equal to the bar colour is nudged until its contrast with the bar is at least 4.5
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions

## Notes
This is what makes "Windows accent color", "Colored labels and icons", "Colored values" and the per-device Color field work. The Windows accent is read by the caller (task 08) with `UISettings.GetColorValue(UIColorType.Accent)`; Core stays free of Windows types.
