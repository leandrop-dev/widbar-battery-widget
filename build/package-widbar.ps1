<#
.SYNOPSIS
  Builds the signed MSIX (or MSIX bundle) of the WidBar device-battery widget (src\Widgets.WidBar). WidBar finds it
  through the com.widbar.widget AppExtension declared in its Package.appxmanifest.

.PARAMETER Arch
  x64 (default): one DeviceBatteries.msix, as before. arm64: the same for ARM64. both: one signed x64 + ARM64
  DeviceBatteries.msixbundle, the file a release publishes.

.EXAMPLE
  .\build\package-widbar.ps1 -CertPassword (Get-Content $env:LOCALAPPDATA\WinTaskbarWidgets-dev\devcert.password.txt -Raw).Trim()

.EXAMPLE
  .\build\package-widbar.ps1 -CertPassword $pw -Arch both
#>
param(
    [string]$Cert = (Join-Path $env:LOCALAPPDATA 'WinTaskbarWidgets-dev\devcert.pfx'),
    [Parameter(Mandatory)][string]$CertPassword,
    [ValidateSet('x64', 'arm64', 'both')][string]$Arch = 'x64',
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$Arch = $Arch.ToLowerInvariant()   # ValidateSet ignores case, but winapp expects lowercase
if (-not $Output) {
    $Output = switch ($Arch) {
        'both' { 'artifacts\DeviceBatteries.msixbundle' }
        'arm64' { 'artifacts\DeviceBatteries-arm64.msix' }   # never replaces the x64 file that installs here
        default { 'artifacts\DeviceBatteries.msix' }
    }
}
$architectures = if ($Arch -eq 'both') { @('x64', 'arm64') } else { @($Arch) }

# The SDK writes obj\widbar\plugin.json during the build, and the package links it in as Public\plugin.json,
# so the project is built first to make sure that file exists and is current. Building each architecture also
# shows a compile error for it before the long packaging step.
foreach ($a in $architectures) {
    $platform = if ($a -eq 'arm64') { 'ARM64' } else { 'x64' }
    dotnet build src\Widgets.WidBar\Widgets.WidBar.csproj -c Release "-p:Platform=$platform"
    if ($LASTEXITCODE -ne 0) { throw "Building the widget for $a failed (exit code $LASTEXITCODE)" }
}

# One call builds, packages and signs; repeating --arch makes winapp publish each architecture and bundle them.
$archArgs = $architectures | ForEach-Object { '--arch'; $_ }
winapp package src\Widgets.WidBar\Widgets.WidBar.csproj -c Release @archArgs --cert $Cert --cert-password $CertPassword --output $Output
if ($LASTEXITCODE -ne 0) { throw "Packaging failed (exit code $LASTEXITCODE)" }

Get-Item $Output | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
