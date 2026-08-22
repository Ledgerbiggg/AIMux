# 升版本号：读取 csproj 的 <Version>，按指定位 +1 并写回。
# 用法: powershell -File bump_version.ps1 [patch|minor|major]
# 默认 patch：第三位每次 +1，满 10 向 minor 进位（0.6.9 -> 0.7.0）。
# 唯一真相源是 AiMux.Shell/AiMux.Shell.csproj 的 <Version>（三段）。
param(
    [string]$Part = "patch",
    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$prj = Join-Path $repo 'AiMux.Shell\AiMux.Shell.csproj'
$c = Get-Content -Path $prj -Encoding utf8
$m = [regex]::Match($c, '<Version>(.*?)</Version>')
if (-not $m.Success) { Write-Error "未在 csproj 中找到 <Version>"; exit 1 }

$v = $m.Groups[1].Value -split '\.'
$maj = [int]$v[0]; $min = [int]$v[1]; $pat = [int]$v[2]

switch ($Part.ToLower()) {
    'major' { $maj++; $min = 0; $pat = 0 }
    'minor' { $min++; $pat = 0 }
    default {
        # patch：第三位 +1，满 10 进位
        $pat++
        if ($pat -ge 10) { $min++; $pat = 0 }
    }
}
$nv = "$maj.$min.$pat"

# 写回 csproj
$nc = $c -replace '<Version>.*?</Version>', "<Version>$nv</Version>"
Set-Content -Path $prj -Value $nc -Encoding utf8

# 同步 version.json 的 version 字段（若存在），避免两处不一致
$vj = Join-Path $repo 'version.json'
if (Test-Path $vj) {
    $j = Get-Content -Path $vj -Raw -Encoding utf8
    $j = [regex]::Replace($j, '("version"\s*:\s*")[^"]*(")', { param($m) $m.Groups[1].Value + $nv + $m.Groups[2].Value })
    if ($Notes -ne "") {
        $j = [regex]::Replace($j, '("notes"\s*:\s*")[^"]*(")', { param($m) $m.Groups[1].Value + $Notes + $m.Groups[2].Value })
    }
    Set-Content -Path $vj -Value $j -Encoding utf8
}

Write-Host "[bump] $($m.Groups[1].Value) -> $nv"
