# Task: Review the taskbar preview
Status: pending
Deps: 09
Review: phase 3

## Goal
The taskbar preview offers a vertical-meter layout and every new option affects it.

## Review focus
- Width computation: `WidthAdjustment`, `Spacing` and the tile count must produce a width WidBar can honour, and `RequestPreviewRefresh` must not loop
- Both layouts honour every option they claim to, with no option that silently does nothing
- Small taskbar buttons (slot about 26 px): meters and labels must not clip
- Stale and low-battery states remain distinguishable in both layouts without relying on colour alone
