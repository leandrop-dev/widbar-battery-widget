# Feature: Match the System Metrics look
Status: in progress
Created: 2026-11-26
Branch: f/system-metrics-look

## Goal
Make the battery widget's settings page, taskbar preview and flyout look and behave like WidBar's System Metrics widget, with every setting actually doing something.

## Context
WidBar creates the flyout and settings windows (title bar, Save/Cancel, backdrop, rounded corners), so only the content inside them differs. System Metrics is closed source and the SDK has no shared style kit; the wiki's one hint is "build cards with Border and ThemeResource brushes". The reference was captured live on 2026-11-26 (see state.md for measurements and the full option list). Scope chosen by the user: settings, flyout AND taskbar preview; mirror its option set where it makes sense for batteries; no history graphs (a level bar instead); flyout tabs per device kind. After phase 1 the user found that the new options and layouts did nothing yet, so the plan was reordered on 2026-11-26: the preview layouts and every option come before the flyout, and the layouts are System Metrics' own (five of its six; History columns is left out).

## Phases
1. Settings page: new options in the settings model and a System Metrics style page with a live preview card. (done)
2. Preview layouts and options: the five System Metrics layouts, and every setting wired so it changes something visible.
3. Flyout: tab strip (Summary plus one tab per device kind) and card layout.

## Tasks
- [x] 01-settings-model — add the new System Metrics style options to the settings model, sanitised and defaulted [deps: —] [parallel: yes] [phase: 1]
- [x] 02-card-primitives — theme-aware card, section, expander and row helpers for settings and flyout [deps: —] [parallel: yes] [phase: 1]
- [x] 03-refresh-interval — make the poll interval settable and apply the smallest value among placed copies [deps: 01] [parallel: no] [phase: 1]
- [x] 04-settings-page — rebuild the settings page: live preview card, Presentation, Devices and colors, Data refresh [deps: 01, 02, 03] [parallel: no] [phase: 1]
- [x] 05-review-settings — review phase 1: the new settings model and page [deps: 01, 02, 03, 04] [parallel: no] [phase: 1] [review]
- [x] 06-layout-model — five-layout enum with a lenient reader, per-layout option support, grid placement and arc geometry in Widgets.Core [deps: 05] [parallel: no] [phase: 2]
- [x] 07-colour-resolver — one tested rule for which colour a device's label, icon and value get [deps: 06] [parallel: no] [phase: 2]
- [x] 08-text-layouts — Minimal text and Icon matrix tile builders [deps: 06, 07] [parallel: no] [phase: 2]
- [ ] 09-graphic-layouts — Vertical meters, Fill capsules and Radial gauges tile builders [deps: 06, 07, 08] [parallel: no] [phase: 2]
- [ ] 10-preview-wiring — dispatch to the layouts and apply spacing, alignment, width adjustment, separators and percent symbol [deps: 08, 09] [parallel: no] [phase: 2]
- [ ] 11-settings-caps — settings page uses the five layouts, disables options a layout ignores, shows the real width and custom labels [deps: 06, 10] [parallel: no] [phase: 2]
- [ ] 12-review-preview — review phase 2: the layouts and that every option works [deps: 06, 07, 08, 09, 10, 11] [parallel: no] [phase: 2] [review]
- [ ] 13-device-groups — group metrics into Summary plus per-kind tabs in Widgets.Core [deps: 12] [parallel: yes] [phase: 3]
- [ ] 14-flyout-view — rebuild the flyout with a tab strip and device cards [deps: 12, 13] [parallel: no] [phase: 3]
- [ ] 15-review-flyout — review phase 3: the grouping logic and the flyout [deps: 13, 14] [parallel: no] [phase: 3] [review]
- [ ] 16-review-final — review the whole change: settings, preview and flyout now follow System Metrics' look [deps: 01–15] [parallel: no] [review]
