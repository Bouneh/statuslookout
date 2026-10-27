param(
    [Parameter(Mandatory)][ValidateSet('win-x64','win-x86','win-arm64')][string]$Runtime,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$InnoSetupCompiler = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $projectRoot 'artifacts'
$publish = Join-Path $artifacts $Runtime
$executable = Join-Path $publish 'StatusLookout.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publish the application before building its installer.' }
$actualVersion = (Get-Item -LiteralPath $executable).VersionInfo.FileVersion
if ([version]$actualVersion -ne [version]($Version+'.0')) { throw "Published executable version $actualVersion does not match installer $Version." }
foreach ($notice in @('LICENSE','THIRD-PARTY-NOTICES.md','LICENSE-dotnet-runtime.txt','LICENSE-dotnet-desktop.txt','LICENSE-notifications.md','LICENSE-html-agility-pack.txt','THIRD-PARTY-NOTICES-dotnet.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $artifacts $notice))) { throw "Missing bundled license: $notice" }
}
if (-not $InnoSetupCompiler) { $InnoSetupCompiler = & (Join-Path $PSScriptRoot 'Get-InnoSetup.ps1') }
& $InnoSetupCompiler "/DAppVersion=$Version" "/DRuntime=$Runtime" "/DPublishDir=$publish" "/DNoticeDir=$artifacts" "/DOutputDir=$artifacts" (Join-Path $projectRoot 'installer\StatusLookout.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $Runtime" }
$name = "StatusLookout-Setup-$Runtime.exe"
$hash = (Get-FileHash -LiteralPath (Join-Path $artifacts $name) -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name" | Set-Content -LiteralPath (Join-Path $artifacts "SHA256SUMS-Setup-$Runtime.txt") -Encoding ascii
Write-Output "Built $name"
