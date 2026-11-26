# Task: release-workflow
Status: pending
Deps: 04, 06, 07

## Goal
Add the GitHub Actions workflow that, on a pushed tag such as v0.3.2, builds the signed x64 + ARM64 bundle and publishes a GitHub Release with the bundle, the public certificate and install notes.

## Steps
1. Create `.github/workflows/release.yml` modelled on claude-usage's (read it with `gh api repos/Cheaterdev/widbar-claude-usage/contents/.github/workflows/release.yml --jq .content` and decode base64). Triggers: `push` of tags `v*` and a `workflow_dispatch` with a `version` input (a manual run builds and keeps the artifact but creates no Release). `permissions: contents: write`, `concurrency` per ref, `runs-on: windows-latest`, `timeout-minutes: 45`.
2. Steps: checkout; `actions/setup-dotnet` with `10.0.x`; any tool installation the spike (task 03) recorded as needed for `winapp` or MSBuild; `build/Set-Version.ps1 -Version <tag or input>` and append its output lines to `$GITHUB_OUTPUT`; import the pfx from secret `SIGNING_CERT_BASE64` and `SIGNING_CERT_PASSWORD` into `$RUNNER_TEMP`, write the public `.cer` into `dist/`; run `build/package-widbar.ps1 -Arch both` with that certificate; copy the bundle to `dist/DeviceBatteries-<version>.msixbundle`; write `dist/release-notes.md` (install: trust the .cer once in Trusted People, open the bundle, then add the widget in WidBar from Layout > Refresh; requires Windows 11 and WidBar; updates: download the newer bundle and install over the old one); `actions/upload-artifact`; `softprops/action-gh-release` on tags with `generate_release_notes: true` and `prerelease` from the stamped output.
3. When the secrets are missing the workflow fails early with a clear message (check `secrets.SIGNING_CERT_BASE64 != ''` through an `env` flag, as the reference does) instead of publishing an unsigned release.
4. Pin each third-party action to a major version tag (`@v4`, `@v2`) as the reference does; do not echo secrets; pass the password only through `env`.
5. Validate the YAML with `gh workflow view` after pushing or locally with a YAML parser (`pwsh -c "ConvertFrom-Yaml"` if available, otherwise `yq`); do not push a tag in this task.

## Notes
The workflow is exercised for the first time in task 09. The release notes text lives in the workflow so the README only links to Releases.
