param([string]$DotnetPath='dotnet', [string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$destination=Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$packageRoot=$env:NUGET_PACKAGES
if (-not $packageRoot) {
    $line=& $DotnetPath nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the NuGet package directory.' }
    $packageRoot=($line -split ': ',2)[1].Trim()
}
function Find-PackageFile([string]$Package,[string]$Name) {
    $directory=Join-Path $packageRoot $Package
    $version=Get-ChildItem -LiteralPath $directory -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $version) { throw "Package not restored: $Package" }
    $source=Join-Path $version.FullName $Name
    if (-not (Test-Path -LiteralPath $source)) { throw "Package notice missing: $source" }
    return $source
}
$notices=@(
    @{Package="microsoft.netcore.app.runtime.$Runtime";Source='LICENSE.TXT';Target='LICENSE-dotnet-runtime.txt'},
    @{Package="microsoft.netcore.app.runtime.$Runtime";Source='THIRD-PARTY-NOTICES.TXT';Target='THIRD-PARTY-NOTICES-dotnet.txt'},
    @{Package="microsoft.windowsdesktop.app.runtime.$Runtime";Source='LICENSE';Target='LICENSE-dotnet-desktop.txt'},
    @{Package='microsoft.toolkit.uwp.notifications';Source='License.md';Target='LICENSE-notifications.md'}
)
foreach ($notice in $notices) {
    $source=Find-PackageFile $notice.Package $notice.Source
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination $notice.Target) -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/LICENSE-html-agility-pack.txt') -Destination (Join-Path $destination 'LICENSE-html-agility-pack.txt') -Force
