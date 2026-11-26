# Task: device-groups
Status: done
Deps: 12

## Goal
Group the shown device metrics into per-kind tabs so the flyout can offer Summary plus one tab per kind that exists.

## Steps
1. Create `src/Widgets.Core/DeviceGroups.cs` with `public sealed record DeviceGroup(string Title, IReadOnlyList<Metric> Devices)` and `public static class DeviceGroups { public static IReadOnlyList<DeviceGroup> For(IReadOnlyList<Metric> metrics) }`.
2. Group by `Metric.Icon` using the `MetricIcons` constants from `src/Widgets.Core/Metric.cs`: Headset to "Headsets", Mouse to "Mice", Keyboard to "Keyboards", Gamepad to "Controllers", anything else to "Other". Return only non-empty groups in that fixed order, keeping each group's devices in the order given.
3. Create `tests/Widgets.Core.Tests/DeviceGroupsTests.cs` and run `dotnet test tests/Widgets.Core.Tests`.

## TDD Checklist
- [ ] Write failing tests: empty input gives no groups; one headset gives one group "Headsets"; a headset, a mouse and a pad give three groups in the fixed order whatever the input order; an unknown icon lands in "Other"; device order inside a group is preserved
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions

## Notes
Summary is not a group; the flyout adds it itself.
