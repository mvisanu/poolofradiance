# Radiant Pool - full local build: rules tests -> Unity bootstrap -> Win64 build.
# Requires: .NET 8 SDK, Unity 6000.0.79f1 with an activated license (docs/SETUP.md).

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe"

Write-Host "== 1/3 Rules library tests =="
dotnet test "$repo\rules\RadiantPool.Rules.sln" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Rules tests failed." }

Write-Host "== 2/3 Unity bootstrap (import + scene/prefab generation) =="
& $unity -batchmode -quit -projectPath "$repo\game" `
    -executeMethod RadiantPool.EditorTools.ProjectBootstrap.Run `
    -logFile "$repo\game\Logs\bootstrap.log" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Get-Content "$repo\game\Logs\bootstrap.log" -Tail 60
    throw "Bootstrap failed (exit $LASTEXITCODE) - see game\Logs\bootstrap.log"
}

Write-Host "== 3/3 Win64 build =="
& $unity -batchmode -quit -projectPath "$repo\game" `
    -executeMethod RadiantPool.EditorTools.HeadlessBuild.Win64 `
    -logFile "$repo\game\Logs\build.log" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Get-Content "$repo\game\Logs\build.log" -Tail 60
    throw "Build failed (exit $LASTEXITCODE) - see game\Logs\build.log"
}

Write-Host "Done: $repo\game\Builds\Win64\RadiantPool.exe"
