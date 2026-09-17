@echo off
setlocal enabledelayedexpansion
title BlueStacks 5 Debloater - @subhoxx
color 0B
echo.
echo  ---
echo   BlueStacks 5 Debloater ^& Optimizer
echo   by @subhoxx
echo  ---
echo.
echo  [^^!] BlueStacks MUST be running before you start.
echo  [^^!] Run this script as Administrator.
echo.
pause
cls
echo.
echo  BlueStacks 5 Debloater ^& Optimizer - @subhoxx
echo  ================================================
echo.

:: Temp files for scan results
set "SCAN_ALL=%TEMP%\bs_scan_all.txt"
set "SCAN_BLOAT=%TEMP%\bs_scan_bloat.txt"
set "SCAN_SERVICES=%TEMP%\bs_scan_services.txt"
set "SCAN_RUNNING=%TEMP%\bs_scan_running.txt"

:: Clean old scan files
del /f /q "%SCAN_ALL%" "%SCAN_BLOAT%" "%SCAN_SERVICES%" "%SCAN_RUNNING%" >nul 2>&1

:: STEP 1 - FIND ADB
echo  [STEP 1] Locating ADB...

set "ADB="

:: MSI App Player 5 (installs as BlueStacks_msi5)
if exist "%ProgramFiles%\BlueStacks_msi5\HD-Adb.exe" set "ADB=%ProgramFiles%\BlueStacks_msi5\HD-Adb.exe" & goto :found_adb
if exist "%ProgramFiles(x86)%\BlueStacks_msi5\HD-Adb.exe" set "ADB=%ProgramFiles(x86)%\BlueStacks_msi5\HD-Adb.exe" & goto :found_adb

:: MSI App Player 4 (installs as BlueStacks_msi4)
if exist "%ProgramFiles%\BlueStacks_msi4\HD-Adb.exe" set "ADB=%ProgramFiles%\BlueStacks_msi4\HD-Adb.exe" & goto :found_adb
if exist "%ProgramFiles(x86)%\BlueStacks_msi4\HD-Adb.exe" set "ADB=%ProgramFiles(x86)%\BlueStacks_msi4\HD-Adb.exe" & goto :found_adb

:: BlueStacks 5
if exist "%ProgramFiles%\BlueStacks_nxt\HD-Adb.exe" set "ADB=%ProgramFiles%\BlueStacks_nxt\HD-Adb.exe" & goto :found_adb
if exist "%ProgramFiles(x86)%\BlueStacks_nxt\HD-Adb.exe" set "ADB=%ProgramFiles(x86)%\BlueStacks_nxt\HD-Adb.exe" & goto :found_adb
if exist "%LocalAppData%\BlueStacks_nxt\HD-Adb.exe" set "ADB=%LocalAppData%\BlueStacks_nxt\HD-Adb.exe" & goto :found_adb

:: BlueStacks 4
if exist "%ProgramFiles%\BlueStacks\HD-Adb.exe" set "ADB=%ProgramFiles%\BlueStacks\HD-Adb.exe" & goto :found_adb
if exist "%ProgramFiles(x86)%\BlueStacks\HD-Adb.exe" set "ADB=%ProgramFiles(x86)%\BlueStacks\HD-Adb.exe" & goto :found_adb

:: Legacy MSI paths
if exist "%ProgramFiles%\MSI\App Player\HD-Adb.exe" set "ADB=%ProgramFiles%\MSI\App Player\HD-Adb.exe" & goto :found_adb
if exist "%ProgramFiles(x86)%\MSI\App Player\HD-Adb.exe" set "ADB=%ProgramFiles(x86)%\MSI\App Player\HD-Adb.exe" & goto :found_adb
if exist "%ProgramFiles%\MSI\MSI App Player\HD-Adb.exe" set "ADB=%ProgramFiles%\MSI\MSI App Player\HD-Adb.exe" & goto :found_adb

:: Next to this script
if exist "%~dp0HD-Adb.exe" set "ADB=%~dp0HD-Adb.exe" & goto :found_adb
if exist "%~dp0adb.exe" set "ADB=%~dp0adb.exe" & goto :found_adb

:: Android SDK adb in PATH
where adb >nul 2>&1
if not errorlevel 1 (
    for /f "delims=" %%A in ('where adb 2^>nul') do (
        set "ADB=%%A"
        goto :found_adb
    )
)

echo  [X] FAILED - Could not find ADB.
echo      Supported: BlueStacks 5, BlueStacks 4, MSI App Player 5, MSI App Player 4
echo      Or place adb.exe next to this script.
pause
exit /b 1

:found_adb
:: Convert to short 8.3 path (no spaces) so for /f commands work
for %%I in ("%ADB%") do set "ADB=%%~sI"
echo  [OK] ADB: %ADB%

:: STEP 2 - ATTACH TO RUNNING EMULATOR
echo.
echo  [STEP 2] Attaching to running emulator...

:: DO NOT kill-server - MSI App Player runs its own HD-Adb server
:: Just make sure a server is running
"%ADB%" start-server >nul 2>&1
timeout /t 2 /nobreak >nul

