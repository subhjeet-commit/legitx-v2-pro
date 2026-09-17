@echo off
color 0C
title LegitX V2 - Interception Driver Uninstaller
mode con: cols=60 lines=35

echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║                                                      ║
echo  ║        LegitX V2 - Kernel Driver Uninstaller          ║
echo  ║                                                      ║
echo  ╚══════════════════════════════════════════════════════╝
echo.
echo  ┌──────────────────────────────────────────────────────┐
echo  │  INTERCEPTION KERNEL DRIVER REMOVAL                  │
echo  │                                                      │
echo  │  This will remove the Interception kernel driver     │
echo  │  from your system.                                   │
echo  │                                                      │
echo  │  After removal, LegitX V2 will automatically         │
echo  │  fall back to User Mode (WH_MOUSE_LL hook).         │
echo  └──────────────────────────────────────────────────────┘
echo.
echo  [!] REQUIRES:  Administrator privileges
echo  [!] REQUIRES:  System restart after uninstall
echo.
echo  ──────────────────────────────────────────────────────
echo   Press any key to begin uninstallation...
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

:: Uninstall the driver
echo.
echo  [~] Removing Interception kernel driver...
echo.
"%~dp0install-interception.exe" /uninstall
if %errorLevel% neq 0 (
    color 0C
    echo.
    echo  ╔══════════════════════════════════════════════════════╗
    echo  ║  [X] ERROR: Driver removal failed!                   ║
    echo  ║                                                      ║
    echo  ║  Try running as Administrator or check if the        ║
    echo  ║  driver is currently in use by another program.      ║
    echo  ╚══════════════════════════════════════════════════════╝
    echo.
    pause
    exit /b 1
)

color 0A
echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║                                                      ║
echo  ║   [OK] Interception driver removed successfully!      ║
echo  ║                                                      ║
echo  ╠══════════════════════════════════════════════════════╣
echo  ║                                                      ║
echo  ║   You MUST restart your computer for the             ║
echo  ║   driver removal to take effect.                     ║
echo  ║                                                      ║
echo  ║   After restart, LegitX V2 will automatically        ║
echo  ║   use User Mode (WH_MOUSE_LL) instead.              ║
echo  ║                                                      ║
echo  ╚══════════════════════════════════════════════════════╝
echo.
echo  ──────────────────────────────────────────────────────
set /p RESTART="   Restart now? (Y/N): "
if /i "%RESTART%"=="Y" (
    echo.
    echo  [~] Restarting in 5 seconds...
    shutdown /r /t 5 /c "Restarting after LegitX V2 Interception driver removal..."
) else (
    echo.
    echo  [!] Remember to restart for changes to take effect.
)
echo.
pause
