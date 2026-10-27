param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $projectRoot '.tools\innosetup-6.7.3'
$compiler = Join-Path $toolRoot 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }

$download = Join-Path $projectRoot '.tools\innosetup-6.7.3.exe'
New-Item -ItemType Directory -Path (Split-Path $download) -Force | Out-Null
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expected = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if (-not (Test-Path -LiteralPath $download)) { Invoke-WebRequest $url -OutFile $download }
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'The official Inno Setup download failed SHA-256 verification.'
}
$process = Start-Process -FilePath $download -ArgumentList @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$toolRoot+'"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup compiler installation failed.' }
return $compiler
