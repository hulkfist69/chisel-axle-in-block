# Axle Chisel - Windows build + package
$ErrorActionPreference = "Stop"

if (-not $env:VINTAGE_STORY) {
    Write-Error "VINTAGE_STORY env var not set. Run: setx VINTAGE_STORY `"C:\path\to\Vintagestory`" (then reopen terminal)"
}

$root    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$modinfo = Get-Content (Join-Path $root "modinfo.json") | ConvertFrom-Json
$version = $modinfo.version

$stage = Join-Path $root "dist\stage"
$dist  = Join-Path $root "dist"
$zip   = Join-Path $dist "AxleChisel-$version.zip"

$buildStamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " UTC" + (Get-Date).ToString("zzz")
$shortSha = try { (git -C $root rev-parse --short HEAD).Trim() } catch { "unknown" }
@"
namespace AxleChisel
{
    public static class BuildInfo
    {
        public const string Version = "$version";
        public const string Stamp = "$buildStamp";
        public const string Sha = "$shortSha";
    }
}
"@ | Set-Content -Encoding UTF8 -Path (Join-Path $root "src\BuildInfo.cs")

Write-Host "Building AxleChisel.dll (Release)..." -ForegroundColor Cyan
dotnet build -c Release (Join-Path $root "AxleChisel.csproj")
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed" }

$dll = Join-Path $root "bin\Release\AxleChisel.dll"
if (-not (Test-Path $dll)) { Write-Error "Build output not found at $dll" }

Write-Host "Staging mod files..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $dll                          (Join-Path $stage "AxleChisel.dll")
Copy-Item (Join-Path $root "modinfo.json") (Join-Path $stage "modinfo.json")
Copy-Item -Recurse (Join-Path $root "assets") (Join-Path $stage "assets")

if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force

Write-Host "Done: $zip" -ForegroundColor Green
