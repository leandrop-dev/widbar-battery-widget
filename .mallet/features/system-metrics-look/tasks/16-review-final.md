# Task: Review the whole System Metrics look change
Status: done
Deps: 01-15
Review: final

## Goal
The battery widget's settings, flyout and taskbar preview now follow System Metrics' look, with the options that make sense for batteries.

## Review focus
- Settings JSON compatibility across all three phases, including a settings file saved by 0.2.1.0
- Consistency: the live preview card in settings, the taskbar and the flyout agree on labels, colours and the percent symbol
- Nothing from System Metrics' option set that does not apply to batteries was added, and nothing that does apply was missed
- Tests pass for Core and Providers; the packaged build installs and the Rival 3 retry fix is untouched
