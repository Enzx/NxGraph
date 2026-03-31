Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot 'upm\com.enzx.nxgraph\Runtime'
$stagedSourceRoot = Join-Path $runtimeRoot 'NxGraph'
$pluginsRoot = Join-Path $runtimeRoot 'Plugins'

if (Test-Path $stagedSourceRoot) {
    Get-ChildItem -Path $stagedSourceRoot -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
    Write-Host "Cleaned staged Unity package sources in $stagedSourceRoot" -ForegroundColor Green
}
else {
    Write-Host "Nothing to clean: $stagedSourceRoot does not exist." -ForegroundColor Yellow
}

if (Test-Path $pluginsRoot) {
    Get-ChildItem -Path $pluginsRoot -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne '.gitkeep' } |
        Remove-Item -Force
    Write-Host "Cleaned staged Unity package binaries in $pluginsRoot" -ForegroundColor Green
}
else {
    Write-Host "Nothing to clean: $pluginsRoot does not exist." -ForegroundColor Yellow
}
