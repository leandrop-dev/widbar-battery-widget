# Task: settings-caps
Status: done
Deps: 06, 10

## Goal
Make the settings page offer the five layouts and show honestly which options the chosen layout uses, with custom device names shown wherever a device is named.

## Steps
1. In `src/Widgets.WidBar/SettingsView.cs` change the Layout combo items to Vertical meters, Fill capsules, Icon matrix, Minimal text, Radial gauges (tags are the `PreviewLayout` names).
2. Keep a list of the option rows the page builds (Icons, Metric labels, Percent symbol, Metric separators, Colored labels and icons, Colored values, Activity indicators, Spacing, Alignment, Value size) as `Dictionary<PreviewOption, UIElement>`; after every change and once at build, set `IsEnabled = LayoutSupport.Honours(layout, option)` and `Opacity` 0.5 for rows the layout ignores, and give each disabled row the description "Not used by this layout" through `Cards.Field`'s description text block.
3. In `src/Widgets.WidBar/Cards.cs` give `Field` and `Row` a way to change their description afterwards (return a small handle or expose the description `TextBlock`), so step 2 can set it.
4. Show the device's custom name in the device expander header (`BatteryWidgetSettings.LabelFor`) and refresh the header after a label is committed: update the header text in `LabelBox.Commit` instead of rebuilding the list.
5. Commit the label and colour text boxes only when the normalised value differs from the stored one (compare with `_draft.Labels`/`_draft.Colours` before calling `ChangeSettings`), so tabbing through them does not save a draft each time; Enter followed by LostFocus saves once.
6. After a move earlier/later, give keyboard focus back to the moved device's button once `RebuildDevices` has run (`Focus(FocusState.Keyboard)` on the matching button, or the other direction's if it is now disabled).
7. Build, package and install as in task 10 step 6; with the user capture the settings window for two layouts and confirm the greyed rows match `LayoutSupport`.

## Notes
This closes the "options that do nothing" complaint two ways: tasks 08 to 10 make the options work, and this task is honest about the ones a layout truly cannot use.
