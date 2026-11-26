# Task: study-claude-usage
Status: done
Deps: 16

## Goal
Learn from the installed widget at https://github.com/Cheaterdev/widbar-claude-usage how a WidBar widget is packaged, versioned, released and updated, and decide which of those practices this widget adopts.

## Steps
1. Read the repository with `gh repo view Cheaterdev/widbar-claude-usage` and `gh api repos/Cheaterdev/widbar-claude-usage/contents` (README, manifest, build scripts, `.github/workflows`, release assets), and compare with how WidBar shows it in its install list.
2. Write down in state.md: the package format and how WidBar discovers it, where the version lives and how it is bumped, the release workflow (tags, CI, signing), and how an update reaches a user (what WidBar checks, and whether a manifest or feed is involved).
3. Compare with this repo's `build/package-widbar.ps1`, the placeholder id `com.example.devicebatteries` and the manual version bump, and list the changes worth making.
4. Ask the user which of the listed changes to do, then plan them as a separate feature.

## Notes
Read-only study. The id placeholder, Store versus sideload and update handling are the open items from the earlier pending list.
