#Requires -Version 5.1
<#
.SYNOPSIS
  Ensures .NET 8 Windows Desktop Runtime (x64) is available for framework-dependent WPF apps.
.DESCRIPTION
  Detects dotnet + Microsoft.WindowsDesktop.App 8.x via `dotnet --list-runtimes`.
  If missing, tries `winget install Microsoft.DotNet.DesktopRuntime.8`. If that fails, prints manual steps.
#>
param(
    [ValidatePattern('^\d+$')]
    [string]$MinimumMajor = "8",
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Get-DotNetExe {
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add((Join-Path $env:ProgramFiles "dotnet\dotnet.exe"))
    $pf86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    if ($pf86) { $candidates.Add((Join-Path $pf86 "dotnet\dotnet.exe")) }
    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Test-DesktopRuntime {
    param([Parameter(Mandatory)][string]$DotNetExe)
    $lines = & $DotNetExe --list-runtimes 2>&1
    if ($LASTEXITCODE -ne 0) { return $false }
    $text = $lines -join "`n"
    # e.g. Microsoft.WindowsDesktop.App 8.0.21 [C:\Program Files\dotnet\shared\...]
    return $text -match "Microsoft\.WindowsDesktop\.App\s+$MinimumMajor\."
}

function Invoke-WingetInstallDesktopRuntime8 {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Host "winget is not available on this PC (older Windows or disabled)." -ForegroundColor Yellow
        return $false
    }

    Write-Host "Installing Microsoft .NET 8 Windows Desktop Runtime via winget (may prompt for elevation)..." -ForegroundColor Cyan
    $args = @(
        "install", "--id", "Microsoft.DotNet.DesktopRuntime.8",
        "-e",
        "--silent",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity"
    )

    $p = Start-Process -FilePath $winget.Source -ArgumentList $args -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        Write-Host "winget finished with exit code $($p.ExitCode)." -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 2
    $dn = Get-DotNetExe
    if ($dn -and (Test-DesktopRuntime -DotNetExe $dn)) { return $true }
    return $false
}

function Show-ManualInstall {
    Write-Host @"

------------------------------------------------------------------
 Manual install required — .NET 8 **Desktop** Runtime (x64)
------------------------------------------------------------------
1) Open: https://dotnet.microsoft.com/download/dotnet/8.0
2) Under ""Run desktop apps"", download Windows Desktop Runtime (x64), not the plain .NET Runtime only.
3) Run the installer, then start LegitX V2 again.

If winget failed with a certificate error, fix TLS/proxy or install from the link above.
------------------------------------------------------------------
"@ -ForegroundColor Yellow
}

# --- x64 guard (this build is win-x64) ---
if ([Environment]::Is64BitOperatingSystem -ne $true) {
    Write-Error "LegitX V2 (this build) requires 64-bit Windows."
    exit 1
}

$dotnet = Get-DotNetExe
if (-not $dotnet) {
    Write-Host "dotnet host not found under Program Files or PATH." -ForegroundColor Yellow
    if (-not (Invoke-WingetInstallDesktopRuntime8)) {
        Show-ManualInstall
        exit 1
    }
    $dotnet = Get-DotNetExe
    if (-not $dotnet) {
        Show-ManualInstall
        exit 1
    }
}

if (Test-DesktopRuntime -DotNetExe $dotnet) {
    if (-not $Quiet) { Write-Host "OK: Microsoft.WindowsDesktop.App $MinimumMajor.x is installed." -ForegroundColor Green }
    exit 0
}

Write-Host "Microsoft.WindowsDesktop.App $MinimumMajor.x was not found." -ForegroundColor Yellow
if (Invoke-WingetInstallDesktopRuntime8) {
    $dotnet = Get-DotNetExe
    if (-not $dotnet) { Show-ManualInstall; exit 1 }
    if (Test-DesktopRuntime -DotNetExe $dotnet) {
        Write-Host "OK: Desktop runtime installed." -ForegroundColor Green
        exit 0
    }
}

Show-ManualInstall
exit 1