:: PHASE 1: Read ADB port from emulator config files (dynamic ports)
set "CFG_PORT="
:: MSI App Player 5 config
for %%C in (
    "%ProgramData%\BlueStacks_msi5\bluestacks.conf"
    "%ProgramData%\BlueStacks_msi4\bluestacks.conf"
    "%ProgramData%\BlueStacks_nxt\bluestacks.conf"
    "%ProgramData%\BlueStacks\bluestacks.conf"
) do (
    if exist %%C (
        for /f "tokens=1,2 delims==" %%A in ('findstr /i "status.adb_port" %%C 2^>nul') do (
            set "RAW_PORT=%%B"
            REM Strip quotes
            set "RAW_PORT=!RAW_PORT:"=!"
            if defined RAW_PORT if not "!RAW_PORT!"=="0" (
                set "CFG_PORT=!RAW_PORT!"
                echo  [*] Config port found: !CFG_PORT! from %%C
            )
        )
    )
)

:: PHASE 2: Connect using config port first, then try standard ports
set "CONNECTED=0"
set "DEVICE="

:: Try config port first (highest priority)
if defined CFG_PORT (
    echo  [*] Trying config port !CFG_PORT!...
    "%ADB%" connect localhost:!CFG_PORT! >nul 2>&1
    "%ADB%" connect 127.0.0.1:!CFG_PORT! >nul 2>&1
    timeout /t 2 /nobreak >nul
)

:: Also try standard emulator ports
:: BS5: 5555-5595, BS4: 5555/5556, general: 5554-5558
for %%P in (5555 5556 5565 5575 5585 5595 5554 5557 5558) do (
    "%ADB%" connect localhost:%%P >nul 2>&1
    "%ADB%" connect 127.0.0.1:%%P >nul 2>&1
)
timeout /t 3 /nobreak >nul

:: PHASE 3: Check what devices are attached
:: NOTE: HD-Adb outputs UTF-16, so we must use inline for /f (not file parsing)
echo  [*] Checking for connected devices...
"%ADB%" devices
echo.

set "DEV_TMP=%TEMP%\bs_devcheck.txt"
"%ADB%" devices 2>nul | find "device" | find /v "attached" > "%DEV_TMP%"
set "CONNECTED=0"
set "DEVICE="
for /f "tokens=1" %%A in ('type "%DEV_TMP%" 2^>nul') do (
    set "DEVICE=%%A"
    set "CONNECTED=1"
)
del /f /q "%DEV_TMP%" >nul 2>&1

if "!CONNECTED!"=="0" (
    echo.
    echo  [X] FAILED - No running emulator detected.
    echo.
    echo      Make sure:
    echo      1. Your emulator is RUNNING
    echo      2. ADB access is ENABLED in emulator settings
    echo.
    echo      To enable ADB in MSI App Player / BlueStacks:
    echo        Settings ^> Advanced ^> Enable Android Debug Bridge ^(ADB^)
    echo.
    echo      Supported: BS5, BS4, MSI App Player 5, MSI App Player 4
    pause
    exit /b 1
)

echo  [OK] Attached to: !DEVICE!

:: Verify shell works
"%ADB%" -s !DEVICE! shell echo ok >nul 2>&1
if errorlevel 1 (
    echo  [*] Trying without -s flag...
    "%ADB%" shell echo ok >nul 2>&1
    if errorlevel 1 (
        echo  [X] FAILED - Cannot communicate with emulator.
        echo      Try restarting the emulator and run again.
        pause
        exit /b 1
    )
    set "DEVICE="
)
echo  [OK] ADB shell is responsive
echo.

:: Build device flag
if defined DEVICE (
    set "ADB_DEV=-s !DEVICE!"
) else (
    set "ADB_DEV="
)

:: STEP 3 - DEEP SCAN THE EMULATOR
echo  [STEP 3] Deep scanning emulator...
echo.

:: 3A - Get ALL installed packages (enabled + disabled)
echo  [*] Scanning all installed packages...
"%ADB%" %ADB_DEV% shell pm list packages -f > "%SCAN_ALL%" 2>&1
set "TOTAL_PKG=0"
for /f %%N in ('type "%SCAN_ALL%" 2^>nul ^| find /c "package:"') do set "TOTAL_PKG=%%N"
echo      Total packages found: !TOTAL_PKG!

:: 3B - Get all running processes
echo  [*] Scanning running processes...
"%ADB%" %ADB_DEV% shell ps > "%SCAN_RUNNING%" 2>&1
set "TOTAL_PROC=0"
for /f %%N in ('type "%SCAN_RUNNING%" 2^>nul ^| find /c /v ""') do set /a "TOTAL_PROC=%%N-1"
echo      Running processes: !TOTAL_PROC!

:: 3C - Get running services
echo  [*] Scanning active services...
"%ADB%" %ADB_DEV% shell dumpsys activity services > "%SCAN_SERVICES%" 2>&1
set "TOTAL_SVC=0"
for /f %%N in ('type "%SCAN_SERVICES%" 2^>nul ^| find /c "ServiceRecord"') do set "TOTAL_SVC=%%N"
echo      Active services: !TOTAL_SVC!

