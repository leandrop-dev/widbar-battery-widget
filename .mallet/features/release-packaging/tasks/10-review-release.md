# Task: Review the release pipeline
Status: pending
Deps: 06, 07, 08, 09
Review: phase 3

## Goal
A pushed tag produces a signed, correctly versioned x64 + ARM64 release, and no secret or certificate can leak through the repository or the workflow logs.

## Review focus
- Secrets: the pfx and password only reach the runner through `secrets` and `env`; nothing echoes them; no certificate, password or machine path is committed (grep the diff and history of the feature); `.gitignore` guards the signing folder
- Version stamping: the tag, manifest Identity Version (X.Y.Z.0), WidBarPluginVersion and MainPlugin.Version always agree; a malformed tag fails before building; prerelease tags are flagged
- The workflow fails loudly when secrets are missing instead of publishing an unsigned bundle; third-party actions are pinned; permissions are minimal (contents: write only)
- The Publisher in the manifest equals the certificate subject; the published .cer is the public certificate only
- The release notes tell a user exactly how to trust the certificate and update, and match what the first release really needed
