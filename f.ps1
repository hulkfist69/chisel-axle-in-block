# Axle Chisel - "f" dev command dispatcher.
#
#   f dev     open the interactive dev terminal (numbered menu)
#   f test    run the game now (build + deploy + launch)
#   f build   build the zip only
#   f log     push VS logs to the repo
#   f         (no arg) opens the interactive menu
#
# Bare "f" works once you add the profile function (see README). Otherwise use .\f
param(
    [string]$Command = "",
    [Parameter(ValueFromRemainingArguments = $true)] $Rest
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

function Get-ModVersion {
    try { (Get-Content (Join-Path $root "modinfo.json") | ConvertFrom-Json).version } catch { "?" }
}
function Get-Branch {
    try { (git -C $root rev-parse --abbrev-ref HEAD 2>$null).Trim() } catch { "?" }
}

# --- Actions (each forwards to the underlying script) -------------------------
function Act-RunGame      { & (Join-Path $root "dev.ps1") }
function Act-RunPushLogs  { & (Join-Path $root "dev.ps1") -PushLogs }
function Act-BuildOnly    { & (Join-Path $root "build.ps1") }
function Act-DeployOnly   { & (Join-Path $root "dev.ps1") -NoLaunch }
function Act-PushLogs     { & (Join-Path $root "pushlogs.ps1") }
function Act-GitPull      { git -C $root pull --rebase --autostash }
function Act-GitStatus    { git -C $root status }

function Act-TailLogs {
    $dataDir = if ($env:VINTAGE_STORY_DATA) { $env:VINTAGE_STORY_DATA } else { Join-Path $env:APPDATA "VintagestoryData" }
    $client = Join-Path $dataDir "Logs\client-main.log"
    if (-not (Test-Path $client)) { Write-Host "No client-main.log yet at $client" -ForegroundColor Yellow; return }
    $lines = Select-String -Path $client -Pattern "[axlechisel]" -SimpleMatch | Select-Object -Last 40
    if (-not $lines) { Write-Host "No [axlechisel] lines found yet." -ForegroundColor Yellow; return }
    $lines | ForEach-Object { Write-Host $_.Line -ForegroundColor Gray }
}

# --- Interactive menu --------------------------------------------------------
function Show-Menu {
    while ($true) {
        Clear-Host
        $ver = Get-ModVersion
        $br  = Get-Branch
        Write-Host ""
        Write-Host "  Axle Chisel - dev terminal" -ForegroundColor Cyan -NoNewline
        Write-Host "      v$ver  " -ForegroundColor DarkGray -NoNewline
        Write-Host "branch: $br" -ForegroundColor Green
        Write-Host "  ---------------------------------------------------------" -ForegroundColor DarkGray
        Write-Host "   1) Run game          " -ForegroundColor White -NoNewline; Write-Host "build + deploy + launch" -ForegroundColor DarkGray
        Write-Host "   2) Run + push logs   " -ForegroundColor White -NoNewline; Write-Host "as above; auto-push logs when you quit VS" -ForegroundColor DarkGray
        Write-Host "   3) Build only        " -ForegroundColor White -NoNewline; Write-Host "produce the zip, don't install" -ForegroundColor DarkGray
        Write-Host "   4) Deploy only       " -ForegroundColor White -NoNewline; Write-Host "build + install, don't launch" -ForegroundColor DarkGray
        Write-Host "   5) Push logs now     " -ForegroundColor White -NoNewline; Write-Host "copy VS logs to repo + push to this branch" -ForegroundColor DarkGray
        Write-Host "   6) Git pull          " -ForegroundColor White -NoNewline; Write-Host "update from origin" -ForegroundColor DarkGray
        Write-Host "   7) Git status        " -ForegroundColor White -NoNewline; Write-Host "show working tree" -ForegroundColor DarkGray
        Write-Host "   8) Tail mod logs     " -ForegroundColor White -NoNewline; Write-Host "last 40 [axlechisel] lines" -ForegroundColor DarkGray
        Write-Host "   0) Quit" -ForegroundColor White
        Write-Host "  ---------------------------------------------------------" -ForegroundColor DarkGray
        $choice = Read-Host "  Select"

        $action = $null
        switch ($choice.Trim()) {
            "1" { $action = { Act-RunGame } }
            "2" { $action = { Act-RunPushLogs } }
            "3" { $action = { Act-BuildOnly } }
            "4" { $action = { Act-DeployOnly } }
            "5" { $action = { Act-PushLogs } }
            "6" { $action = { Act-GitPull } }
            "7" { $action = { Act-GitStatus } }
            "8" { $action = { Act-TailLogs } }
            "0" { return }
            "q" { return }
            default {
                Write-Host "  Unknown option: '$choice'" -ForegroundColor Red
                Start-Sleep -Milliseconds 800
                continue
            }
        }

        Write-Host ""
        try { & $action } catch { Write-Host ("  Error: " + $_.Exception.Message) -ForegroundColor Red }
        Write-Host ""
        Write-Host "  -- done. Press any key to return to the menu --" -ForegroundColor DarkGray
        [void][System.Console]::ReadKey($true)
    }
}

# --- Dispatch ----------------------------------------------------------------
switch ($Command.ToLower()) {
    "dev"   { Show-Menu }
    ""      { Show-Menu }
    "test"  { Act-RunGame }
    "build" { Act-BuildOnly }
    "log"   { Act-PushLogs }
    "pull"  { Act-GitPull }
    default {
        Write-Host "Unknown command: '$Command'" -ForegroundColor Red
        Write-Host "Usage: f [dev|test|build|log|pull]" -ForegroundColor Yellow
    }
}
