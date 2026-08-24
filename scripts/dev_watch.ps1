# 热部署开发：启动应用后持续监听源码变化，自动重新构建并重启（按 Q 或创建 .watch-quit 退出）。
# 用法: powershell -File scripts/dev_watch.ps1  或  make watch
# 原理: 轮询各项目源码文件（.cs/.xaml/.csproj 等）的最后修改时间，发现变化 -> 杀进程 -> dotnet build -> 重启。
#       相比 FileSystemWatcher，轮询在任何终端/后台环境下都可靠；构建失败则保留输出，等待下次修改自动重试。
param(
    [int]$DebounceMs = 600
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$sln = Join-Path $root 'AiMux.sln'
$app = Join-Path $root 'AiMux.Shell\bin\Debug\net9.0-windows\AiMux.exe'
$processName = 'AiMux'

# 监听的源码目录（自动排除 bin/obj 等）
$projectDirs = @('AiMux.Shell', 'AiMux.Services', 'AiMux.Common', 'AiMux.Models')

# 源码文件扩展名（bin/obj/.git 等构建产物与版本目录全部排除）
$includeExt = @('.cs', '.xaml', '.csproj', '.sln', '.resx', '.json', '.props', '.targets')

function Get-SourceFiles {
    Get-ChildItem -Path $projectDirs -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj|\.git|\.vs|\.idea|package|_publish)\\' -and
            $includeExt -contains $_.Extension.ToLowerInvariant()
        }
}

function Build-Snapshot {
    $snap = @{}
    foreach ($f in Get-SourceFiles) { $snap[$f.FullName] = $f.LastWriteTimeUtc }
    return $snap
}

function Stop-App {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Build {
    Write-Host '[watch] 构建中...' -ForegroundColor Cyan
    $output = & dotnet build $sln --nologo -v q 2>&1
    $ok = $LASTEXITCODE -eq 0
    if ($ok) {
        Write-Host '[watch] 构建成功' -ForegroundColor Green
    } else {
        Write-Host '[watch] 构建失败（继续监听，下次修改自动重试）' -ForegroundColor Red
        $output | Select-Object -Last 5 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    }
    return $ok
}

function Start-App {
    if (Test-Path $app) {
        Start-Process -FilePath $app
        Write-Host '[watch] 已启动 AiMux' -ForegroundColor Green
    } else {
        Write-Host "[watch] 未找到 $app，请先执行 make build" -ForegroundColor Yellow
    }
}

# 首次构建并启动
Write-Host '========================================' -ForegroundColor DarkGray
Write-Host '[watch] AiMux 热部署模式（修改源码自动重建重启，按 Q 退出）' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor DarkGray
Stop-App
if (Build) { Start-App } else { Write-Host '[watch] 首次构建失败，修改代码后将自动重试' -ForegroundColor Red }

$snapshot = Build-Snapshot
Write-Host "[watch] 监听中：$($snapshot.Count) 个源码文件，修改自动重启（按 Q 或创建 .watch-quit 退出）" -ForegroundColor DarkGray

$lastRebuildAt = [DateTime]::UtcNow
$quitFile = Join-Path $root '.watch-quit'
try {
    while ($true) {
        # 无控制台（stdin 重定向）时 KeyAvailable 会抛异常，忽略并仅靠 Ctrl+C / 退出标记退出
        try {
            if ([Console]::KeyAvailable) {
                if ([Console]::ReadKey($true).Key -eq 'Q') { break }
            }
        }
        catch {
            # 无控制台环境：跳过按键检测
        }
        # 退出标记文件：创建 .watch-quit 即可优雅退出（无控制台环境用）
        if (Test-Path $quitFile) {
            Remove-Item $quitFile -Force -ErrorAction SilentlyContinue
            break
        }

        # 轮询检测源码变化（时间戳比对；删除的文件视为变化）
        $changed = $false
        $changedPaths = @()
        foreach ($f in Get-SourceFiles) {
            $t = $f.LastWriteTimeUtc
            if ($snapshot.ContainsKey($f.FullName)) {
                if ($snapshot[$f.FullName] -ne $t) { $changed = $true; $changedPaths += $f.FullName }
            } else {
                $changed = $true; $changedPaths += $f.FullName  # 新增文件
            }
        }
        if (-not $changed) {
            foreach ($k in $snapshot.Keys) {
                if (-not (Test-Path $k)) { $changed = $true; $changedPaths += $k }  # 文件被删除
            }
        }

        if ($changed -and ([DateTime]::UtcNow - $lastRebuildAt).TotalMilliseconds -ge $DebounceMs) {
            $lastRebuildAt = [DateTime]::UtcNow
            Write-Host ''
            Write-Host '========================================' -ForegroundColor DarkGray
            Write-Host '[watch] 检测到代码变化，重新构建并重启...' -ForegroundColor Yellow
            $changedPaths | ForEach-Object { Write-Host "  变化: $_" -ForegroundColor DarkGray }
            Stop-App
            if (Build) { Start-App } else { Write-Host '[watch] 构建失败，应用未重启；继续监听等待下次修改' -ForegroundColor Red }
            Write-Host '========================================' -ForegroundColor DarkGray
            $snapshot = Build-Snapshot  # 重建快照，避免同一改动反复触发
        }

        Start-Sleep -Milliseconds 600
    }
}
finally {
    Write-Host ''
    Write-Host '[watch] 已退出（应用仍在运行，可手动关闭）' -ForegroundColor Cyan
}
