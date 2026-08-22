# Build the single-file installer from _publish/ via Inno Setup.
# Usage: powershell -File build_installer.ps1 -Iscc "<ISCC path>" -Iss "installer.iss"
# Version is read from csproj <Version>; no manual passing needed.
param(
    [string]$Iscc = "D:\Inno Setup 7\ISCC.exe",
    [string]$Iss = "installer.iss"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$prj = Join-Path $root 'AiMux.Shell\AiMux.Shell.csproj'
$c = Get-Content -Path $prj -Encoding utf8
$m = [regex]::Match($c, '<Version>(.*?)</Version>')
$version = if ($m.Success) { $m.Groups[1].Value } else { "0.0.0" }

if (-not (Test-Path $Iscc)) {
    Write-Error "[installer] Inno Setup compiler not found: $Iscc"; exit 1
}

# Keep only the latest installer: remove all previous AiMux-Setup-*.exe before building.
# The new one is generated right after, so it won't be deleted.
$pkgDir = Join-Path $root 'package'
if (-not (Test-Path $pkgDir)) { New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null }
if (Test-Path $pkgDir) {
    Get-ChildItem -Path $pkgDir -Filter 'AiMux-Setup-*.exe' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host "[package] Cleaned old installers in package/"
}

Write-Host "[dist] Packing installer with Inno Setup (version $version)..."
$issPath = Join-Path $root $Iss
& $Iscc $issPath "/DMyAppVersion=$version"
if ($LASTEXITCODE -ne 0) { Write-Error "[dist] Inno Setup compile failed"; exit $LASTEXITCODE }

if (Test-Path (Join-Path $root '_publish')) { Remove-Item -Path (Join-Path $root '_publish') -Recurse -Force }
$out = Join-Path $root "package\AiMux-Setup-$version.exe"
Write-Host "[package] Done -> $out"
