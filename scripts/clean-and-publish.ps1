# Full clean of build outputs, then Release build + portable publish.
# This machine has only .NET SDK 10.x; native apphost/single-file often fails (MSB3030) — often fixed by installing .NET 8 SDK
# and/or adjusting AV. This script produces a framework-dependent folder you can zip; users need .NET 8 Desktop Runtime x64.
param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$WpfDir = Join-Path $RepoRoot "LegitX.WPF"
$WpfProj = Join-Path $WpfDir "LegitX.WPF.csproj"
$Sln = Join-Path $RepoRoot "LegitX V2.sln"

if (-not (Test-Path $WpfProj)) { throw "Project not found: $WpfProj" }

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $WpfDir "publish\LegitX-V2-Portable"
}

Write-Host "== Clean ==" -ForegroundColor Cyan
foreach ($p in @(
        (Join-Path $WpfDir "bin"),
        (Join-Path $WpfDir "obj"),
        (Join-Path $WpfDir "publish"),
        $OutputDir
    )) {
    if (Test-Path $p) {
        Remove-Item $p -Recurse -Force
        Write-Host "  removed: $p" -ForegroundColor DarkGray
    }
}
foreach ($panel in @("admin-panel", "user-panel", "reseller-panel")) {
    $dist = Join-Path (Join-Path $RepoRoot $panel) "dist"
    if (Test-Path $dist) {
        Remove-Item $dist -Recurse -Force
        Write-Host "  removed: $dist" -ForegroundColor DarkGray
    }
}

if (Test-Path $Sln) {
    dotnet clean $Sln -c Release -v minimal 2>&1 | Out-Null
}

Write-Host "== Restore + Build (Release, full compile) ==" -ForegroundColor Cyan
dotnet restore $Sln --force-evaluate
dotnet build $Sln -c Release --no-restore --no-incremental
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

Write-Host "== Publish (framework-dependent win-x64, no apphost) ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

dotnet publish $WpfProj -c Release -r win-x64 -o $OutputDir `
    -p:SelfContained=false `
    -p:PublishSingleFile=false `
    -p:UseAppHost=false `
    -p:DebugType=none `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$runtimeScript = Join-Path $PSScriptRoot "Ensure-DotNetDesktopRuntime.ps1"
if (-not (Test-Path $runtimeScript)) { throw "Missing script: $runtimeScript" }
Copy-Item -Path $runtimeScript -Destination (Join-Path $OutputDir "Ensure-DotNetDesktopRuntime.ps1") -Force

$bat = @"
@echo off
setlocal EnableExtensions
title LegitX V2
cd /d "%~dp0"

echo.
echo  LegitX V2 — checking .NET 8 Desktop Runtime...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Ensure-DotNetDesktopRuntime.ps1"
if errorlevel 1 (
  echo.
  echo  Startup checks failed. See messages above.
  echo.
  pause
  exit /b 1
)

REM Prefer 64-bit host from Program Files
set "DOTNET_ROOT=%ProgramFiles%\dotnet"
if exist "%DOTNET_ROOT%\dotnet.exe" (
  set "PATH=%DOTNET_ROOT%;%PATH%"
)

set "APP_DLL=%~dp0LegitX V2.dll"
if not exist "%APP_DLL%" (
  echo  Missing: LegitX V2.dll in this folder.
  pause
  exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
  start "" "%DOTNET_ROOT%\dotnet.exe" "%APP_DLL%"
) else (
  start "" dotnet "%APP_DLL%"
)
exit /b 0
"@
Set-Content -Path (Join-Path $OutputDir "Launch LegitX V2.bat") -Value $bat -Encoding ASCII

$readme = @"
LegitX V2 - portable build (framework-dependent)
================================================

This folder is NOT a single self-contained .exe. It requires the .NET 8 Windows Desktop Runtime (x64):
https://dotnet.microsoft.com/download/dotnet/8.0

Run: double-click Launch LegitX V2.bat
  - Ensure-DotNetDesktopRuntime.ps1 checks dotnet --list-runtimes for Microsoft.WindowsDesktop.App 8.x
  - If missing, it tries: winget install Microsoft.DotNet.DesktopRuntime.8 (silent, may need admin once)
  - If winget or TLS fails, the script prints manual install steps

Single-file .exe (when apphost works on the build PC):
  dotnet publish LegitX.WPF\LegitX.WPF.csproj -c Release -r win-x64 -o publish\single -p:UseAppHost=true -p:PublishSingleFile=true -p:SelfContained=true

If apphost fails with MSB3030: install .NET 8 SDK and/or exclude build folders from antivirus.
"@
Set-Content -Path (Join-Path $OutputDir "README-PORTABLE.txt") -Value $readme -Encoding utf8

Write-Host "== Web panels (vite build) ==" -ForegroundColor Cyan
foreach ($panel in @("admin-panel", "user-panel", "reseller-panel")) {
    $pd = Join-Path $RepoRoot $panel
    if (-not (Test-Path (Join-Path $pd "package.json"))) { continue }
    Push-Location $pd
    npm run build --strict-ssl=false 2>&1
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "npm run build failed in $panel" }
    Pop-Location
    Write-Host "  built: $panel -> dist\" -ForegroundColor Gray
}

Write-Host "Done. Desktop output: $OutputDir" -ForegroundColor Green
Get-ChildItem $OutputDir | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Gray }
