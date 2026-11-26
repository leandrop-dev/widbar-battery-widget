# Task: preview-wiring
Status: done
Deps: 08, 09

## Goal
Make the taskbar preview and the settings preview card draw through the five layouts, with spacing, alignment, width adjustment, separators and the percent symbol all taking effect.

## Steps
1. Rewrite `src/Widgets.WidBar/PreviewBuilder.cs`: replace `Fill(StackPanel, ...)` with `public static PreviewResult Build(BatteryWidgetSettings settings, IReadOnlyList<Metric> shown, double slotHeight, bool dark)` returning `PreviewResult(FrameworkElement Content, int Width)`. It picks the `ITileLayout` for `settings.Appearance.Layout` (a `switch` over the five values), builds the content, measures it and returns the final width: `Math.Max(60, ceil(content width + 2 * SidePadding) + settings.Appearance.WidthAdjustment)`. "No batteries" text keeps its current look for an empty list.
2. Apply `Alignment` in the same place: the content's `HorizontalAlignment` is `Left`, `Center` or `Right` inside a `Grid` that fills the preview width, so a widened preview (positive `WidthAdjustment`) places the tiles where the user chose. A narrowed preview keeps the tiles clipped on the side opposite the alignment.
3. In `src/Widgets.WidBar/MainPlugin.cs` replace the `_row` `StackPanel` with a host `ContentPresenter`/`Grid` the new content is swapped into, and use `PreviewResult.Width` for `PreviewLogicalWidth`; keep the `RequestPreviewRefresh` logic and the slot-height and theme redraws.
4. In `src/Widgets.WidBar/SettingsView.cs` replace the `_previewRow` `StackPanel` and the `{width} px - fits content` calculation with the same `PreviewBuilder.Build(...)` call, so the page shows the width the taskbar will really get.
5. Delete `src/Widgets.WidBar/TileFactory.cs` and the now unused pill pieces (`PillStyle` stays in Core only if `LookMetrics` still needs it; remove `LookMetrics` and `PillTint` and their tests only if nothing else uses them: run `dotnet build` and `dotnet test tests/Widgets.Core.Tests` to find out).
6. Build, package and install: raise `Version` in `src/Widgets.WidBar/Package.appxmanifest`, run `.\build\package-widbar.ps1 -CertPassword (Get-Content "$env:LOCALAPPDATA\WinTaskbarWidgets-dev\devcert.password.txt" -Raw).Trim()` and `Add-AppxPackage artifacts\DeviceBatteries.msix -ForceApplicationShutdown`.
7. With the user, open the settings window and capture it with `winapp ui screenshot -w <hwnd>` for each of the five layouts; compare the live preview card against the user's screenshots (`C:\Users\noobi\Projects\metrics-layout-*.png`). Flip every option in turn and confirm the card changes. Ask the user to confirm the taskbar itself by eye, since this session cannot capture it.

## Notes
The preview slot is as tall as the taskbar (48, or about 26 with small taskbar buttons); each layout must clip nothing in either size, checked in the phase 2 review.
