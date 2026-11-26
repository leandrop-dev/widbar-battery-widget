# Task: Review the release packaging as a whole
Status: pending
Deps: 01–10
Review: final

## Goal
The widget ships under its own plugin id com.codemonk.devicebattery as a signed x64 + ARM64 bundle, built and published from a tag by GitHub Actions, with updates done by downloading the newer bundle from Releases.

## Review focus
- Everything the earlier phase reviews flagged as left open
- A clean checkout can build: the publish profiles, scripts and workflow need nothing that exists only on this machine
- README accuracy: build, install, update and certificate trust instructions match the scripts and the first release
- The local developer flow (`build/package-widbar.ps1` default, install over a previous local build) still works unchanged
- Nothing unrelated crept into the diff; the user's no-attribution commit rule was followed
