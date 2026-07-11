# IP guardrail (IP-CHECKLIST.md): fail if any banned WotC term appears in shippable
# text. IP-CHECKLIST.md itself and this script are exempt. Run in CI / before builds.

$banned = @(
    "dungeons & dragons", "dungeons and dragons", "d&d",
    "forgotten realms", "faerun", "moonsea",
    "phlan", "sokal", "valjevo", "tyranthraxus",
    "beholder", "mind flayer", "illithid", "yuan-ti",
    "githyanki", "displacer beast", "umber hulk", "kuo-toa"
)

$repo = Split-Path $PSScriptRoot -Parent
$targets = @("content", "game\Assets", "README.md", "CONTENT-PLAN.md", "ARCHITECTURE.md", "docs")
$failures = @()

foreach ($t in $targets) {
    $path = Join-Path $repo $t
    if (-not (Test-Path $path)) { continue }
    $files = if (Test-Path $path -PathType Container) {
        Get-ChildItem $path -Recurse -File -Include *.json, *.cs, *.md, *.txt, *.asset
    } else { Get-Item $path }
    foreach ($f in $files) {
        $text = (Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue)
        if ($null -eq $text) { continue }
        $lower = $text.ToLowerInvariant()
        foreach ($term in $banned) {
            if ($lower.Contains($term)) {
                $failures += "$($f.FullName): '$term'"
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "IP SCAN FAILED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "IP scan clean." -ForegroundColor Green
exit 0
