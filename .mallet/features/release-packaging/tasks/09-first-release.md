# Task: first-release
Status: done
Deps: 08

## Goal
Run the release workflow for the first time with tag v0.4.0 and install the released bundle over the locally installed build to prove users can update by downloading from Releases.

## Steps
1. Run `build/New-ReleaseCert.ps1` (task 07) and ask the user to confirm before upload: uploading the certificate and its password as repository secrets sends them to GitHub. Only after an explicit yes, run the two `gh secret set` commands the script printed, for repository `leandrop-dev/widbar-battery-widget`.
2. Make sure `main` contains the merged work (the feature branch merged, pushed), then trigger a manual run first: `gh workflow run release.yml -f version=0.4.0`, watch it with `gh run watch`, download the artifact with `gh run download`, and fix any failure on the branch before continuing.
3. With the user's go-ahead, create and push the tag: `git tag v0.4.0` and `git push origin v0.4.0`; watch the run; check the Release page lists the .msixbundle, the .cer and the notes (`gh release view v0.4.0`).
4. Download the Release assets into a new empty folder under the scratchpad directory, import the .cer into `Cert:\LocalMachine\TrustedPeople` (needs an elevated shell; ask the user to run it with the `!` prefix) and install the bundle with `Add-AppxPackage`. The released package is 0.4.0.0, newer than the local 0.3.4.0 from task 01, so it installs over it; never release a version lower than the one installed, because Windows refuses downgrades.
5. The user checks the widget runs from the released package; record the outcome and the Release URL in `state.md`.

## Notes
Tags are hard to take back once users download: run the manual workflow and inspect its artifact before pushing the tag. A bad tag release is deleted with `gh release delete` and `git push --delete origin <tag>` only on the user's request.
