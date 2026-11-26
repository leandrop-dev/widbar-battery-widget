# Feature: Release packaging
Status: planning
Created: 2026-11-26
Branch: —

## Goal
Give the battery widget its real plugin id, an x64 + ARM64 signed bundle, and a tag-driven GitHub release, following how Cheaterdev/widbar-claude-usage ships.

## Context
The packaging study (system-metrics-look state.md, "Packaging study") found that WidBar widgets ship as MSIX with the com.widbar.widget AppExtension, that claude-usage builds a signed x64 + ARM64 .msixbundle and a Store .msixupload from one tag in GitHub Actions, and that updates are manual downloads from Releases (or the Store). Our widget is built with `winapp package` for x64 only, with the placeholder id com.example.devicebatteries, and a hand-edited version in three places. The user chose: plugin id com.codemonk.devicebattery, the tag-driven release workflow, ARM64 with committed publish profiles, and manual updates. The Microsoft Store is not part of this feature.

## Phases
1. Plugin id: the real id in all three places, with a test that keeps them identical.
2. Bundle: publish profiles in source control and one command that builds a signed x64 + ARM64 .msixbundle.
3. Release: version stamping from a tag, a dedicated release certificate, the GitHub Actions workflow and a first release installed from it.

## Tasks
- [x] 01-plugin-id — set com.codemonk.devicebattery in the plugin class, manifest and csproj, with a test that the three agree [deps: —] [parallel: no] [phase: 1]
- [x] 02-review-id — review phase 1: the id change and its consistency test [deps: 01] [parallel: no] [phase: 1] [review]
- [x] 03-bundle-spike — find out whether winapp and makeappx can build ARM64 and a bundle here and on a GitHub runner [deps: 02] [parallel: no] [phase: 2]
- [x] 04-bundle-script — commit the publish profiles and make build/package-widbar.ps1 produce a signed .msixbundle [deps: 03] [parallel: no] [phase: 2]
- [x] 05-review-bundle — review phase 2: the profiles and the bundle script [deps: 03, 04] [parallel: no] [phase: 2] [review]
- [x] 06-version-stamp — a script that stamps one version into the manifest, csproj and plugin class, with tests [deps: 05] [parallel: yes] [phase: 3]
- [x] 07-release-cert — a script that makes the dedicated release certificate and prepares the two GitHub secrets [deps: 05] [parallel: yes] [phase: 3]
- [x] 08-release-workflow — the GitHub Actions workflow that builds and publishes a release from a tag [deps: 04, 06, 07] [parallel: no] [phase: 3]
- [x] 09-first-release — set the secrets, push the first tag and install the released bundle [deps: 08] [parallel: no] [phase: 3]
- [x] 10-review-release — review phase 3: stamping, certificate handling and the workflow [deps: 06, 07, 08, 09] [parallel: no] [phase: 3] [review]
- [ ] 11-review-final — review the whole change: the widget ships under its own id as a signed x64 + ARM64 release from a tag [deps: 01–10] [parallel: no] [review]
