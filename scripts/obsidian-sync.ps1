# Mirrors every project *.md (docs, design docs, README, CLAUDE.md, ...) plus the
# Claude auto-memory notes into the Obsidian vault, as a browsable second brain.
# Runs from the Claude Code Stop hook (see .claude/settings.json); safe to run by hand.
# robocopy exit codes 0-7 mean success, so this script always exits 0 on copy success.

$repo = Split-Path $PSScriptRoot -Parent
$vault = "C:\Users\Bruce\Documents\obsidian\projects\poolofradiance"

# Repo markdown, preserving folder structure. Excluded: game/ + scripts/ (vendored
# FishNet/tooling junk), theme/ (reference exports, contain third-party names),
# artifacts/ (build output), .git.
robocopy $repo $vault *.md /S /NFL /NDL /NJH /NJS /NP `
    /XD .git artifacts theme game scripts | Out-Null

# Claude's persistent memory notes -> claude-memory/ inside the vault.
$memory = Join-Path $env:USERPROFILE ".claude\projects\C--Users-Bruce-source-repo-poolofradiance\memory"
if (Test-Path $memory) {
    robocopy $memory (Join-Path $vault "claude-memory") *.md /S /NFL /NDL /NJH /NJS /NP | Out-Null
}

exit 0
