# Task: refresh-interval
Status: done
Deps: 01

## Goal
Let each placed copy choose a refresh interval of 1 to 10 minutes and poll at the smallest one among the placed copies.

## Steps
1. Create `src/Widgets.Core/IntervalVotes.cs`: `public sealed class IntervalVotes` with `Set(string instanceId, TimeSpan interval)`, `Remove(string instanceId)` and `TimeSpan Effective(TimeSpan fallback)` returning the minimum vote, or the fallback when there are none. Guard the dictionary with a lock (copies call it from different threads).
2. Add tests in `tests/Widgets.Core.Tests/IntervalVotesTests.cs`.
3. In `src/Widgets.Providers/Battery/BatteryProvider.cs` change `public TimeSpan Interval { get; }` to `{ get; set; }`. `MetricScheduler.RunLoopAsync` reads `provider.Interval` on every loop turn, so the scheduler needs no change.
4. In `src/Widgets.WidBar/BatteryHub.cs` keep a private `IntervalVotes` and the `BatteryProvider` instance in fields. Add `public static void SetRefresh(string instanceId, int minutes)` that records the vote, sets `provider.Interval = votes.Effective(TimeSpan.FromSeconds(60))` and calls `Refresh()`, and `public static void ClearRefresh(string instanceId)`.
5. In `src/Widgets.WidBar/MainPlugin.cs` call `BatteryHub.SetRefresh(context.InstanceId, _settings.RefreshMinutes)` in `InitializeAsync` and in `OnSettingsDraftChanged`, and `BatteryHub.ClearRefresh(Context!.InstanceId)` in `DisposeAsync`.
6. Run `dotnet test tests/Widgets.Core.Tests`, `dotnet test tests/Widgets.Providers.Tests` and `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64`.

## TDD Checklist
- [ ] Write failing tests: no votes returns the fallback; two votes return the smaller; removing the smaller vote returns the larger; setting the same id twice replaces its vote
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions

## Notes
The default stays 60 s, which equals RefreshMinutes = 1.
