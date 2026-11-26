# Task: plugin-id
Status: pending
Deps: —

## Goal
Replace the placeholder plugin id com.example.devicebatteries with com.codemonk.devicebattery in the plugin class, the package manifest and the project file, and keep the three from drifting apart with a test.

## Steps
1. Create `tests/Widgets.Core.Tests/PackagingTests.cs` with a helper `RepoFile(string relative)` that walks up from `AppContext.BaseDirectory` until it finds a folder containing `Widgets.slnx` or a `.git` entry, and returns the file under it. Add a test `PluginIdIsTheSameInTheClassTheManifestAndTheProject` that reads the three ids (regex on `MainPlugin.cs` for `Id => "..."`, on `Package.appxmanifest` for `<WidBarPluginId>...</WidBarPluginId>`, on `Widgets.WidBar.csproj` for `<WidBarPluginId>...</WidBarPluginId>`) and asserts all equal `com.codemonk.devicebattery`. Add a second test that the id matches `^[a-z0-9]+(\.[a-z0-9]+)+$`.
2. Run `dotnet test tests/Widgets.Core.Tests` and confirm both tests fail (the files still hold com.example.devicebatteries).
3. Edit `src/Widgets.WidBar/MainPlugin.cs` line 28, `src/Widgets.WidBar/Package.appxmanifest` (the `<WidBarPluginId>` element) and `src/Widgets.WidBar/Widgets.WidBar.csproj` line 25 to `com.codemonk.devicebattery`.
4. Run `dotnet test tests/Widgets.Core.Tests` and confirm they pass; run `dotnet build src/Widgets.WidBar/Widgets.WidBar.csproj -c Debug -p:Platform=x64`.
5. Raise the manifest version to 0.3.3.0, run `.\build\package-widbar.ps1 -CertPassword (Get-Content "$env:LOCALAPPDATA\WinTaskbarWidgets-dev\devcert.password.txt" -Raw).Trim()`, then `Add-AppxPackage artifacts\DeviceBatteries.msix -ForceApplicationShutdown`.
6. With the user: open WidBar's Layout page, press Refresh, and add "Device batteries" again from the catalog (the old placed copy is orphaned by the new id); the user checks the taskbar preview, settings and flyout work and reports which of their settings need re-entering.
7. Update `README.md` if it names the id.

## TDD Checklist
- [ ] Write failing tests (id equal in three files; id has the reverse-DNS shape)
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions (`dotnet test tests/Widgets.Providers.Tests` too)

## Notes
The test reads source files, so it needs the repository layout; it skips with a clear message (`Assert.Skip` or an early return with a comment) when the repo root cannot be found, for example in a packaged test run.
