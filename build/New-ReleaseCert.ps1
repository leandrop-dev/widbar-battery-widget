<#
.SYNOPSIS
  Creates the self-signed code-signing certificate that signs releases, and prepares the two values the release
  workflow reads from GitHub repository secrets (SIGNING_CERT_BASE64 and SIGNING_CERT_PASSWORD).

  It sends nothing anywhere: it prints the two `gh secret set` commands to run when you decide to upload.

.DESCRIPTION
  The certificate subject is taken from the Publisher of Package.appxmanifest, because a package only installs when
  its Publisher equals the signing certificate subject exactly.

  The files go to a folder in your user profile, NOT the repository. Keep them: signing a later release with a
  different certificate means everyone has to trust the new one again. Running the script again reuses what is there.

.EXAMPLE
  .\build\New-ReleaseCert.ps1
#>
param(
    [string]$Manifest = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Widgets.WidBar\Package.appxmanifest'),
    [string]$Repo = 'leandrop-dev/widbar-battery-widget',
    [string]$OutDir = (Join-Path $env:USERPROFILE 'DeviceBatteries-signing'),
    [int]$Years = 5
)
$ErrorActionPreference = 'Stop'

$publisher = ([xml](Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8)).Package.Identity.Publisher
if (-not $publisher) { throw "No Identity Publisher found in $Manifest" }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$pfx = Join-Path $OutDir 'DeviceBatteries.pfx'
$cer = Join-Path $OutDir 'DeviceBatteries.cer'
$pwFile = Join-Path $OutDir 'password.txt'
$b64File = Join-Path $OutDir 'SIGNING_CERT_BASE64.txt'

if ((Test-Path $pfx) -and -not (Test-Path $pwFile)) {
    # Creating a new key here would overwrite the one every installed copy was trusted for.
    throw "$pfx exists but $pwFile does not. Put the password back in password.txt, or move the old .pfx away on purpose to start over."
}
if (Test-Path $pfx) {
    Write-Host "Reusing the existing certificate in $OutDir"
}
else {
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $plain = [Convert]::ToBase64String($bytes)
    $password = ConvertTo-SecureString $plain -AsPlainText -Force

    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -KeyUsage DigitalSignature `
        -FriendlyName 'Device batteries widget' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears($Years) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $password | Out-Null
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey   # keep it, and its key container, off this PC's store
    Set-Content -Path $pwFile -Value $plain -Encoding ascii
}

Set-Content -Path $b64File -Value ([Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx))) -Encoding ascii

$subject = (New-Object Security.Cryptography.X509Certificates.X509Certificate2 $cer).Subject
if ($subject -ne $publisher) { throw "The certificate subject '$subject' differs from the manifest Publisher '$publisher'." }

Write-Host ''
Write-Host "Certificate subject: $subject (equals the manifest Publisher)"
Write-Host "Private, keep safe:  $pfx and $pwFile"
Write-Host "Public certificate:  $cer"
Write-Host ''
Write-Host 'Nothing has been uploaded. When you decide to, this sends the signing certificate to GitHub as secrets:'
Write-Host "  (Get-Content '$b64File' -Raw).Trim() | gh secret set SIGNING_CERT_BASE64 --repo $Repo"
Write-Host "  (Get-Content '$pwFile' -Raw).Trim() | gh secret set SIGNING_CERT_PASSWORD --repo $Repo"
