<#
.SYNOPSIS
  Builds the signed MSIX of the WidBar device-battery widget (src\Widgets.WidBar). WidBar finds it through the
  com.widbar.widget AppExtension declared in its Package.appxmanifest.

.EXAMPLE
  .\build\package-widbar.ps1 -CertPassword (Get-Content $env:LOCALAPPDATA\WinTaskbarWidgets-dev\devcert.password.txt -Raw).Trim()
#>
param(
    [string]$Cert = (Join-Path $env:LOCALAPPDATA 'WinTaskbarWidgets-dev\devcert.pfx'),
    [Parameter(Mandatory)][string]$CertPassword,
    [string]$Output = 'artifacts\DeviceBatteries.msix'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# The SDK writes obj\widbar\plugin.json during the build, and the package links it in as Public\plugin.json,
# so the project is built once first to make sure that file exists and is current.
dotnet build src\Widgets.WidBar\Widgets.WidBar.csproj -c Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Building the widget failed (exit code $LASTEXITCODE)" }

winapp package src\Widgets.WidBar\Widgets.WidBar.csproj -c Release --arch x64 --cert $Cert --cert-password $CertPassword --output $Output
if ($LASTEXITCODE -ne 0) { throw "Packaging failed (exit code $LASTEXITCODE)" }

Get-Item $Output | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
