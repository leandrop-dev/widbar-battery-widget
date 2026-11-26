# Task: Review the layouts and the options
Status: done
Deps: 06, 07, 08, 09, 10, 11
Review: phase 2

## Goal
The taskbar preview and the settings preview card draw System Metrics' five layouts, and every setting changes something visible or is shown as unused.

## Review focus
- Every option in `PreviewOption` and every toggle on the settings page is read by each layout that `LayoutSupport.Honours` says honours it, and ignored (and disabled on the page) by the others: no option silently does nothing
- A settings file saved with the removed layout "Row", or with any unknown layout, still loads all its other settings
- Width: `WidthAdjustment`, spacing and tile count give a width WidBar can honour, the 60 px floor applies, the settings card shows the same number as the taskbar, and `RequestPreviewRefresh` cannot loop
- Small taskbar buttons (slot about 26 px) and the normal 48 px slot: no layout clips, and Fill capsules and Icon matrix (two rows) still fit the small slot or scale down
- Colours: per-device colour, Windows accent and the palette resolve through `DeviceColours` only; text stays readable (contrast 4.5) in light and dark; stale and low-battery devices stay distinguishable without relying on colour alone
- Radial gauge edge cases: 0 %, 100 % (full ring) and no reading; arcs are rebuilt only when a level changes
