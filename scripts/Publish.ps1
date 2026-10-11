param(
    [ValidateSet('win-x64','win-x86','win-arm64')][string[]]$Runtimes = @('win-x64','win-x86','win-arm64'),
    [string]$DotnetPath = 'dotnet',
    [string]$Version = '0.3.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src\StatusLookout.Windows\StatusLookout.Windows.csproj'
$artifactRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
foreach ($runtime in $Runtimes) {
    $publishDirectory = Join-Path $artifactRoot $runtime
    & $DotnetPath publish $project -c Release -r $runtime --self-contained true -o $publishDirectory "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime" }
    $executable = Join-Path $artifactRoot "StatusLookout-$runtime.exe"
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'StatusLookout.exe') -Destination $executable -Force
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  StatusLookout-$runtime.exe" | Set-Content -LiteralPath (Join-Path $artifactRoot "SHA256SUMS-$runtime.txt") -Encoding ascii
}
foreach ($notice in @('LICENSE','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination (Join-Path $artifactRoot $notice) -Force
}
& (Join-Path $PSScriptRoot 'Copy-Notices.ps1') -DotnetPath $DotnetPath -Runtime $Runtimes[0]
Get-ChildItem -LiteralPath $artifactRoot -Filter '*.exe' | Select-Object Name,Length
