# Task: Review the settings model and page
Status: done
Deps: 01, 02, 03, 04
Review: phase 1

## Goal
The settings model gained the System Metrics options and the settings page now follows its card layout.

## Review focus
- Old settings JSON (no new fields) still loads with sane defaults, and nothing in `Sanitize` can throw on a hand-edited file
- `IntervalVotes` locking and the `BatteryHub` lifetime: a removed copy must not keep a vote, and the first copy must not own the interval
- The settings page's draft flow: Cancel must restore the original (WidBar sends the original JSON through `OnSettingsDraftChanged`), and rebuilding the device expanders from inside a click handler must not replace the control being clicked
- Theme correctness: cards use `ThemeResource` brushes only, with no fixed colours, in light and dark
- Accessibility: every control has an `AutomationProperties.Name`, and tab order follows the visual order
