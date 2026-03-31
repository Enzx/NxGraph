param(
    [ValidateSet('Source', 'Binary')]
    [string]$Mode = 'Source'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'NxGraph'
$projectPath = Join-Path $repoRoot 'NxGraph\NxGraph.csproj'
$packageRoot = Join-Path $repoRoot 'upm\com.enzx.nxgraph'
$runtimeRoot = Join-Path $packageRoot 'Runtime'
$stagedSourceRoot = Join-Path $runtimeRoot 'NxGraph'
$pluginsRoot = Join-Path $runtimeRoot 'Plugins'
$buildOutput = Join-Path $repoRoot 'NxGraph\bin\Release\netstandard2.1'

function Clear-StagedSource {
    if (Test-Path $stagedSourceRoot) {
        Get-ChildItem -Path $stagedSourceRoot -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force
    }
}

function Clear-StagedPlugins {
    New-Item -ItemType Directory -Force -Path $pluginsRoot | Out-Null
    Get-ChildItem -Path $pluginsRoot -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne '.gitkeep' } |
        Remove-Item -Force
}

function Stage-Source {
    Write-Host "Staging NxGraph source for the Unity package..." -ForegroundColor Cyan
    Write-Host "Source:  $sourceRoot"
    Write-Host "Package: $packageRoot"

    if (-not (Test-Path $sourceRoot)) {
        throw "Source root not found: $sourceRoot"
    }

    New-Item -ItemType Directory -Force -Path $stagedSourceRoot | Out-Null
    Clear-StagedSource
    Clear-StagedPlugins

    $directoriesToCopy = @(
        'Authoring',
        'Compatibility',
        'Diagnostics\Export',
        'Diagnostics\Replay',
        'Diagnostics\Validations',
        'Fsm',
        'Graphs'
    )

    foreach ($relativeDir in $directoriesToCopy) {
        $src = Join-Path $sourceRoot $relativeDir
        $dst = Join-Path $stagedSourceRoot $relativeDir

        if (-not (Test-Path $src)) {
            throw "Expected source directory not found: $src"
        }

        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
        Copy-Item -Path $src -Destination $dst -Recurse -Force
    }

    $filesToCopy = @(
        'Result.cs',
        'ResultHelpers.cs'
    )

    foreach ($relativeFile in $filesToCopy) {
        $src = Join-Path $sourceRoot $relativeFile
        $dst = Join-Path $stagedSourceRoot $relativeFile

        if (-not (Test-Path $src)) {
            throw "Expected source file not found: $src"
        }

        Copy-Item -Path $src -Destination $dst -Force
    }

    $excludedFiles = @(
        (Join-Path $stagedSourceRoot 'Fsm\TracingObserver.cs')
    )

    foreach ($excluded in $excludedFiles) {
        if (Test-Path $excluded) {
            Remove-Item $excluded -Force
        }
    }

    Write-Host ''
    Write-Host 'Unity package source staged successfully.' -ForegroundColor Green
    Get-ChildItem -Path $stagedSourceRoot -Recurse -File |
        Select-Object FullName |
        ForEach-Object { $_.FullName.Replace($repoRoot + '\\', '') }
}

function Stage-Binary {
    Write-Host "Building NxGraph binary fallback for Unity package staging..." -ForegroundColor Cyan
    Write-Host "Project: $projectPath"
    Write-Host "Package: $packageRoot"

    if (-not (Test-Path $projectPath)) {
        throw "Project not found: $projectPath"
    }

    Clear-StagedSource
    Clear-StagedPlugins

    dotnet build $projectPath -c Release -f netstandard2.1

    $dllPath = Join-Path $buildOutput 'NxGraph.dll'
    if (-not (Test-Path $dllPath)) {
        throw "Build completed but NxGraph.dll was not found at $dllPath"
    }

    Copy-Item $dllPath $pluginsRoot -Force

    $pdbPath = Join-Path $buildOutput 'NxGraph.pdb'
    if (Test-Path $pdbPath) {
        Copy-Item $pdbPath $pluginsRoot -Force
    }

    $xmlPath = Join-Path $buildOutput 'NxGraph.xml'
    if (Test-Path $xmlPath) {
        Copy-Item $xmlPath $pluginsRoot -Force
    }

    Write-Host ''
    Write-Host 'Binary fallback staged successfully.' -ForegroundColor Green
    Get-ChildItem -Path $pluginsRoot -File | Select-Object Name, Length | Format-Table -AutoSize
}

switch ($Mode) {
    'Source' { Stage-Source }
    'Binary' { Stage-Binary }
    default { throw "Unsupported mode: $Mode" }
}