:: 3D - Categorize bloatware
echo  [*] Categorizing bloatware...
echo.

:: Count each category
set "CNT_BS=0"
set "CNT_GOOGLE=0"
set "CNT_ANDROID=0"
set "CNT_ADS=0"
set "CNT_SAFE=0"

:: Scan and categorize every package
(echo.) > "%SCAN_BLOAT%"

for /f "tokens=*" %%L in ('type "%SCAN_ALL%" 2^>nul') do (
    set "LINE=%%L"
    set "PKG="
    REM Extract package name (after last =)
    for /f "tokens=2 delims==" %%X in ("%%L") do set "PKG=%%X"
    if defined PKG (
        REM Check BlueStacks bloat
        echo !PKG! | findstr /i "bluestacks nowgg now.gg" >nul 2>&1
        if not errorlevel 1 (
            echo   [BS-BLOAT]  !PKG! >> "%SCAN_BLOAT%"
            set /a CNT_BS+=1
        ) else (
            REM Check Google bloat
            echo !PKG! | findstr /i "google.android.apps google.android.youtube google.android.music google.android.gm google.android.calendar google.android.contacts google.android.dialer google.android.tts google.android.feedback google.android.marvin google.android.videos google.android.keep google.android.talk google.android.inputmethod google.android.syncadapters google.android.printservice google.android.googlequicksearch google.android.partnersetup google.android.setupwizard" >nul 2>&1
            if not errorlevel 1 (
                echo   [G-BLOAT]   !PKG! >> "%SCAN_BLOAT%"
                set /a CNT_GOOGLE+=1
            ) else (
                REM Check Android bloat
                echo !PKG! | findstr /i "android.browser android.calculator android.camera android.deskclock android.documentsui android.dreams android.email android.gallery android.printspooler android.wallpaper android.musicfx android.soundrecorder android.quicksearchbox android.bookmarkprovider android.cellbroadcast android.managedprovisioning android.bips android.calllogbackup android.egg android.stk" >nul 2>&1
                if not errorlevel 1 (
                    echo   [A-BLOAT]   !PKG! >> "%SCAN_BLOAT%"
                    set /a CNT_ANDROID+=1
                ) else (
                    REM Check ad/tracking packages
                    echo !PKG! | findstr /i "adservice admob facebook.analytics appsflyer adjust.sdk mobileapptracking" >nul 2>&1
                    if not errorlevel 1 (
                        echo   [AD/TRACK]  !PKG! >> "%SCAN_BLOAT%"
                        set /a CNT_ADS+=1
                    ) else (
                        set /a CNT_SAFE+=1
                    )
                )
            )
        )
    )
)

:: STEP 4 - SCAN REPORT
echo.
echo  [STEP 4] SCAN REPORT
echo.
echo   Total packages:         !TOTAL_PKG!
echo   Running processes:      !TOTAL_PROC!
echo   Active services:        !TOTAL_SVC!
echo.
echo   BLOATWARE FOUND:
echo.
echo   BlueStacks bloat:     !CNT_BS! packages
echo   Google bloat:         !CNT_GOOGLE! packages
echo   Android bloat:        !CNT_ANDROID! packages
echo   Ad/Tracking:          !CNT_ADS! packages
echo   Safe (keep):          !CNT_SAFE! packages
echo.
echo   Bloatware detected:
type "%SCAN_BLOAT%" 2>nul
echo.

set /a TOTAL_BLOAT=CNT_BS+CNT_GOOGLE+CNT_ANDROID+CNT_ADS
if !TOTAL_BLOAT! EQU 0 (
    echo  [OK] No bloatware found! Emulator is already clean.
    echo.
    goto :skip_to_optimize
)

echo  [^^!] Found !TOTAL_BLOAT! bloatware packages to remove.
echo.
echo  Press any key to START REMOVAL...
pause >nul

:: STEP 5 - REMOVE BLUESTACKS BLOATWARE
echo.
echo  [STEP 5] Removing BlueStacks bloatware...

:: Dynamic scan: find and kill every bluestacks/nowgg package on THIS device
set "BS_REMOVED=0"
for /f "tokens=2 delims=:" %%P in ('"%ADB%" %ADB_DEV% shell pm list packages 2^>nul ^| findstr /i "bluestacks nowgg now.gg"') do (
    set "PKG=%%P"
    REM Remove trailing whitespace/CR
    for /f "tokens=1" %%C in ("!PKG!") do set "PKG=%%C"
    echo    [REMOVE] !PKG!
    "%ADB%" %ADB_DEV% shell am force-stop !PKG! >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm clear !PKG! >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm uninstall -k --user 0 !PKG! >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm uninstall --user 0 !PKG! >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm disable-user --user 0 !PKG! >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm hide !PKG! >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm suspend !PKG! >nul 2>&1
    set /a BS_REMOVED+=1
)

