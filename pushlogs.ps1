# Axle Chisel — push Vintage Story logs back to the git repo for remote review.
# Copies the VS log files into ./logs, writes a filtered [axlechisel]-only view,
# then commits and pushes just that folder so they can be pulled on the dev Mac.
#
#   .\pushlogs.ps1                  # copy logs, commit, push to the CURRENT branch
#   .\pushlogs.ps1 -Branch test-logs   # push to a specific branch instead
#
# Reads the same paths as dev.ps1: $env:APPDATA\VintagestoryData\Logs by default,
# or $env:VINTAGE_STORY_DATA\Logs if that override is set.
param(
    [string]$Branch = ""
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Default to whatever branch you're working out of (e.g. dev), so logs land there.
if (-not $Branch) { $Branch = (git -C $root rev-parse --abbrev-ref HEAD).Trim() }

$dataDir = if ($env:VINTAGE_STORY_DATA) { $env:VINTAGE_STORY_DATA } else { Join-Path $env:APPDATA "VintagestoryData" }
$logsSrc = Join-Path $dataDir "Logs"
$logsDst = Join-Path $root "logs"

if (-not (Test-Path $logsSrc)) { Write-Error "VS Logs folder not found: $logsSrc" }
New-Item -ItemType Directory -Force -Path $logsDst | Out-Null

# Copy the logs worth reviewing (overwrite same names so the repo stays small;
# git history keeps prior sessions). Missing files are skipped silently.
$wanted = @("client-main.log", "server-main.log", "client-debug.log", "server-debug.log")
$copied = @()
foreach ($name in $wanted) {
    $src = Join-Path $logsSrc $name
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $logsDst $name) -Force
        $copied += $name
    }
}
if ($copied.Count -eq 0) { Write-Error "No expected log files found in $logsSrc" }

# Quick-read filtered view: just our mod's lines, from whichever logs have them.
$filtered = Join-Path $logsDst "axlechisel-filtered.log"
$matches = foreach ($name in @("client-main.log", "server-main.log")) {
    $p = Join-Path $logsSrc $name
    if (Test-Path $p) {
        Select-String -Path $p -Pattern "[axlechisel]" -SimpleMatch | ForEach-Object { "$name`: $($_.Line)" }
    }
}
if ($matches) { $matches | Set-Content -Encoding UTF8 $filtered }

Write-Host "Copied: $($copied -join ', ')" -ForegroundColor Green

# --- Commit + push only the logs folder ---------------------------------------
# Rebase first so the dev Mac's code pushes and these log pushes don't diverge;
# --autostash tucks the freshly copied logs aside during the rebase.
Write-Host "Syncing with origin/$Branch..." -ForegroundColor Cyan
git -C $root pull --rebase --autostash origin $Branch | Out-Null

git -C $root add -- logs
git -C $root commit -m ("test logs: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No log changes to commit." -ForegroundColor DarkGray
    return
}

git -C $root push origin $Branch
Write-Host "Pushed logs to origin/$Branch." -ForegroundColor Green
