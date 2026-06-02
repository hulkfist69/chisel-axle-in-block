# Axle Chisel — dev hot-loop (Windows)
# Closes any running Vintage Story, builds the mod, swaps the zip into the Mods
# folder, and relaunches the game. For fast iterate-and-test cycles.
#
#   .\dev.ps1            # build + redeploy + launch
#   .\dev.ps1 -NoLaunch  # build + redeploy, but don't start the game
#
# Requires $env:VINTAGE_STORY to point at your VS install dir (the one that
# contains Vintagestory.exe + VintagestoryAPI.dll) — same var build.ps1 uses.
param(
    [switch]$NoLaunch
)
$ErrorActionPreference = "Stop"

if (-not $env:VINTAGE_STORY) {
    Write-Error "VINTAGE_STORY env var not set. Run: setx VINTAGE_STORY `"C:\path\to\Vintagestory`" (then reopen terminal)"
}

$root    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$modinfo = Get-Content (Join-Path $root "modinfo.json") | ConvertFrom-Json
$version = $modinfo.version
$modid   = $modinfo.modid

# Mods folder. $env:APPDATA expands to C:\Users\<you>\AppData\Roaming, so this
# resolves to ...\AppData\Roaming\VintagestoryData\Mods (the standard location).
# Override with $env:VINTAGE_STORY_DATA if you use a custom data path.
$dataDir = if ($env:VINTAGE_STORY_DATA) { $env:VINTAGE_STORY_DATA } else { Join-Path $env:APPDATA "VintagestoryData" }
$modsDir = Join-Path $dataDir "Mods"

# --- 1. Close any running Vintage Story ---------------------------------------
$procs = Get-Process -Name "Vintagestory" -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "Closing running Vintage Story..." -ForegroundColor Yellow
    $procs | Stop-Process -Force
    # Wait for the process(es) to fully release file handles before we touch Mods.
    foreach ($p in $procs) { try { $p.WaitForExit(10000) | Out-Null } catch {} }
    Start-Sleep -Milliseconds 500
} else {
    Write-Host "No running Vintage Story instance." -ForegroundColor DarkGray
}

# --- 2. Build -----------------------------------------------------------------
Write-Host "Building..." -ForegroundColor Cyan
& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) { Write-Error "build.ps1 failed" }

$zip = Join-Path $root "dist\AxleChisel-$version.zip"
if (-not (Test-Path $zip)) { Write-Error "Built zip not found at $zip" }

# --- 3. Swap the mod into the Mods folder -------------------------------------
if (-not (Test-Path $modsDir)) {
    Write-Error "Mods folder not found: $modsDir (set `$env:VINTAGE_STORY_DATA if your data path is custom)"
}

# Remove ALL prior installs of this mod, however they were dropped in:
#   - versioned zips (AxleChisel-0.1.0.zip, etc.)
#   - a loose AxleChisel.dll
#   - an unzipped axlechisel/ folder
Get-ChildItem -Path $modsDir -Filter "AxleChisel-*.zip" -ErrorAction SilentlyContinue | Remove-Item -Force
$looseDll = Join-Path $modsDir "AxleChisel.dll"
if (Test-Path $looseDll) { Remove-Item $looseDll -Force }
$unzipped = Join-Path $modsDir $modid
if (Test-Path $unzipped) { Remove-Item $unzipped -Recurse -Force }

Copy-Item $zip (Join-Path $modsDir "AxleChisel-$version.zip") -Force
Write-Host "Installed AxleChisel-$version.zip -> $modsDir" -ForegroundColor Green

# --- 4. Launch ----------------------------------------------------------------
if ($NoLaunch) {
    Write-Host "Skipping launch (-NoLaunch)." -ForegroundColor DarkGray
    return
}

$exe = Join-Path $env:VINTAGE_STORY "Vintagestory.exe"
if (-not (Test-Path $exe)) { Write-Error "Vintagestory.exe not found at $exe" }
Write-Host "Launching Vintage Story..." -ForegroundColor Cyan
Start-Process -FilePath $exe
