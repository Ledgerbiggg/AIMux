# 一键发布：升版本号 -> 写入更新明细 -> git 提交 -> 推送。
# 打包/发 Release 由 GitHub 工作流（build.yml）云端完成。
#
# 用法:
#   powershell -File release.ps1 -Notes "修复了启动闪退；新增关于页面" [-Part patch]
#   -Part 可选: patch(默认) / minor / major
#
# 说明:
#   - 版本号唯一真相源 = AiMux.Shell/AiMux.Shell.csproj 的 <Version>
#   - 更新明细写入仓库根 version.json 的 notes 字段，Release 说明与更新检测都读它
#   - commit message 为英文；Notes 可为中文（面向用户）

param(
    [Parameter(Mandatory=$true)][string]$Notes,
    [string]$Part = "patch"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot

# 1) 升版本号并写入 notes
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\bump_version.ps1') -Part $Part -Notes $Notes
$prj = Join-Path $root 'AiMux.Shell\AiMux.Shell.csproj'
$c = Get-Content -Path $prj -Encoding utf8
$m = [regex]::Match($c, '<Version>(.*?)</Version>')
$newVer = if ($m.Success) { $m.Groups[1].Value } else { "0.0.0" }

# 2) git 提交并推送（英文 message）
Set-Location $root
git add -A
git commit -m "release: bump to $newVer" -m $Notes
if ($LASTEXITCODE -ne 0) { Write-Error "[release] git commit failed"; exit $LASTEXITCODE }

git push origin main
if ($LASTEXITCODE -ne 0) { Write-Error "[release] git push failed"; exit $LASTEXITCODE }

Write-Host "[release] Pushed $newVer. GitHub Actions will build & publish the installer."
