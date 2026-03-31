Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$buildScript = Join-Path $PSScriptRoot 'build-upm.ps1'

if (-not (Test-Path $buildScript)) {
    throw "Build script not found: $buildScript"
}

& $buildScript -Mode Binary

