# temp wrapper: read notes from file (UTF-8) and run release.ps1 with it
$notes = (Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot '.release-notes.tmp')).Trim()
& (Join-Path $PSScriptRoot 'release.ps1') -Notes $notes -Part patch
