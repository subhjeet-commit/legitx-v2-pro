param(
    [string]$ProjectPath = ".\LegitX.WPF\LegitX.WPF.csproj",
    [string]$PublishOutputDir = ".\LegitX.WPF\publish\LegitX-V2-single",
    [string]$Runtime = "win-x64",
    # If set, this DLL is copied over the win-x64 output (no compile). Use when your golden DLL is not already in win-x64.
    [string]$CopyFromDll = "",
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

Write-Host "== Publish single-file EXE from existing win-x64 output (no build) ==" -ForegroundColor Cyan

if (!(Test-Path $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}

$projectFullPath = (Resolve-Path $ProjectPath).Path
$projectDir = Split-Path $projectFullPath -Parent

Write-Host "Resolving assembly name / target framework..." -ForegroundColor Yellow
$metaRaw = dotnet msbuild $projectFullPath -nologo -getProperty:AssemblyName,TargetFramework
if ($LASTEXITCODE -ne 0) {
    throw "Failed to read project metadata."
}
$meta = $metaRaw | ConvertFrom-Json
$assemblyName = $meta.Properties.AssemblyName
$targetFramework = $meta.Properties.TargetFramework
if ([string]::IsNullOrWhiteSpace($assemblyName) -or [string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "Could not resolve AssemblyName/TargetFramework."
}

$managedBinDir = Join-Path $projectDir ("bin\Release\" + $targetFramework + "\" + $Runtime)
$managedDllPath = Join-Path $managedBinDir ($assemblyName + ".dll")

if (![string]::IsNullOrWhiteSpace($CopyFromDll)) {
    if (!(Test-Path $CopyFromDll)) {
        throw "CopyFromDll not found: $CopyFromDll"
    }
    New-Item -ItemType Directory -Path $managedBinDir -Force | Out-Null
    Write-Host "Copying golden DLL into win-x64 output (no compile):" -ForegroundColor Yellow
    Write-Host "  $CopyFromDll" -ForegroundColor DarkGray
    Write-Host "  -> $managedDllPath" -ForegroundColor DarkGray
    Copy-Item -LiteralPath $CopyFromDll -Destination $managedDllPath -Force
}

if (!(Test-Path $managedDllPath)) {
    throw @"
Managed DLL not found:
  $managedDllPath

Place your final $($assemblyName).dll there (or pass -CopyFromDll `"path\to\$assemblyName.dll`"), then re-run.
This script intentionally does not run dotnet build so your DLL is not recompiled.
"@
}

# Publish --no-build bundles from obj\Release publish inputs. Force-sync these with the requested win-x64 DLL.
$objBaseDir = Join-Path $projectDir ("obj\Release\" + $targetFramework)
$objRidDir = Join-Path $objBaseDir $Runtime
$objBaseDll = Join-Path $objBaseDir ($assemblyName + ".dll")
$objRidDll = Join-Path $objRidDir ($assemblyName + ".dll")
New-Item -ItemType Directory -Path $objBaseDir -Force | Out-Null
New-Item -ItemType Directory -Path $objRidDir -Force | Out-Null
Copy-Item -LiteralPath $managedDllPath -Destination $objBaseDll -Force
Copy-Item -LiteralPath $managedDllPath -Destination $objRidDll -Force

if ($Restore) {
    Write-Host "dotnet restore (does not rebuild main DLL)..." -ForegroundColor Yellow
    dotnet restore $projectFullPath -r $Runtime -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }
}

Write-Host "Publishing single-file EXE using --no-build (bundles from existing win-x64 layout)..." -ForegroundColor Yellow
Write-Host "Using DLL: $managedDllPath" -ForegroundColor DarkGray

dotnet publish $projectFullPath -c Release -o $PublishOutputDir --no-build `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:RuntimeIdentifier=$Runtime `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:PublishReadyToRun=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Done: $PublishOutputDir" -ForegroundColor Green
Write-Host "Bundled from: $managedDllPath" -ForegroundColor Green
