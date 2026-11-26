<#
.SYNOPSIS
  Stamps one release version into the three places that carry it, so a release never needs a hand edit:
    - the package Identity Version of Package.appxmanifest        (X.Y.Z.0; the Store needs the fourth number to be 0)
    - <WidBarPluginVersion> of Widgets.WidBar.csproj              (X.Y.Z, written to plugin.json by the WidBar SDK)
    - MainPlugin.Version in MainPlugin.cs                         (X.Y.Z)

.PARAMETER Version
  X.Y.Z, optionally with a leading v (a tag name) and a suffix such as -beta.1. A suffix is not stamped; it makes
  the prerelease output line true.

.PARAMETER Root
  The folder holding src\Widgets.WidBar. Defaults to the repository root.

.OUTPUTS
  Lines the GitHub workflow appends to $GITHUB_OUTPUT: version=, package_version= and prerelease=.

.EXAMPLE
  .\build\Set-Version.ps1 -Version v0.4.0
#>
param(
    [Parameter(Mandatory)][AllowEmptyString()][string]$Version,
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'

# ASCII digits only, no leading zeros, each part within what a package version allows (0-65535).
$part = '(0|[1-9][0-9]{0,4})'
if ($Version -cnotmatch "^v?(?<core>$part\.$part\.$part)(?<suffix>-[0-9A-Za-z][0-9A-Za-z.-]*)?\z" -or
    ($Matches['core'] -split '\.' | Where-Object { [int]$_ -gt 65535 })) {
    throw "Version '$Version' is not X.Y.Z with each number 0-65535 (a leading v and a -suffix are allowed)."
}
$core = $Matches['core']
$prerelease = [bool]$Matches['suffix']

# (file, pattern, new value). The text before and after the version (groups 1 and 2) is kept as it is.
$dir = Join-Path $Root 'src\Widgets.WidBar'
$targets = @(
    @{ File = Join-Path $dir 'Package.appxmanifest'; Pattern = '(<Identity\b[^>]*?\bVersion=")[^"]*(")'; Value = "$core.0" },
    @{ File = Join-Path $dir 'Widgets.WidBar.csproj'; Pattern = '(<WidBarPluginVersion>)[^<]*(</WidBarPluginVersion>)'; Value = $core },
    @{ File = Join-Path $dir 'MainPlugin.cs'; Pattern = '(override\s+string\s+Version\s*=>\s*")[^"]*(")'; Value = $core }
)

# Latin-1 maps every byte to one character, so reading and writing with it keeps the BOM and line endings untouched.
$latin1 = [Text.Encoding]::Latin1
$changes = foreach ($t in $targets) {
    $text = $latin1.GetString([IO.File]::ReadAllBytes($t.File))
    $found = [regex]::Matches($text, $t.Pattern, 'Singleline').Count
    if ($found -ne 1) {
        throw "Expected exactly one match in $($t.File) but found $found; nothing was changed."
    }
    $replaced = [regex]::Replace($text, $t.Pattern, { param($m) $m.Groups[1].Value + $t.Value + $m.Groups[2].Value }.GetNewClosure(), 'Singleline')
    [pscustomobject]@{ File = $t.File; Text = $replaced }
}
# Every file matched, so write them all.
foreach ($c in $changes) { [IO.File]::WriteAllBytes($c.File, $latin1.GetBytes($c.Text)) }

"version=$core"
"package_version=$core.0"
"prerelease=$($prerelease.ToString().ToLowerInvariant())"