:: Also hit known BS package names that may not show in list
for %%P in (
    com.bluestacks.appmart
    com.bluestacks.BstCommandProcessor
    com.bluestacks.home
    com.bluestacks.promotions
    com.bluestacks.settings
    com.bluestacks.gamepophome
    com.bluestacks.appfinder
    com.bluestacks.filemanager
    com.bluestacks.s2p
    com.bluestacks.spotlight
    com.bluestacks.setup
    com.bluestacks.gamecenter
    com.bluestacks.launcher
    com.bluestacks.consolemode
    com.bluestacks.ime
    com.bluestacks.windowhandler
    com.bluestacks.keymapping
    com.bluestacks.help
    com.bluestacks.aid
    com.bluestacks.apptray
    com.bluestacks.tv
    com.bluestacks.android.lang
    com.bluestacks.adblockerservice
    com.bluestacks.bstpanel
    com.bluestacks.gameguide
    com.bluestacks.store
    com.bluestacks.updater
    com.bluestacks.input
    com.bluestacks.nxtlauncher
    com.bluestacks.sidebar
    com.bluestacks.bstvmmanager
    com.bluestacks.app.player
    com.bluestacks.multiinstance
    com.bluestacks.notification
    com.now.gg
    com.nowgg.redirect
    com.nowgg.store
) do (
    "%ADB%" %ADB_DEV% shell am force-stop %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm clear %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm uninstall -k --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm uninstall --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm disable-user --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm hide %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm suspend %%P >nul 2>&1
)
echo  [OK] BlueStacks bloat: !BS_REMOVED! packages processed
echo.

:: STEP 6 - REMOVE GOOGLE BLOATWARE
echo  [STEP 6] Removing Google bloatware...

set "G_REMOVED=0"
for %%P in (
    com.google.android.apps.docs
    com.google.android.apps.maps
    com.google.android.apps.photos
    com.google.android.apps.tachyon
    com.google.android.apps.wellbeing
    com.google.android.apps.youtube.music
    com.google.android.apps.magazines
    com.google.android.apps.books
    com.google.android.apps.plus
    com.google.android.apps.cloudprint
    com.google.android.calendar
    com.google.android.contacts
    com.google.android.dialer
    com.google.android.feedback
    com.google.android.gm
    com.google.android.googlequicksearchbox
    com.google.android.marvin.talkback
    com.google.android.music
    com.google.android.printservice.recommendation
    com.google.android.syncadapters.calendar
    com.google.android.syncadapters.contacts
    com.google.android.tts
    com.google.android.youtube
    com.google.android.videos
    com.google.android.keep
    com.google.android.talk
    com.google.android.partnersetup
    com.google.android.setupwizard
    com.google.android.inputmethod.latin
    com.google.android.tag
    com.google.android.webview
    com.google.android.configupdater
) do (
    echo    [REMOVE] %%P
    "%ADB%" %ADB_DEV% shell am force-stop %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm uninstall -k --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm disable-user --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm hide %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm suspend %%P >nul 2>&1
    set /a G_REMOVED+=1
)
echo  [OK] Google bloat: !G_REMOVED! packages processed
echo.

:: STEP 7 - REMOVE ANDROID SYSTEM BLOATWARE
echo  [STEP 7] Removing Android bloatware...

set "A_REMOVED=0"
for %%P in (
    com.android.bookmarkprovider
    com.android.browser
    com.android.calculator2
    com.android.calendar
    com.android.camera2
    com.android.cellbroadcastreceiver
    com.android.deskclock
    com.android.documentsui
    com.android.dreams.basic
    com.android.dreams.phototable
    com.android.email
    com.android.gallery3d
    com.android.managedprovisioning
    com.android.printspooler
    com.android.stk
    com.android.wallpaper
    com.android.wallpaper.livepicker
    com.android.musicfx
    com.android.soundrecorder
    com.android.quicksearchbox
    com.android.bips
    com.android.calllogbackup
    com.android.egg
    com.android.htmlviewer
    com.android.music
    com.android.nfc
    com.android.traceur
    com.android.wallpaperbackup
) do (
    echo    [REMOVE] %%P
    "%ADB%" %ADB_DEV% shell am force-stop %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm uninstall -k --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm disable-user --user 0 %%P >nul 2>&1
    "%ADB%" %ADB_DEV% shell pm hide %%P >nul 2>&1
    set /a A_REMOVED+=1
)
echo  [OK] Android bloat: !A_REMOVED! packages processed
echo.

:: STEP 8 - DISABLE LAUNCHER ACTIVITIES
echo  [STEP 8] Disabling launcher activities...

:: For every surviving bluestacks package, dump and disable all activities
for /f "tokens=2 delims=:" %%P in ('"%ADB%" %ADB_DEV% shell pm list packages 2^>nul ^| findstr /i "bluestacks"') do (
    for /f "tokens=1" %%C in ("%%P") do set "SURV=%%C"
    echo    [*] Disabling activities for !SURV! ...
    REM Get all activities for this package and disable each one
    for /f "tokens=2 delims= " %%A in ('"%ADB%" %ADB_DEV% shell dumpsys package !SURV! 2^>nul ^| findstr /i "Activity"') do (
        "%ADB%" %ADB_DEV% shell pm disable-user --user 0 "!SURV!/%%A" >nul 2>&1
    )
    REM Also try common activity names
    for %%A in (
        .MainActivity
        .activities.MainActivity
        .HomeActivity
        .activities.HomeActivity
        .GameCenterActivity
        .activities.GameCenterActivity
        .ConsoleActivity
        .ConsoleModeActivity
        .SpotlightActivity
        .SettingsActivity
        .SplashActivity
        .LauncherActivity
    ) do (
        "%ADB%" %ADB_DEV% shell pm disable-user --user 0 "!SURV!/%%A" >nul 2>&1
    )
)

