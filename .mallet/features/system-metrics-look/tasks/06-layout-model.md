# Task: layout-model
Status: pending
Deps: 05

## Goal
Replace the two-value layout enum with System Metrics' five layouts and add the tested rules every layout builder shares: option support, grid placement, arc geometry and a reader that never loses settings over an unknown layout name.

## Steps
1. In `src/Widgets.Core/Settings/AppearanceSettings.cs` change `PreviewLayout` to `{ VerticalMeters, FillCapsules, IconMatrix, MinimalText, RadialGauges }`. The default stays `VerticalMeters`.
2. Create `src/Widgets.Core/Settings/LenientEnumConverter.cs`: a `JsonConverter<T>` (T is an enum) that reads a string and returns the enum, or `default(T)`'s fallback passed to its constructor when the name is unknown, a number is out of range, or the token is not a string; it writes the name. Apply it to `AppearanceSettings.Layout` (fallback `VerticalMeters`) instead of `JsonStringEnumConverter`, so a file saved with the removed value "Row" keeps every other setting.
3. Create `src/Widgets.Core/Appearance/LayoutSupport.cs`: `public static class LayoutSupport` with an enum `PreviewOption { Icons, MetricLabels, PercentSymbol, Separators, ChargingBolt, ColouredLabelsAndIcons, ColouredValues, Spacing, Alignment, ValueSize }` and `public static bool Honours(PreviewLayout layout, PreviewOption option)`. Rules: `Icons` only for `IconMatrix`; `Separators` only for `MinimalText`; every other option is honoured by every layout.
4. Create `src/Widgets.Core/Appearance/PreviewGrid.cs`: `public static class PreviewGrid { public static (int Column, int Row) Cell(int index, int count, int rows = 2) }` placing items column by column, two rows per column (the order in System Metrics' capsules and icon matrix: CPU and RAM down the first column, GPU and TEMP down the second), and `public static int Columns(int count, int rows = 2)`.
5. Create `src/Widgets.Core/Appearance/Arc.cs`: `public static class Arc { public static (double X, double Y) PointAt(double cx, double cy, double radius, double fraction) ; public static bool IsLargeArc(double fraction) }` for a ring that starts at 12 o'clock and runs clockwise; the fraction is clamped to 0..1.
6. Add tests in `tests/Widgets.Core.Tests/LayoutTests.cs` and run `dotnet test tests/Widgets.Core.Tests`, then `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64` (the enum change will break `PreviewBuilder` only if it names `Row`; it does not yet).

## TDD Checklist
- [ ] Write failing tests: each layout name round-trips through JSON; `"Layout":"Row"`, `"Layout":"nonsense"`, `"Layout":99` and `"Layout":null` all give `VerticalMeters` and leave the other settings (for example `LowThreshold`) intact; `Honours` is true for `IconMatrix`+`Icons` and false for the other four layouts, true for `MinimalText`+`Separators` and false for the others, true for every other option on every layout; `PreviewGrid.Cell` for 4 items gives (0,0),(0,1),(1,0),(1,1), for 3 items gives (0,0),(0,1),(1,0), and `Columns` is 1, 1, 2, 2, 3 for counts 1 to 5 (it is `ceil(count / rows)`); `Arc.PointAt` for fraction 0 is the top point, 0.25 the right point, 0.5 the bottom point, and `IsLargeArc` is false up to 0.5 and true above it
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions (`dotnet test tests/Widgets.Providers.Tests` too)

## Notes
`Honours` is what the settings page (task 11) uses to disable rows, and what the builders use to know when to ignore an option, so there is one table.
