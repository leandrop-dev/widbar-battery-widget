# Task: graphic-layouts
Status: pending
Deps: 06, 07, 08

## Goal
Draw the Vertical meters, Fill capsules and Radial gauges layouts like System Metrics' screenshots, honouring the options that apply to them.

## Steps
1. Create `src/Widgets.WidBar/Layouts/VerticalMetersLayout.cs`: a horizontal `StackPanel`; each device is a tile about 50 px wide and as tall as the slot allows (System Metrics' tiles are 50x46 in a 48 px slot): a 5 px wide rounded bar on the left, filled bottom-up to the level in the device colour over a track of the same colour at 25 % opacity; to its right a 9 px bold label above a 14 px semibold value. `ShowNames` off drops the label; the gap between tiles comes from `Spacing` (Compact 6, Standard 10, Relaxed 16 px).
2. Create `src/Widgets.WidBar/Layouts/FillCapsulesLayout.cs`: a `Grid` of two rows placed with `PreviewGrid.Cell`; each cell is a capsule about 76 x 14 px with corner radius 7: a track in the device colour at 25 % opacity, a fill rectangle (same corner radius) whose width is `level / 100 * capsule width` in the device colour at 70 % opacity, the label (8 px bold, `Label` colour) at the left and the value (12 px semibold, `Value` colour) right-aligned; column gap from `Spacing` (Compact 4, Standard 8, Relaxed 14), row gap 3.
3. Create `src/Widgets.WidBar/Layouts/RadialGaugesLayout.cs`: a horizontal `StackPanel`; each device is a ring about 30 px across: a 3 px track circle in the device colour at 25 % opacity and, over it, a 3 px arc built with `Microsoft.UI.Xaml.Shapes.Path` and an `ArcSegment` whose end point comes from `Arc.PointAt` and whose `IsLargeArc` from `Arc.IsLargeArc` (a level of 100 draws a full `Ellipse` instead, because an arc cannot close on its start point); the 8 px bold label sits above the 12 px bold value, both centred inside the ring; gap from `Spacing` (Compact 8, Standard 14, Relaxed 22).
4. In all three, no reading draws an empty track and "--"; stale devices render at opacity 0.5; charging shows the bolt (`ShowChargingBolt`) beside the value; the accessible name comes from the shared helper in `TileParts`.
5. Build with `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64`. Visual checking happens in task 10.

## Notes
Radial gauges are what System Metrics marks "uses more GPU"; the arc is a static `Path` redrawn only when a level changes, so the cost here is far smaller, and the settings combo keeps the same label text as System Metrics' without the warning.
