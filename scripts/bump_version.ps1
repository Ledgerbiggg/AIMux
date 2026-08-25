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

# 同步 version.json 的 version 字段（若存在），避免两处不一致。
# 对象方式更新 + .NET 写入 UTF-8 无 BOM：PS5.1 的 Set-Content -Encoding utf8 会写 BOM，
# 带 BOM 的 JSON 经 GitHub raw 分发后会被程序 JsonDocument.Parse 拒绝，更新检测失效。
$vj = Join-Path $repo 'version.json'
if (Test-Path $vj) {
    $obj = Get-Content $vj -Raw -Encoding UTF8 | ConvertFrom-Json
    $obj.version = $nv
    if ($Notes -ne '') { $obj.notes = $Notes }
    $out = $obj | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($vj, $out, [System.Text.UTF8Encoding]::new($false))  # 无 BOM
}

Write-Host "[bump] $($m.Groups[1].Value) -> $nv"
