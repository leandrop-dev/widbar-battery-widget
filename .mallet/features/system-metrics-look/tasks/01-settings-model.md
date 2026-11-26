# Task: settings-model
Status: done
Deps: —

## Goal
Add the System Metrics style options to the battery settings model with defaults, sanitising and per-device overrides.

## Steps
1. In `src/Widgets.Core/Settings/AppearanceSettings.cs` add enums `PreviewLayout { VerticalMeters, Row }`, `TileSpacing { Compact, Standard, Relaxed }` and `ContentAlignment { Left, Center, Right }`.
2. In the same file add properties to `AppearanceSettings` (each enum with `[JsonConverter(typeof(JsonStringEnumConverter))]`): `Layout = VerticalMeters`, `Spacing = Standard`, `Alignment = Center`, `WidthAdjustment = 0` (int, clamped -120..400), `ShowPercentSymbol = true`, `ShowSeparators = true`, `ColouredLabelsAndIcons = true`, `ColouredValues = false`, `ShowChargingBolt = true`, `UseAccentColour = false`.
3. Extend `AppearanceSettings.Sanitize`: `Enum.IsDefined` fallbacks for the three new enums and `Math.Clamp(WidthAdjustment, -120, 400)`.
4. In `src/Widgets.Core/Settings/BatteryWidgetSettings.cs` add `Dictionary<string,string> Labels` (device id to custom label), `Dictionary<string,string> Colours` (device id to #RRGGBB) and `int RefreshMinutes = 1`.
5. Update `BatteryWidgetSettings.Clone` to deep-copy both dictionaries, and `Sanitize` to: drop empty ids, trim labels to 24 characters and drop blank ones, drop colours that fail `Hex.TryParse` and normalise the rest with `Hex.Normalise`, clamp `RefreshMinutes` to 1..10.
6. Add `public string LabelFor(string id, string fallback)` and `public Rgb? ColourFor(string id)` to `BatteryWidgetSettings` (null when there is no override).
7. Add tests to `tests/Widgets.Core.Tests/AppearanceTests.cs` and `tests/Widgets.Core.Tests/SettingsTests.cs`, then run `dotnet test tests/Widgets.Core.Tests`.

## TDD Checklist
- [ ] Write failing tests: defaults of every new field; old JSON without the new fields loads with defaults; WidthAdjustment 999 clamps to 400 and -999 to -120; an invalid enum string falls back; a bad colour override is dropped and a good one is upper-cased; a label over 24 characters is trimmed; RefreshMinutes 0 and 99 clamp to 1 and 10; JSON round trip keeps Labels and Colours; Clone does not share the dictionaries
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions (`dotnet test tests/Widgets.Core.Tests` and `dotnet test tests/Widgets.Providers.Tests`, one project per call)

## Notes
Existing installs have no new fields in their JSON, so they adopt the defaults, including Layout = VerticalMeters.
