# Task: settings-page
Status: pending
Deps: 01, 02, 03

## Goal
Rebuild the settings page in System Metrics' layout: live preview card, Presentation, Devices and colors, Data refresh.

## Steps
1. Extract the preview drawing out of `MainPlugin.RenderPreview` (`src/Widgets.WidBar/MainPlugin.cs:110-144`) into `src/Widgets.WidBar/PreviewBuilder.cs`: `internal static (StackPanel Row, int Width) Build(BatteryWidgetSettings settings, IReadOnlyList<Metric> shown, double slotHeight, bool dark)`. `RenderPreview` calls it and keeps the `RequestPreviewRefresh` logic. Task 09 reuses it.
2. Rewrite `src/Widgets.WidBar/SettingsView.cs` as a `ScrollViewer` holding a `StackPanel` (Spacing 8, content width up to 1056) built from `Cards` (task 02), in this order:
   - Live preview card: header "Live preview", a right-aligned `"{width} px - fits content"`, and a darker inner `Border` showing `PreviewBuilder.Build(...).Row`, rebuilt after every change.
   - Section "Presentation": Layout `ComboBox` row ("Vertical meters", "Row"); Icons `ComboBox` row (Outline, Filled, Simple, Battery gauge, None, writing `Appearance.Icons`); expander "Size and spacing" with Value size (`ComboBox`: Small 90, Medium 100, Large 115, Extra large 130, writing `Appearance.Scale`), Spacing, Alignment, and "Preview width adjustment" (`NumberBox`, -120..400, description "Widen or narrow the taskbar widget. 0 keeps the automatic width."); expander "Labels and colors" with toggle rows Metric labels (`ShowNames`), Percent symbol, Metric separators, Colored labels and icons, Colored values, Activity indicators (`ShowChargingBolt`).
   - Section "Devices and colors": toggle row "Windows accent color" (`UseAccentColour`, description "Each device has its own color" while off); Battery colours `ComboBox` row (Classic, Colour-blind, Pastel, High contrast, Mono, Custom) with the three custom `TextBox` colour boxes from the current `BuildCustomColours` (`SettingsView.cs:121-146`) inside an expander shown only for Custom; a "Flag a device as low at or below (%)" `NumberBox` row (`LowThreshold`, 5..50); one expander per device in `TileLayout.Ordered` order, with the show toggle in the header, a "Label" `TextBox` (writes `Labels[id]`, empty removes it), a "Color" `TextBox` with a `#RRGGBB` swatch (writes `Colours[id]` through `Hex.Normalise`, empty removes it) and the existing move earlier/later buttons from `MoveButton` (`SettingsView.cs:183-196`).
   - Section "Data refresh": Refresh interval `NumberBox` row (1..10, description "A slower interval reduces background activity.", writes `RefreshMinutes`).
   - Footer `TextBlock`: "Changes are previewed live and saved only for this widget instance."
3. Keep the existing draft flow: every change goes through `Save()` (`SettingsView.cs:81-86`), which sanitises, calls `_context.SaveSettings` and `_context.RequestPreviewRefresh`.
4. Build, package and install: `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64`; raise `Version` in `src/Widgets.WidBar/Package.appxmanifest`; run `.\build\package-widbar.ps1 -CertPassword (Get-Content "$env:LOCALAPPDATA\WinTaskbarWidgets-dev\devcert.password.txt" -Raw).Trim()`; run `Add-AppxPackage artifacts\DeviceBatteries.msix -ForceApplicationShutdown`.
5. With the user, open the widget's settings window and capture it with `winapp ui screenshot -w <hwnd>`. Compare against the System Metrics capture (row height about 66 px, icon column, section headings, On/Off toggles, expanders) in dark and light themes.

## Notes
The Layout combo is written and read here; task 09 makes it take effect on the taskbar.
