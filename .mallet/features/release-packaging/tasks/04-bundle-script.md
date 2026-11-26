# Task: bundle-script
Status: pending
Deps: 03

## Goal
Make one command build a signed x64 + ARM64 .msixbundle of the widget, with the publish profiles in source control.

## Steps
1. In `.gitignore` add exceptions below the `*.pubxml` rule (line 195): `!**/Properties/PublishProfiles/win-x64.pubxml` and `!**/Properties/PublishProfiles/win-arm64.pubxml`; confirm with `git check-ignore -v` that both profiles are no longer ignored, then `git add` them.
2. Extend `build/package-widbar.ps1` with parameters `[ValidateSet('x64','arm64','both')][string]$Arch = 'x64'` and `[string]$Output`, defaulting to `artifacts\DeviceBatteries.msix` for x64 and `artifacts\DeviceBatteries.msixbundle` for both. Keep the current x64 behaviour as the default so the existing local install flow and the documented command do not change.
3. For `-Arch both`, follow the verdict recorded in `state.md` by task 03: build and package each architecture into its own folder under `artifacts\bundle\`, run `makeappx bundle`, sign the bundle with the same certificate, and fail with a clear message when a step returns a non-zero exit code.
4. Run `.\build\package-widbar.ps1 -CertPassword ... -Arch both`, check that `artifacts\DeviceBatteries.msixbundle` exists and that its `AppxBundleManifest.xml` lists both architectures, and run the default command again to confirm the x64 install still works (`Add-AppxPackage artifacts\DeviceBatteries.msix -ForceApplicationShutdown`).
5. Update `README.md` (build section) with the new `-Arch` option.

## Notes
The bundle's package identity and version come from the manifest exactly as for the x64 package; the version stamping arrives in task 06.
