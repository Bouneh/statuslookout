param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
# This deliberately exercises real per-user registration on a clean, disposable runner.
# Never replace an existing user's installation/preferences while running a smoke test.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer smoke tests must run on a disposable GitHub Actions runner.' }
$data = Join-Path $env:LOCALAPPDATA 'StatusLookout'
if (Test-Path -LiteralPath $data) { throw 'Expected a clean runner without existing StatusLookout preferences.' }
$target = Join-Path $env:LOCALAPPDATA 'Programs\StatusLookout'
if (Test-Path -LiteralPath $target) { throw 'Expected a clean runner without an existing installation.' }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{750FBCDF-6BB7-4485-AF93-3F6A1BB2DD59}_is1'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'StatusLookout.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'StatusLookout.lnk'
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$executable = Join-Path $target 'StatusLookout.exe'
$expectedStartup = '"' + $executable + '" --background'
function Assert-Condition([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Run-Setup([string[]]$ExtraArguments, [string]$LogName) {
    $log = Join-Path $env:RUNNER_TEMP $LogName
    $process = Start-Process -FilePath $installerPath -ArgumentList (@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LOG="'+$log+'"')) + $ExtraArguments) -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $log -Tail 35; throw "Installer exited with $($process.ExitCode)." }
}
Run-Setup @('/LANG=french','/TASKS="desktopicon,startup"') 'StatusLookout-install.log'
Assert-Condition (Test-Path -LiteralPath $executable) 'Installed executable missing.'
Assert-Condition ((Get-Item -LiteralPath $executable).VersionInfo.FileVersion -eq ($Version+'.0')) 'Installed version is incorrect.'
Assert-Condition (Test-Path -LiteralPath $uninstallKey) 'Windows uninstall entry missing.'
Assert-Condition (Test-Path -LiteralPath $startMenu) 'Start menu shortcut missing.'
Assert-Condition (Test-Path -LiteralPath $desktop) 'Optional desktop shortcut missing.'
Assert-Condition ((Get-ItemProperty -LiteralPath $runKey -Name StatusLookout).StatusLookout -eq $expectedStartup) 'Startup does not target the installed executable.'
Assert-Condition (Test-Path -LiteralPath (Join-Path $target 'Licenses\LICENSE-dotnet-runtime.txt')) 'Bundled runtime license missing.'

New-Item -ItemType Directory -Path $data | Out-Null
$preferences = Join-Path $data 'preferences.json'
$history = Join-Path $data 'state.json'
'{"language":"fr","theme":"dark","selectedServices":["extra.OpenAI"]}' | Set-Content -LiteralPath $preferences
'{"history":[],"lastKnown":{"extra.OpenAI":"Operational"}}' | Set-Content -LiteralPath $history
$preferencesHash = (Get-FileHash -LiteralPath $preferences).Hash
$historyHash = (Get-FileHash -LiteralPath $history).Hash

# A user may turn startup off in the app after initially selecting it in Setup.
Remove-ItemProperty -LiteralPath $runKey -Name StatusLookout
Run-Setup @('/LANG=english') 'StatusLookout-upgrade.log'
Assert-Condition ($null -eq (Get-ItemProperty -LiteralPath $runKey -Name StatusLookout -ErrorAction SilentlyContinue)) 'Upgrade re-enabled startup after a user opt-out.'
Assert-Condition ((Get-FileHash -LiteralPath $preferences).Hash -eq $preferencesHash) 'Upgrade changed preferences.'
Assert-Condition ((Get-FileHash -LiteralPath $history).Hash -eq $historyHash) 'Upgrade changed alert history.'

$report = Join-Path $env:RUNNER_TEMP 'StatusLookout-installed-features.json'
$native = Start-Process -FilePath $executable -ArgumentList @('--feature-test',('"'+$report+'"')) -PassThru -Wait -WindowStyle Hidden
Assert-Condition ($native.ExitCode -eq 0 -and (Test-Path -LiteralPath $report)) 'Installed application feature checks failed.'
$null = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json

New-ItemProperty -LiteralPath $runKey -Name StatusLookout -Value $expectedStartup -PropertyType String -Force | Out-Null
$uninstaller = Join-Path $target 'unins000.exe'
$uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -Wait -WindowStyle Hidden
Assert-Condition ($uninstall.ExitCode -eq 0) 'Uninstall failed.'
Assert-Condition (-not (Test-Path -LiteralPath $executable)) 'Uninstall left the executable behind.'
Assert-Condition (-not (Test-Path -LiteralPath $uninstallKey)) 'Uninstall left its Windows registration behind.'
Assert-Condition (-not (Test-Path -LiteralPath $startMenu)) 'Uninstall left its Start menu shortcut behind.'
Assert-Condition (-not (Test-Path -LiteralPath $desktop)) 'Uninstall left its desktop shortcut behind.'
Assert-Condition ($null -eq (Get-ItemProperty -LiteralPath $runKey -Name StatusLookout -ErrorAction SilentlyContinue)) 'Uninstall left startup enabled.'
Assert-Condition ((Get-FileHash -LiteralPath $preferences).Hash -eq $preferencesHash) 'Uninstall deleted or changed preferences.'
Assert-Condition ((Get-FileHash -LiteralPath $history).Hash -eq $historyHash) 'Uninstall deleted or changed alert history.'
Write-Output 'Installer smoke tests passed: install, shortcuts, startup, upgrade opt-out, native app, uninstall and retained user data.'
