param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [string]$CompilerPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$player = Join-Path $repo 'game\Builds\Win64\RadiantPool.exe'
$data = Join-Path $repo 'game\Builds\Win64\RadiantPool_Data'
$definition = Join-Path $repo 'installer\RadiantPool.iss'
$output = Join-Path $repo "game\Builds\Installer\RadiantPool-Setup-$Version.exe"

if (-not (Test-Path -LiteralPath $player) -or -not (Test-Path -LiteralPath $data)) {
    throw 'The Win64 player is missing. Run scripts/build-all.ps1 before building the installer.'
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($CompilerPath) -or
    -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Inno Setup ISCC.exe was not found. Install Inno Setup 6 or 7 from https://jrsoftware.org/isdl.php, or pass -CompilerPath.'
}

Write-Host "Building Radiant Pool $Version installer..."
& $CompilerPath "/DMyAppVersion=$Version" $definition
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $output)) {
    throw "Inno Setup reported success but did not produce $output."
}

$file = Get-Item -LiteralPath $output
$hash = Get-FileHash -LiteralPath $output -Algorithm SHA256
Write-Host "Installer: $($file.FullName)"
Write-Host "Size:      $([Math]::Round($file.Length / 1MB, 1)) MB"
Write-Host "SHA-256:   $($hash.Hash)"
