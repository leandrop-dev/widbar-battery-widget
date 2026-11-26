# Feature: Match the System Metrics look
Status: in progress
Created: 2026-11-26
Branch: f/system-metrics-look

## Goal
Make the battery widget's settings page, flyout and taskbar preview look and behave like WidBar's System Metrics widget.

## Context
WidBar creates the flyout and settings windows (title bar, Save/Cancel, backdrop, rounded corners), so only the content inside them differs. System Metrics is closed source and the SDK has no shared style kit; the wiki's one hint is "build cards with Border and ThemeResource brushes". The reference was captured live on 2026-11-26 (see state.md for measurements and the full option list). Scope chosen by the user: settings, flyout AND taskbar preview; mirror its option set where it makes sense for batteries; no history graphs (a level bar instead); flyout tabs per device kind.

## Phases
1. Settings page: new options in the settings model and a System Metrics style page with a live preview card.
2. Flyout: tab strip (Summary plus one tab per device kind) and card layout.
3. Taskbar preview: a vertical-meter layout like System Metrics' preview; the current row look stays as a choice.

## Tasks
- [x] 01-settings-model — add the new System Metrics style options to the settings model, sanitised and defaulted [deps: —] [parallel: yes] [phase: 1]
- [x] 02-card-primitives — theme-aware card, section, expander and row helpers for settings and flyout [deps: —] [parallel: yes] [phase: 1]
- [x] 03-refresh-interval — make the poll interval settable and apply the smallest value among placed copies [deps: 01] [parallel: no] [phase: 1]
- [x] 04-settings-page — rebuild the settings page: live preview card, Presentation, Devices and colors, Data refresh [deps: 01, 02, 03] [parallel: no] [phase: 1]
- [ ] 05-review-settings — review phase 1: the new settings model and page [deps: 01, 02, 03, 04] [parallel: no] [phase: 1] [review]
- [ ] 06-device-groups — group metrics into Summary plus per-kind tabs in Widgets.Core [deps: 05] [parallel: yes] [phase: 2]
- [ ] 07-flyout-view — rebuild the flyout with a tab strip and device cards [deps: 05, 06] [parallel: no] [phase: 2]
- [ ] 08-review-flyout — review phase 2: the grouping logic and the flyout [deps: 06, 07] [parallel: no] [phase: 2] [review]
- [ ] 09-meter-preview — add the vertical-meter taskbar layout and honour the new options [deps: 08] [parallel: no] [phase: 3]
- [ ] 10-review-preview — review phase 3: the taskbar preview [deps: 09] [parallel: no] [phase: 3] [review]
- [ ] 11-review-final — review the whole change: settings, flyout and preview now follow System Metrics' look [deps: 01–10] [parallel: no] [review]
