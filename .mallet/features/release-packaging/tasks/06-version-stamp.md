# Task: version-stamp
Status: pending
Deps: 05

## Goal
Stamp one version (for example 0.3.2 from a tag) into the manifest, the project file and the plugin class with a script, so a release never needs a hand edit.

## Steps
1. Create `tests/Widgets.Core.Tests/VersionStampTests.cs`. Each test copies `Package.appxmanifest`, `Widgets.WidBar.csproj` and `MainPlugin.cs` from the repository (using the `RepoFile` helper from task 01's `PackagingTests.cs`; move it to a shared `RepoFiles` static class) into a temp folder keeping their relative paths, runs `pwsh -NoProfile -File build/Set-Version.ps1 -Root <temp> -Version <v>` and reads the results. Cases: `0.3.2` gives manifest Identity `Version="0.3.2.0"`, csproj `<WidBarPluginVersion>0.3.2</WidBarPluginVersion>`, and `Version => "0.3.2"` in MainPlugin.cs; a leading `v` is accepted (`v1.2.3`); a suffix `1.2.3-beta.1` stamps 1.2.3 and prints a `prerelease=true` line; `1.2` and `abc` fail with a non-zero exit code and leave the files untouched; running it twice gives identical files; line endings and the UTF-8 BOM state of each file are preserved byte for byte outside the changed attribute.
2. Run `dotnet test tests/Widgets.Core.Tests` and confirm the new tests fail (the script does not exist).
3. Create `build/Set-Version.ps1` with parameters `-Version` (mandatory) and `-Root` (default: the repository root); read each file as bytes, replace only the targeted value with a regex, write bytes back, and print `version=<X.Y.Z>`, `package_version=<X.Y.Z>.0` and `prerelease=<true|false>` lines (the format the GitHub workflow writes to `$GITHUB_OUTPUT`).
4. Run `dotnet test tests/Widgets.Core.Tests` and confirm they pass.
5. Run the script once against the real tree with `-Version 0.3.3`, check `git diff` shows only the three expected lines, then `git checkout` the three files.

## TDD Checklist
- [ ] Write failing tests
- [ ] Confirm tests fail (red)
- [ ] Implement
- [ ] Confirm tests pass (green)
- [ ] Confirm no regressions

## Notes
`MainPlugin.Version` is currently "0.3.1" while the package is 0.3.2.0; stamping removes that drift. The same regexes are what the workflow in task 08 relies on, so it calls this script instead of repeating them.
