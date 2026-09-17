param(
    [string]$ProjectPath = ".\LegitX.WPF\LegitX.WPF.csproj",
    [string]$Runtime = "win-x64",
    [string]$PublishOutputDir = ".\LegitX.WPF\publish\LegitX-V2-single",
    [string]$DotfuscatorCliPath = "C:\Program Files (x86)\PreEmptive Solutions\Dotfuscator Professional Edition Evaluation 4.31.1\dotfuscator.exe",
    [string]$DotfuscatorConfigPath = ".\scripts\Dotfuscator.xml",
    [string]$StageDir = ".\LegitX.WPF\bin\Release\dotfuscator-stage",
    [string]$DotfuscatorOutputDir = ".\LegitX.WPF\bin\Release\dotfuscator-output"
)

$ErrorActionPreference = "Stop"

Write-Host "== LegitX Dotfuscator Two-Stage Release ==" -ForegroundColor Cyan

if (!(Test-Path $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}
if (!(Test-Path $DotfuscatorCliPath)) {
    throw "Dotfuscator CLI not found: $DotfuscatorCliPath"
}

# Dotfuscator 4.31 relies on ildasm.exe being present on PATH.
cmd /c "where ildasm >nul 2>nul"
if ($LASTEXITCODE -ne 0) {
    throw "ildasm.exe was not found on PATH. Install a Windows/.NET Framework SDK that provides ildasm.exe, then re-run."
}

$projectFullPath = (Resolve-Path $ProjectPath).Path
$projectDir = Split-Path $projectFullPath -Parent

Write-Host "1) Resolving project metadata..." -ForegroundColor Yellow
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

Write-Host "2) Stage 1 build (managed output for Dotfuscator)..." -ForegroundColor Yellow
dotnet build $projectFullPath -c Release -r $Runtime --self-contained true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

$managedOutputDir = Join-Path $projectDir ("bin\Release\" + $targetFramework + "\" + $Runtime)
$managedDllPath = Join-Path $managedOutputDir ($assemblyName + ".dll")
if (!(Test-Path $managedDllPath)) {
    throw ("Managed assembly not found: " + $managedDllPath)
}

$stageDirFull = if ([System.IO.Path]::IsPathRooted($StageDir)) { $StageDir } else { Join-Path (Split-Path $projectFullPath -Parent | Split-Path -Parent) $StageDir }
$dotfOutDirFull = if ([System.IO.Path]::IsPathRooted($DotfuscatorOutputDir)) { $DotfuscatorOutputDir } else { Join-Path (Split-Path $projectFullPath -Parent | Split-Path -Parent) $DotfuscatorOutputDir }
$configPathFull = if ([System.IO.Path]::IsPathRooted($DotfuscatorConfigPath)) { $DotfuscatorConfigPath } else { Join-Path (Split-Path $projectFullPath -Parent | Split-Path -Parent) $DotfuscatorConfigPath }

New-Item -ItemType Directory -Path $stageDirFull -Force | Out-Null
New-Item -ItemType Directory -Path $dotfOutDirFull -Force | Out-Null

Write-Host "3) Staging managed files for Dotfuscator..." -ForegroundColor Yellow
Copy-Item (Join-Path $managedOutputDir "*") $stageDirFull -Force
$stageDllPath = Join-Path $stageDirFull ($assemblyName + ".dll")
if (!(Test-Path $stageDllPath)) {
    throw ("Staged assembly not found: " + $stageDllPath)
}

Write-Host "4) Generating Dotfuscator config (Dotfuscator.xml)..." -ForegroundColor Yellow
& $DotfuscatorCliPath /v /in:+$stageDllPath /out:$dotfOutDirFull /rename:on /encrypt:on /controlflow:high /makeconfig:$configPathFull
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate Dotfuscator config."
}

Write-Host "5) Running Dotfuscator CLI..." -ForegroundColor Yellow
& $DotfuscatorCliPath /v $configPathFull
if ($LASTEXITCODE -ne 0) {
    throw "Dotfuscator obfuscation failed."
}

$obfDllPath = Join-Path $dotfOutDirFull ($assemblyName + ".dll")
if (!(Test-Path $obfDllPath)) {
    throw ("Obfuscated DLL not produced: " + $obfDllPath)
}

Write-Host "6) Replacing build output with obfuscated assembly..." -ForegroundColor Yellow
Copy-Item $obfDllPath $managedDllPath -Force

Write-Host "7) Stage 2 publish (single-file, self-contained, --no-build)..." -ForegroundColor Yellow
dotnet publish $projectFullPath -c Release -o $PublishOutputDir --no-build `
  -p:PublishSingleFile=true `
  -p:SelfContained=true `
  -p:RuntimeIdentifier=$Runtime `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Done: $PublishOutputDir" -ForegroundColor Green
Write-Host "Generated Dotfuscator config: $configPathFull" -ForegroundColor Green
