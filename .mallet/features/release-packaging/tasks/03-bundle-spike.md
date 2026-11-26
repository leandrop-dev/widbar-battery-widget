# Task: bundle-spike
Status: pending
Deps: 02

## Goal
Find out, with evidence, whether `winapp package` and `makeappx` can build an ARM64 package and a signed x64 + ARM64 .msixbundle for this project, locally and on a GitHub-hosted runner, and record the answer.

## Steps
1. Run `winapp package --help` and `winapp --version`; note the options for architecture, bundle and publish profile, and write them into `state.md`.
2. Create `src/Widgets.WidBar/Properties/PublishProfiles/win-x64.pubxml` and `win-arm64.pubxml` modelled on claude-usage's (read them with `gh api repos/Cheaterdev/widbar-claude-usage/contents/ClaudeUsage/ClaudeUsage.ExtensionApp/Properties/PublishProfiles/win-x64.pubxml --jq .content` and decode base64), with `SelfContained` true and the right `RuntimeIdentifier`.
3. Add `<Platforms>x64;ARM64</Platforms>` and `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` to `src/Widgets.WidBar/Widgets.WidBar.csproj`, then try `dotnet build src\Widgets.WidBar\Widgets.WidBar.csproj -c Release -p:Platform=ARM64` and `winapp package src\Widgets.WidBar\Widgets.WidBar.csproj -c Release --arch arm64 --cert <pfx> --cert-password <pw> --output artifacts\DeviceBatteries-arm64.msix`. Record exact errors if it fails.
4. Find `makeappx.exe` (`Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe`), try `makeappx bundle /d <folder with both .msix> /p artifacts\DeviceBatteries.msixbundle`, then sign it with `signtool sign /fd SHA256 /f <pfx> /p <pw> artifacts\DeviceBatteries.msixbundle`. Record whether the bundle installs with `Add-AppxPackage` (x64 machine).
5. Check the runner: `gh api repos/actions/runner-images/contents/images/windows/Windows2025-Readme.md --jq .content` (decode) for the Windows SDK, .NET SDK and Visual Studio versions, and whether `winapp` is installable (`winget`, npm or a release download) without interactive steps; record the installation command that would be needed.
6. Write the verdict in `state.md`: which of (a) winapp for each architecture plus makeappx bundle, or (b) claude-usage's .wapproj + MSBuild is used for tasks 04 and 08, and why. If (b), add the missing steps to tasks 04 and 08 before starting them.

## Notes
This task changes only the csproj, the two profiles and artifacts (ignored); keep what works and revert what does not. The ARM64 package cannot be installed on this x64 machine, so its check is that the build and the bundle succeed and the bundle lists both architectures (`makeappx unbundle` or reading `AppxBundleManifest.xml`).