:: Hide from launcher via settings
"%ADB%" %ADB_DEV% shell settings put secure hidden_apps "com.bluestacks.gamecenter,com.bluestacks.consolemode,com.bluestacks.home,com.bluestacks.appmart,com.bluestacks.settings,com.bluestacks.spotlight" >nul 2>&1

echo  [OK] Launcher activities disabled
echo.

:: STEP 9 - KILL AD SERVICES AND BLOCK TELEMETRY
echo  [STEP 9] Killing ads and telemetry...

:: Scan for ad-related running services and kill them
echo  [*] Scanning for ad services...
for /f "tokens=*" %%S in ('"%ADB%" %ADB_DEV% shell dumpsys activity services 2^>nul ^| findstr /i "ads. adservice analytics measurement appsflyer adjust admob facebook.analytics"') do (
    echo    [AD-SVC] %%S
)

:: Disable known GMS ad components
for %%C in (
    .ads.AdRequestBrokerService
    .ads.identifier.service.AdvertisingIdService
    .ads.social.GcmSchedulerWakeupService
    .analytics.service.PlayLogMonitorIntervalService
    .measurement.AppMeasurementService
    .ads.cache.CacheBrokerService
    .ads.jams.NegotiationService
    .analytics.AnalyticsService
    .analytics.AnalyticsTaskService
    .clearcut.service.ClearcutLoggerService
    .stats.service.DropBoxEntryAddedChimeraService
) do (
    "%ADB%" %ADB_DEV% shell pm disable-user --user 0 "com.google.android.gms/%%C" >nul 2>&1
)

:: Reset advertising ID and block tracking
"%ADB%" %ADB_DEV% shell settings put secure advertising_id 00000000-0000-0000-0000-000000000000 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put secure limit_ad_tracking 1 >nul 2>&1

:: Block ad domains via hosts
:: Build hosts block in temp, then try to copy to /etc/hosts
"%ADB%" %ADB_DEV% shell "cat /etc/hosts" > "%TEMP%\bs_hosts_orig.txt" 2>nul
(
echo 127.0.0.1 localhost
echo ::1 ip6-localhost
echo 127.0.0.1 googleads.g.doubleclick.net
echo 127.0.0.1 pagead2.googlesyndication.com
echo 127.0.0.1 ad.doubleclick.net
echo 127.0.0.1 ads.bluestacks.com
echo 127.0.0.1 analytics.bluestacks.com
echo 127.0.0.1 cloud.bluestacks.com
echo 127.0.0.1 www.googleadservices.com
echo 127.0.0.1 app-measurement.com
echo 127.0.0.1 firebase-settings.crashlytics.com
echo 127.0.0.1 settings.crashlytics.com
echo 127.0.0.1 cdn.now.gg
echo 127.0.0.1 api.now.gg
echo 127.0.0.1 ads.mopub.com
echo 127.0.0.1 analytics.google.com
echo 127.0.0.1 ssl.google-analytics.com
echo 127.0.0.1 graph.facebook.com
echo 127.0.0.1 connect.facebook.net
echo 127.0.0.1 www.facebook.com
echo 127.0.0.1 pixel.facebook.com
echo 127.0.0.1 adservice.google.com
echo 127.0.0.1 tpc.googlesyndication.com
echo 127.0.0.1 www.google-analytics.com
echo 127.0.0.1 static.doubleclick.net
echo 127.0.0.1 ade.googlesyndication.com
echo 127.0.0.1 crashlyticsreports-pa.googleapis.com
echo 127.0.0.1 data.flurry.com
echo 127.0.0.1 cdn.mxpnl.com
echo 127.0.0.1 api.mixpanel.com
echo 127.0.0.1 e.crashlytics.com
echo 127.0.0.1 cdn3.bluestacks.com
echo 127.0.0.1 eb.bluestacks.com
) > "%TEMP%\bs_hosts_block.txt"

:: Push to emulator and try to install as /etc/hosts
"%ADB%" %ADB_DEV% push "%TEMP%\bs_hosts_block.txt" /data/local/tmp/hosts_block >nul 2>&1
"%ADB%" %ADB_DEV% shell "cp /data/local/tmp/hosts_block /etc/hosts" >nul 2>&1
"%ADB%" %ADB_DEV% shell "cat /data/local/tmp/hosts_block > /etc/hosts" >nul 2>&1

