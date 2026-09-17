param(
    [string]$ProjectPath = ".\LegitX.WPF\LegitX.WPF.csproj",
    [string]$OutputDir = ".\LegitX.WPF\publish\LegitX-V2-hardened",
    [string]$Runtime = "win-x64",
    [ValidateSet("balanced", "aggressive")]
    [string]$ObfuscationProfile = "balanced",
    [string]$ConfuserCliPath = ".\tools\ConfuserEx\Confuser.CLI.exe",
    [switch]$SkipObfuscation,
    # Type scrambler strengthens obfuscation but can break reflective/WPF scenarios in some apps — test thoroughly if enabled.
    [switch]$UseTypeScrambler,
    # MaxProtect forces strongest preset/settings and should be regression-tested.
    [switch]$MaxProtect
)

$ErrorActionPreference = "Stop"

Write-Host "== LegitX Hardened Release ==" -ForegroundColor Cyan

if (!(Test-Path $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}

$projectFullPath = (Resolve-Path $ProjectPath).Path
$projectDir = Split-Path $projectFullPath -Parent

# .NET SDK + MSBuild can mis-resolve native apphost when the path contains "(" or ")" (e.g. "Copy (3)"), breaking single-file publish (MSB3030).
if ($projectFullPath -match '[()]') {
    throw "Project path must not contain '(' or ')' for self-contained single-file publish. Move or clone the repo to a path like C:\dev\LegitXV2 and run this script again.`nCurrent: $projectFullPath"
}

if ($MaxProtect) {
    $ObfuscationProfile = "aggressive"
    $UseTypeScrambler = $true
    Write-Host "MAX-PROTECT mode enabled: forcing aggressive profile + type scrambler." -ForegroundColor Magenta
    Write-Host "Warning: this mode can increase compatibility risk; run full runtime regression tests." -ForegroundColor DarkYellow
    Write-Host "Warning: max mode also enables Confuser compressor packer, which can increase AV/compatibility sensitivity." -ForegroundColor DarkYellow
}

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

$dllPath = Join-Path $projectDir ("bin\Release\" + $targetFramework + "\" + $assemblyName + ".dll")
$binReleaseDir = Split-Path $dllPath -Parent

Write-Host "2) Restoring and building release binaries..." -ForegroundColor Yellow
dotnet restore $projectFullPath -r $Runtime -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}
dotnet build $projectFullPath -c Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

if (-not $SkipObfuscation) {
    if (!(Test-Path $ConfuserCliPath)) {
        throw ("Confuser CLI not found: " + $ConfuserCliPath + "`nInstall ConfuserEx and pass -ConfuserCliPath.")
    }

    if (!(Test-Path $dllPath)) {
        throw ("Target DLL not found: " + $dllPath)
    }

    $obfLabel = if ($MaxProtect) { "$ObfuscationProfile / max-protect" } else { $ObfuscationProfile }
    Write-Host "3) Obfuscating assembly with ConfuserEx ($obfLabel)..." -ForegroundColor Yellow

    $obfInputDir = $binReleaseDir
    $obfOutDir = Join-Path $projectDir "bin\Release\obfuscated"
    New-Item -ItemType Directory -Path $obfOutDir -Force | Out-Null

    # Preset: aggressive = Confuser preset "maximum" or "aggressive".
    # Explicitly layered on top: harden + invalid metadata + Confuser runtime anti-debug.
    # Type scrambler (optional): more layout noise; validate WPF if you enable it.
    $rulePreset = if ($ObfuscationProfile -eq "aggressive") { "maximum" } else { "aggressive" }
    $antiDebugMode = if ($ObfuscationProfile -eq "aggressive") { "win32" } else { "safe" }

    $typeScrLine = ""
    if ($UseTypeScrambler) {
        $typeScrLine = "    <protection id=`"typescramble`" />`n"
    }
    $packerXml = if ($MaxProtect) { "  <packer id=`"compressor`" />`n" } else { "" }

    # .NET reference probe paths (needed so ConfuserEx can resolve net8 framework assemblies).
    $netCoreAppRefRoot = "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref"
    $windowsDesktopRefRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windowsdesktop.app.ref"

    $netCoreLatest = Get-ChildItem $netCoreAppRefRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    $windowsDesktopLatest = Get-ChildItem $windowsDesktopRefRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1

    $netCoreRefPath = if ($netCoreLatest) { Join-Path $netCoreLatest.FullName "ref\net8.0" } else { "" }
    $windowsDesktopRefPath = if ($windowsDesktopLatest) { Join-Path $windowsDesktopLatest.FullName "ref\net8.0" } else { "" }

    Add-Type -AssemblyName System.Security
    function Escape-XmlText([string]$s) { return [System.Security.SecurityElement]::Escape($s) }

    $probeDirs = [System.Collections.Generic.List[string]]::new()
    foreach ($p in @($obfInputDir, $netCoreRefPath, $windowsDesktopRefPath)) {
        if (![string]::IsNullOrWhiteSpace($p) -and (Test-Path $p) -and -not $probeDirs.Contains($p)) {
            $probeDirs.Add($p)
        }
    }
    # probePath list (after rule/module per Confuser XSD); always include bin output.
    if ($probeDirs.Count -eq 0) { $probeDirs.Add($obfInputDir) }

    $probePathsXml = ""
    foreach ($p in $probeDirs) {
        $probePathsXml += ("  <probePath>" + (Escape-XmlText $p) + "</probePath>`n")
    }

    $escapedOutDir = Escape-XmlText $obfOutDir
    $escapedBaseDir = Escape-XmlText $obfInputDir

    # Confuser.Core/ConfuserPrj.xsd sequence: rule*, packer?, module*, probePath*, plugin*
    $confuserProject = @"
<?xml version="1.0" encoding="utf-8"?>
<project outputDir="$escapedOutDir" baseDir="$escapedBaseDir" xmlns="http://confuser.codeplex.com">
  <rule pattern="true" preset="$rulePreset" inherit="false">
    <protection id="harden" />
    <protection id="invalid metadata" />
    <protection id="anti debug">
      <argument name="mode" value="$antiDebugMode" />
    </protection>
$typeScrLine  </rule>
$packerXml  <module path="$assemblyName.dll" />
$probePathsXml
</project>
"@

    $confuserProjPath = Join-Path $projectDir "bin\Release\confuser-temp.crproj"
    Set-Content -Path $confuserProjPath -Value $confuserProject -Encoding UTF8

    & $ConfuserCliPath -n $confuserProjPath
    if ($LASTEXITCODE -ne 0) {
        throw "ConfuserEx obfuscation failed."
    }

    $obfDll = Join-Path $obfOutDir ($assemblyName + ".dll")
    if (!(Test-Path $obfDll)) {
        throw ("Obfuscated DLL not produced: " + $obfDll)
    }

    Copy-Item -Path $obfDll -Destination $dllPath -Force
}
else {
    Write-Host "3) Obfuscation skipped by flag." -ForegroundColor DarkYellow
}

Write-Host "4) Publishing hardened single-file EXE..." -ForegroundColor Yellow
dotnet publish $projectFullPath -c Release -o $OutputDir --no-build `
  -p:PublishSingleFile=true `
  -p:SelfContained=true `
  -p:RuntimeIdentifier=$Runtime `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -p:PublishReadyToRun=true `
  -p:TieredCompilation=false `
  -p:EnableCompressionInSingleFile=true `
  -p:UseAppHost=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Done: $OutputDir" -ForegroundColor Green
Write-Host "Important: test login/license/HWID flow after obfuscation before release." -ForegroundColor DarkYellow
