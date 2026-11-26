# Task: release-cert
Status: done
Deps: 05

## Goal
Provide a script that creates a dedicated code-signing certificate for releases and prepares the two values the release workflow needs as GitHub secrets, without sending anything anywhere by itself.

## Steps
1. Create `build/New-ReleaseCert.ps1` modelled on claude-usage's `tools/New-SigningCert.ps1` (read it with `gh api repos/Cheaterdev/widbar-claude-usage/contents/tools/New-SigningCert.ps1 --jq .content` and decode base64): parameters `-Subject` (default `CN=Leandro Paixão`, which must equal the manifest `Publisher`), `-OutDir` (default `%USERPROFILE%\DeviceBatteries-signing`, outside the repository), and `-Years` (default 5).
2. The script reuses an existing pfx and password file in `-OutDir`, otherwise creates a self-signed code-signing certificate (`New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature` with the code-signing EKU `1.3.6.1.5.5.7.3.3`), exports the pfx with a random password, the public `.cer`, and a base64 text file of the pfx, and removes the certificate from the user's store.
3. Unlike the reference script it does NOT call `gh secret set`. It prints the exact two commands to run (`gh secret set SIGNING_CERT_BASE64 --repo leandrop-dev/widbar-battery-widget < <file>` and the password one) and where the files are, so uploading is a separate, deliberate step in task 09.
4. Add the output folder name to the repository `.gitignore` as a guard (`DeviceBatteries-signing/`), and confirm `git status` shows no certificate material after running the script.
5. Run it once with `-OutDir` under the scratchpad directory, check the .cer subject with `certutil -dump`, then delete that test output.

## Notes
The subject must equal the manifest Publisher exactly or the package will not install. Nothing created here is committed; the user keeps the pfx and password file safe (losing them means users must trust a new certificate).