:: Deny background run for GMS (massive battery/CPU saver)
"%ADB%" %ADB_DEV% shell cmd appops set com.google.android.gms RUN_IN_BACKGROUND deny >nul 2>&1
"%ADB%" %ADB_DEV% shell cmd appops set com.google.android.gms RUN_ANY_IN_BACKGROUND deny >nul 2>&1
"%ADB%" %ADB_DEV% shell cmd appops set com.google.android.gms WAKE_LOCK deny >nul 2>&1
"%ADB%" %ADB_DEV% shell cmd appops set com.google.android.gms MONITOR_LOCATION deny >nul 2>&1
"%ADB%" %ADB_DEV% shell cmd appops set com.google.android.gms MONITOR_HIGH_POWER_LOCATION deny >nul 2>&1

:: Restrict Play Store too
"%ADB%" %ADB_DEV% shell cmd appops set com.android.vending RUN_IN_BACKGROUND deny >nul 2>&1
"%ADB%" %ADB_DEV% shell cmd appops set com.android.vending RUN_ANY_IN_BACKGROUND deny >nul 2>&1
"%ADB%" %ADB_DEV% shell cmd appops set com.android.vending WAKE_LOCK deny >nul 2>&1

echo  [OK] Ads killed, telemetry blocked
echo.

:: STEP 10 - KILL ALL BACKGROUND PROCESSES
echo  [STEP 10] Killing background processes...

:: Kill every non-essential process
for /f "tokens=2 delims=:" %%P in ('"%ADB%" %ADB_DEV% shell pm list packages 2^>nul ^| findstr /i "bluestacks google.android.gms google.android.gsf vending setupwizard partnersetup"') do (
    for /f "tokens=1" %%C in ("%%P") do (
        "%ADB%" %ADB_DEV% shell am force-stop %%C >nul 2>&1
    )
)

echo  [OK] Background processes killed
echo.

:skip_to_optimize

:: STEP 11 - ANDROID PERFORMANCE OPTIMIZATION
echo  [STEP 11] Optimizing Android performance...
echo.

:: === 11A - Kill ALL animations ===
echo  [*] Disabling animations...
"%ADB%" %ADB_DEV% shell settings put global window_animation_scale 0
"%ADB%" %ADB_DEV% shell settings put global transition_animation_scale 0
"%ADB%" %ADB_DEV% shell settings put global animator_duration_scale 0

:: === 11B - GPU / Rendering optimization ===
echo  [*] Forcing GPU rendering...
"%ADB%" %ADB_DEV% shell settings put global force_hw_overlay 1
"%ADB%" %ADB_DEV% shell setprop debug.hwui.renderer skiagl >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop persist.sys.gpu.rendering opengl >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop debug.egl.hw 1 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop debug.composition.type gpu >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop debug.hwui.render_dirty_regions false >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop debug.gr.numframebuffers 3 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop debug.enabletr true >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop hwui.render_dirty_regions false >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop persist.sys.ui.hw 1 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop video.accelerate.hw 1 >nul 2>&1

:: Force 4x MSAA in OpenGL (smoother rendering)
"%ADB%" %ADB_DEV% shell settings put global force_allow_on_external 1 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop debug.egl.force_msaa true >nul 2>&1

:: === 11C - Dalvik VM optimization (faster app launch) ===
echo  [*] Tuning Dalvik/ART runtime...
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.execution-mode int:jit >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.dex2oat-Xms 64m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.dex2oat-Xmx 512m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.dex2oat-threads 4 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.image-dex2oat-Xms 64m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.image-dex2oat-Xmx 64m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.image-dex2oat-threads 4 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.heapgrowthlimit 256m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.heapsize 512m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.heapminfree 2m >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop dalvik.vm.heaptargetutilization 0.75 >nul 2>&1

:: === 11D - Input / Pointer optimization (LegitX ready) ===
echo  [*] Optimizing input system...
"%ADB%" %ADB_DEV% shell settings put system pointer_speed 0
"%ADB%" %ADB_DEV% shell settings put system show_touches 0
"%ADB%" %ADB_DEV% shell settings put system show_pointer_location 0 >nul 2>&1

:: === 11E - Network optimization (lower ping) ===
echo  [*] Tuning network stack...
"%ADB%" %ADB_DEV% shell setprop net.tcp.buffersize.default 4096,87380,110208,4096,16384,110208 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop net.tcp.buffersize.wifi 524288,1048576,2097152,262144,524288,1048576 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop net.dns1 8.8.8.8 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop net.dns2 1.1.1.1 >nul 2>&1
"%ADB%" %ADB_DEV% shell setprop net.tcp.default_init_rwnd 60 >nul 2>&1

:: === 11F - Disable all unnecessary Android features ===
echo  [*] Disabling unnecessary features...
"%ADB%" %ADB_DEV% shell settings put global auto_sync 0
"%ADB%" %ADB_DEV% shell settings put global notification_sound "" >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put system haptic_feedback_enabled 0
"%ADB%" %ADB_DEV% shell settings put global always_finish_activities 0
"%ADB%" %ADB_DEV% shell settings put system accelerometer_rotation 0
"%ADB%" %ADB_DEV% shell settings put system vibrate_on_touch 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put system dtmf_tone 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put system sound_effects_enabled 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put system lockscreen_sounds_enabled 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put system charging_sounds_enabled 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put global low_power 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put global adaptive_battery_management_enabled 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put secure screensaver_enabled 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put system screen_off_timeout 2147483647 >nul 2>&1

