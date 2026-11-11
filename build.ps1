# @file build.ps1
# @author Codex
# @module PowerModeSwitch.Build

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'PowerModeSwitch.csproj'
$outputDirectory = Join-Path $projectRoot 'CODEX\Release\win-x64'

if (Test-Path -LiteralPath $outputDirectory) {
    $backupRoot = Join-Path $projectRoot 'CODEX\BACKUP\DEL'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backupPath = Join-Path $backupRoot ('Release-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Move-Item -LiteralPath $outputDirectory -Destination $backupPath
}

dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$intermediatePri = Join-Path $projectRoot 'CODEX\build\bin\PowerModeSwitch\x64\Release\net10.0-windows10.0.19041.0\win-x64\PowerModeSwitch.pri'
if (Test-Path -LiteralPath $intermediatePri) {
    Copy-Item -LiteralPath $intermediatePri -Destination (Join-Path $outputDirectory 'PowerModeSwitch.pri') -Force
}

$readme = Join-Path $projectRoot 'README.md'
if (Test-Path -LiteralPath $readme) {
    Copy-Item -LiteralPath $readme -Destination (Join-Path $outputDirectory 'README.md') -Force
}

$executable = Join-Path $outputDirectory 'PowerModeSwitch.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Build completed without PowerModeSwitch.exe"
}

Write-Host "Published: $executable"
