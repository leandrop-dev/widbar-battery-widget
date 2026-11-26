# Task: Review the flyout
Status: done
Deps: 13, 14
Review: phase 3

## Goal
The flyout shows Summary plus one tab per device kind in System Metrics' card style, with a level bar per device.

## Review focus
- `DeviceGroups` ordering and the "Other" bucket; tab selection surviving refreshes and a kind disappearing while it is selected
- Per-device label and colour overrides (`LabelFor`, `ColourFor`) applied everywhere the flyout names or colours a device
- Light and dark themes: no fixed colours left except band colours; text contrast on the host's acrylic, dense acrylic and Mica
- The flyout still opens fast (`OnFlyoutShown` refresh) and keyboard navigation reaches the tab strip and the cards