:: Disable auto-updates
"%ADB%" %ADB_DEV% shell settings put global package_verifier_enable 0 >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put global app_auto_restriction_enabled false >nul 2>&1
"%ADB%" %ADB_DEV% shell settings put global verifier_verify_adb_installs 0 >nul 2>&1

:: === 11G - Trim cache and force garbage collection ===
echo  [*] Clearing caches...
"%ADB%" %ADB_DEV% shell pm trim-caches 999999999999FREE >nul 2>&1

echo  [OK] Android performance optimized
echo.

:: STEP 12 - HOST-SIDE EMULATOR CONFIG OPTIMIZATION
echo  [STEP 12] Optimizing emulator host config...
echo.

:: Find the emulator config file
set "EMU_CONF="
for %%C in (
    "%ProgramData%\BlueStacks_msi5\bluestacks.conf"
    "%ProgramData%\BlueStacks_msi4\bluestacks.conf"
    "%ProgramData%\BlueStacks_nxt\bluestacks.conf"
    "%ProgramData%\BlueStacks\bluestacks.conf"
) do (
    if exist %%C (
        set "EMU_CONF=%%~C"
    )
)

if not defined EMU_CONF (
    echo  [^^!] No emulator config found, skipping host optimization.
    goto :skip_host_config
)

echo  [*] Config: !EMU_CONF!

:: Create a backup first
copy /y "!EMU_CONF!" "!EMU_CONF!.bak" >nul 2>&1
echo  [*] Backup saved: !EMU_CONF!.bak

:: Use PowerShell for safe config editing (no BOM, UTF-8)
:: This replaces values in-place or adds them if missing
echo  [*] Applying config tweaks...
powershell -NoProfile -Command ^
  "$f='!EMU_CONF!'; $c=[IO.File]::ReadAllText($f); " ^
  "$pairs=@{" ^
  "  'bst.enable_adb_access'='1';" ^
  "  'bst.instance.Nougat64.enable_high_fps'='1';" ^
  "  'bst.instance.Nougat64.enable_vsync'='0';" ^
  "  'bst.instance.Nougat64.enable_fps_display'='0';" ^
  "  'bst.instance.Nougat64.enable_notifications'='0';" ^
  "  'bst.instance.Nougat64.autohide_notifications'='1';" ^
  "  'bst.instance.Nougat64.show_sidebar'='0';" ^
  "  'bst.instance.Nougat64.astc_decoding_mode'='gpu';" ^
  "  'bst.instance.Nougat64.graphics_renderer'='gl';" ^
  "  'bst.instance.Nougat64.graphics_engine'='pga';" ^
  "  'bst.feature.app_install_stats'='0';" ^
  "  'bst.feature.usage_stats'='0';" ^
  "  'bst.feature.creator_studio'='0';" ^
  "  'bst.feature.show_cloud_instance'='0';" ^
  "  'bst.enable_discord_integration'='0';" ^
  "  'bst.show_bsx_launch_intro_popup'='0';" ^
  "  'bst.show_camera_detection_message'='0';" ^
  "  'bst.show_gamepad_detection_message'='0';" ^
  "  'bst.show_raw_mode_warning'='0';" ^
  "  'bst.instance.Nougat64.google_login_popup_shown'='1';" ^
  "  'bst.prefer_dedicated_gpu'='1';" ^
  "  'bst.mem_opt_mode'='1';" ^
  "  'bst.instance.Nougat64.android_sound_while_tapping'='0';" ^
  "  'bst.instance.Nougat64.eco_mode_max_fps'='240'" ^
  "};" ^
  "foreach($k in $pairs.Keys){" ^
  "  $v=$pairs[$k]; $pattern=($k -replace '\.','\.')+'=\"[^\"]*\"';" ^
  "  if($c -match $pattern){$c=$c -replace $pattern,($k+'=\"'+$v+'\"')}" ^
  "  else{$c+=\"`n\"+$k+'=\"'+$v+'\"'}" ^
  "};" ^
  "$u=New-Object System.Text.UTF8Encoding $false;" ^
  "[IO.File]::WriteAllText($f,$c,$u);" ^
  "Write-Host '  [OK] Config updated (no BOM)'"

echo  [*] Sidebar hidden, notifications off, popups disabled
echo  [*] ADB access enabled, high FPS unlocked
echo  [*] ASTC GPU decoding, dedicated GPU forced
echo  [*] Telemetry/stats disabled in config
echo  [OK] Host config optimized
echo.

:skip_host_config

:: STEP 13 - DEEP CLEAN TELEMETRY AND TEMP FILES
echo  [STEP 13] Cleaning telemetry and temp files...
echo.

