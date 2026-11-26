# Task: Review the bundle build
Status: done
Deps: 03, 04
Review: phase 2

## Goal
One command builds a signed x64 + ARM64 .msixbundle, and the existing x64 flow is unchanged.

## Review focus
- The publish profiles are tracked, correct for each architecture (self-contained, runtime identifier) and the .gitignore exceptions do not un-ignore anything else
- `build/package-widbar.ps1` keeps its old default behaviour and parameters; every external command's exit code is checked; paths with spaces work
- The bundle really contains both architectures and is signed with a certificate whose subject equals the manifest Publisher
- The csproj changes (Platforms, RuntimeIdentifiers) do not break the Debug x64 build, the test projects or the existing packaging command
- The spike's verdict in state.md matches what the script does
