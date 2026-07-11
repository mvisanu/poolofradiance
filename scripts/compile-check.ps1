# Compile-checks the game's runtime C# without a Unity license: builds the scripts
# against the installed editor's DLLs + a FishNet source checkout via dotnet.
# (Does not run FishNet codegen or cover Assets/Editor scripts — the real editor
# compile at bootstrap remains the final gate.)

$ErrorActionPreference = "Stop"
$dir = Join-Path $PSScriptRoot "compile-check"
$fishnetSrc = Join-Path $dir "fishnet-src"

if (-not (Test-Path $fishnetSrc)) {
    Write-Host "Cloning FishNet (shallow) for reference sources..."
    git clone --depth 1 https://github.com/FirstGearGames/FishNet.git $fishnetSrc
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnet) { $dotnet.Source } else { "C:\Program Files\dotnet\dotnet.exe" }

& $dotnetPath build (Join-Path $dir "CompileCheck.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Compile check FAILED." }
Write-Host "Compile check passed - game runtime scripts are compile-clean." -ForegroundColor Green