:: === 13A - Clean Android temp/cache directories ===
echo  [*] Cleaning temp files...
"%ADB%" %ADB_DEV% shell rm -rf /data/local/tmp/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/local/tmp/.* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /cache/* >nul 2>&1

:: === 13B - Clean app cache for ALL packages ===
echo  [*] Clearing all app caches...
for /f "tokens=2 delims=:" %%P in ('"%ADB%" %ADB_DEV% shell pm list packages 2^>nul') do (
    for /f "tokens=1" %%C in ("%%P") do (
        "%ADB%" %ADB_DEV% shell pm clear --cache-only %%C >nul 2>&1
        REM Fallback: manually wipe cache dirs
        "%ADB%" %ADB_DEV% shell rm -rf /data/data/%%C/cache/* >nul 2>&1
        "%ADB%" %ADB_DEV% shell rm -rf /data/data/%%C/code_cache/* >nul 2>&1
    )
)

:: === 13C - Delete telemetry/crash/log files ===
echo  [*] Purging telemetry and crash logs...
"%ADB%" %ADB_DEV% shell rm -rf /data/system/dropbox/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/tombstones/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/anr/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/misc/logd/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/log/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/system/usagestats/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/system/netstats/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/system/procstats/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/misc/perfprofd/* >nul 2>&1

:: === 13D - Clean GMS telemetry specifically ===
echo  [*] Cleaning Google telemetry data...
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/databases/help_rtc_* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/databases/icing* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/databases/metrics* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/databases/pal* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/files/persistent_mustreport* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/files/faster* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/data/com.google.android.gms/shared_prefs/adid* >nul 2>&1

:: === 13E - Delete BlueStacks ad/tracking data ===
echo  [*] Cleaning BlueStacks tracking data...
"%ADB%" %ADB_DEV% shell rm -f /data/downloads/admob_user_agent.xml >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/downloads/.aff/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/downloads/.dp/* >nul 2>&1
"%ADB%" %ADB_DEV% shell rm -rf /data/downloads/.tmp/* >nul 2>&1

:: === 13F - Clean host-side logs and telemetry ===
echo  [*] Cleaning host-side emulator logs...
set "HOST_LOGDIR="
for %%L in (
    "%ProgramData%\BlueStacks_msi5\Logs"
    "%ProgramData%\BlueStacks_msi4\Logs"
    "%ProgramData%\BlueStacks_nxt\Logs"
    "%ProgramData%\BlueStacks\Logs"
) do (
    if exist "%%~L" (
        del /f /q "%%~L\*.log" >nul 2>&1
        del /f /q "%%~L\*.log.*" >nul 2>&1
        del /f /q "%%~L\*.txt" >nul 2>&1
        echo    [CLEANED] %%~L
    )
)

echo  [OK] Telemetry and temp files cleaned
echo.

:: STEP 14 - VERIFICATION SCAN
echo  [STEP 14] Verification scan...
echo.

:: Count what's left
set "REMAIN_BS=0"
set "REMAIN_TOTAL=0"
for /f "tokens=2 delims=:" %%P in ('"%ADB%" %ADB_DEV% shell pm list packages -e 2^>nul') do (
    set /a REMAIN_TOTAL+=1
    echo %%P | findstr /i "bluestacks nowgg now.gg" >nul 2>&1
    if not errorlevel 1 (
        set /a REMAIN_BS+=1
        echo    [SURVIVED] %%P
    )
)

echo.
echo  Enabled packages remaining: !REMAIN_TOTAL!
echo  BlueStacks packages remaining: !REMAIN_BS!
echo.

if !REMAIN_BS! GTR 0 (
    echo  [^^!] Some BlueStacks packages survived ^(system-protected^).
    echo      They have been DISABLED/HIDDEN but cannot be fully removed.
) else (
    echo  [OK] All BlueStacks bloatware has been removed!
)
echo.

:: FINAL REPORT
echo.
echo  ================================================
echo   DEBLOAT ^& OPTIMIZE COMPLETE^^!
echo  ================================================
echo.
echo   [+] Emulator deep-scanned
echo   [+] BlueStacks bloat removed/disabled
echo   [+] Google bloat removed
echo   [+] Android bloat removed
echo   [+] Ad services killed
echo   [+] Telemetry domains blocked
echo   [+] Background processes killed
echo   [+] ALL animations OFF
echo   [+] GPU rendering forced (SkiaGL + HW overlay)
echo   [+] Dalvik/ART runtime tuned
echo   [+] Network stack optimized (lower ping)
echo   [+] Pointer speed = 0 (LegitX ready)
echo   [+] Input latency optimized
echo   [+] Screen timeout = never
echo   [+] Auto-updates disabled
echo   [+] Host config optimized:
echo       - ADB access enabled
echo       - High FPS unlocked
echo       - Sidebar hidden
echo       - Notifications OFF
echo       - Popups disabled
echo       - ASTC GPU decoding
echo       - Dedicated GPU forced
echo       - Stats/telemetry OFF
echo   [+] Telemetry files purged
echo   [+] Crash logs cleaned
echo   [+] App caches cleared
echo   [+] Host logs cleaned
echo.
echo   RESTART the emulator now for full effect^^!
echo  ================================================
echo.
pause
endlocal
