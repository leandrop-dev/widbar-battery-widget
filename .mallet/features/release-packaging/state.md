# State: release-packaging

## Decisions
<!-- 2026-11-26: Plugin id is com.codemonk.devicebattery [user]. Changing it orphans the placed widget and its settings on this machine, so the widget is re-added once; the cached battery levels in SharedDataDirectory are lost, which is harmless. The AppExtension Id ("devicebatteries") and the package Identity Name ("DeviceBatteries.WidBarWidget") are separate and stay unless a later decision changes them: changing the Identity Name would install as a second app. -->
<!-- 2026-11-26: Scope chosen by the user: plugin id, tag-driven release workflow, ARM64 with committed publish profiles. Not in scope: Microsoft Store submission, the WidBar showcase pull request (needs a Store product id), an .appinstaller. Updates stay manual: download the new bundle from Releases and install over the old one [user]. -->
<!-- 2026-11-26: Keep the working `winapp package` flow and add architectures and a bundle step; claude-usage's wapproj + MSBuild flow is the fallback if the spike (task 03) shows winapp or makeappx cannot do ARM64 or a bundle on a GitHub runner. -->
<!-- 2026-11-26: The release uses a dedicated certificate, not the dev certificate in %LOCALAPPDATA%\WinTaskbarWidgets-dev. Uploading it as repository secrets sends it to GitHub, so that step (task 09) needs the user's explicit OK when it happens. The first tag is v0.3.2, matching the installed 0.3.2.0. -->
<!-- 2026-11-26: Commits follow the user's rule: no Co-Authored-By, Generated-with or session lines. -->

## Reference (from the packaging study)
- Plugin id is kept identical in csproj WidBarPluginId, manifest <WidBarPluginId> and the plugin class Id (src/Widgets.WidBar/MainPlugin.cs, Package.appxmanifest, Widgets.WidBar.csproj).
- Sideload package Publisher must equal the signing certificate subject exactly; the Store version needs the 4th version number at 0 (X.Y.Z.0). A tag v0.3.2 stamps Identity Version 0.3.2.0, WidBarPluginVersion 0.3.2 and MainPlugin.Version "0.3.2".
- claude-usage release.yml: restore and build as separate MSBuild calls (MakeAppx error 80080204 otherwise), runs on windows-latest with actions/setup-dotnet 10.0.x, imports the pfx from secrets SIGNING_CERT_BASE64 and SIGNING_CERT_PASSWORD, uploads a workflow artifact, then softprops/action-gh-release creates the Release with the .msixbundle and the public .cer and generated notes.
- Local reference scripts: tools/Build-Local.ps1 (4th version number counts up in 10-minute ticks since 2026-01-01 so a local build installs over an older one) and tools/New-SigningCert.ps1 in Cheaterdev/widbar-claude-usage.
- Our current flow: build/package-widbar.ps1 runs `dotnet build -c Release -p:Platform=x64` then `winapp package <csproj> -c Release --arch x64 --cert <pfx> --cert-password <pw> --output artifacts\DeviceBatteries.msix`. `.gitignore` line 195 ignores *.pubxml, and src/Widgets.WidBar/Properties/PublishProfiles has none tracked.

## Blockers
<!-- - [ ] <description> (check off when resolved) -->
