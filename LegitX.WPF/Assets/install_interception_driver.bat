@echo off
color 0A
title LegitX V2 - Interception Driver Installer
mode con: cols=60 lines=35

echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║                                                      ║
echo  ║          LegitX V2 - Kernel Driver Installer          ║
echo  ║                                                      ║
echo  ╚══════════════════════════════════════════════════════╝
echo.
echo  ┌──────────────────────────────────────────────────────┐
echo  │  INTERCEPTION KERNEL DRIVER                          │
echo  │                                                      │
echo  │  This driver enables sub-0.1ms hardware-level        │
echo  │  mouse interception for maximum precision.           │
echo  │                                                      │
echo  │  Without it, LegitX V2 will use the standard         │
echo  │  Windows hook (1-5ms latency) as fallback.           │
echo  └──────────────────────────────────────────────────────┘
echo.
echo  [!] REQUIRES:  Administrator privileges
echo  [!] REQUIRES:  System restart after install
echo.
echo  ──────────────────────────────────────────────────────
echo   Press any key to begin installation...
echo  ──────────────────────────────────────────────────────
pause >nul

:: Check admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    color 0C
    echo.
    echo  ╔══════════════════════════════════════════════════════╗
    echo  ║  [X] ERROR: No administrator privileges!             ║
    echo  ║                                                      ║
    echo  ║  Right-click this file and select:                   ║
    echo  ║  "Run as administrator"                              ║
    echo  ╚══════════════════════════════════════════════════════╝
    echo.
    pause
    exit /b 1
)

:: Check that install-interception.exe exists in same folder
if not exist "%~dp0install-interception.exe" (
    color 0C
    echo.
    echo  ╔══════════════════════════════════════════════════════╗
    echo  ║  [X] ERROR: install-interception.exe not found!      ║
    echo  ║                                                      ║
    echo  ║  Make sure it is in the same folder as this file.    ║
    echo  ╚══════════════════════════════════════════════════════╝
    echo.
    pause
    exit /b 1
)

:: Install the driver
echo.
echo  [~] Installing Interception kernel driver...
echo.
"%~dp0install-interception.exe" /install
if %errorLevel% neq 0 (
    color 0C
    echo.
    echo  ╔══════════════════════════════════════════════════════╗
    echo  ║  [X] ERROR: Driver installation failed!              ║
    echo  ║                                                      ║
    echo  ║  Try running as Administrator or check antivirus.    ║
    echo  ╚══════════════════════════════════════════════════════╝
    echo.
    pause
    exit /b 1
)

color 0A
echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║                                                      ║
echo  ║   [OK] Interception driver installed successfully!    ║
echo  ║                                                      ║
echo  ╠══════════════════════════════════════════════════════╣
echo  ║                                                      ║
echo  ║   You MUST restart your computer for the             ║
echo  ║   kernel driver to become active.                    ║
echo  ║                                                      ║
echo  ║   After restart, LegitX V2 will automatically        ║
echo  ║   detect and use the kernel driver.                  ║
echo  ║                                                      ║
echo  ╚══════════════════════════════════════════════════════╝
echo.
echo  ──────────────────────────────────────────────────────
set /p RESTART="   Restart now? (Y/N): "
if /i "%RESTART%"=="Y" (
    echo.
    echo  [~] Restarting in 5 seconds...
    shutdown /r /t 5 /c "Restarting for LegitX V2 Interception driver..."
) else (
    echo.
    echo  [!] Remember to restart before using LegitX V2.
)
echo.
pause
