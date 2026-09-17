@echo off
color 0C
title LegitX V2 - Anti-Debug Tool Cleaner
mode con: cols=65 lines=45

echo.
echo  ========================================================
echo          LegitX V2 - Debug / RE Tool Process Killer
echo  ========================================================
echo.
echo  Stopping all known debuggers, reverse engineering tools,
echo  memory inspectors, and packet sniffers...
echo.

echo  [1/8] Killing Debuggers...
taskkill /F /IM x64dbg.exe >nul 2>&1
taskkill /F /IM x32dbg.exe >nul 2>&1
taskkill /F /IM ollydbg.exe >nul 2>&1
taskkill /F /IM windbg.exe >nul 2>&1
taskkill /F /IM DbgX.Shell.exe >nul 2>&1
taskkill /F /IM idaq.exe >nul 2>&1
taskkill /F /IM idaq64.exe >nul 2>&1
taskkill /F /IM ida.exe >nul 2>&1
taskkill /F /IM ida64.exe >nul 2>&1
taskkill /F /IM immunitydebugger.exe >nul 2>&1
taskkill /F /IM radare2.exe >nul 2>&1
taskkill /F /IM r2.exe >nul 2>&1
taskkill /F /IM cutter.exe >nul 2>&1
taskkill /F /IM kd.exe >nul 2>&1
taskkill /F /IM ntsd.exe >nul 2>&1
taskkill /F /IM cdb.exe >nul 2>&1
echo        Done.

echo  [2/8] Killing Disassemblers and Decompilers...
taskkill /F /IM ghidra.exe >nul 2>&1
taskkill /F /IM ghidraRun.exe >nul 2>&1
taskkill /F /IM dnspy.exe >nul 2>&1
taskkill /F /IM dnSpy.exe >nul 2>&1
taskkill /F /IM dnSpy-x86.exe >nul 2>&1
taskkill /F /IM dotPeek64.exe >nul 2>&1
taskkill /F /IM dotPeek32.exe >nul 2>&1
taskkill /F /IM ILSpy.exe >nul 2>&1
taskkill /F /IM justdecompile.exe >nul 2>&1
taskkill /F /IM Reflector.exe >nul 2>&1
taskkill /F /IM de4dot.exe >nul 2>&1
taskkill /F /IM ildasm.exe >nul 2>&1
taskkill /F /IM dumpbin.exe >nul 2>&1
echo        Done.

echo  [3/8] Killing Memory Editors and Inspectors...
taskkill /F /IM cheatengine-x86_64.exe >nul 2>&1
taskkill /F /IM cheatengine-i386.exe >nul 2>&1
taskkill /F /IM cheatengine.exe >nul 2>&1
taskkill /F /IM CheatEngine.exe >nul 2>&1
taskkill /F /IM ce.exe >nul 2>&1
taskkill /F /IM ArtMoney.exe >nul 2>&1
taskkill /F /IM GameGuardian.exe >nul 2>&1
taskkill /F /IM MHS.exe >nul 2>&1
taskkill /F /IM ReClassEx.exe >nul 2>&1
taskkill /F /IM ReClass.NET.exe >nul 2>&1
taskkill /F /IM ReClass64.exe >nul 2>&1
taskkill /F /IM HxD.exe >nul 2>&1
taskkill /F /IM HxD64.exe >nul 2>&1
echo        Done.

echo  [4/8] Killing Process Monitors...
taskkill /F /IM procmon.exe >nul 2>&1
taskkill /F /IM procmon64.exe >nul 2>&1
taskkill /F /IM Procmon64a.exe >nul 2>&1
taskkill /F /IM procexp.exe >nul 2>&1
taskkill /F /IM procexp64.exe >nul 2>&1
taskkill /F /IM ProcessHacker.exe >nul 2>&1
taskkill /F /IM SystemInformer.exe >nul 2>&1
taskkill /F /IM pestudio.exe >nul 2>&1
taskkill /F /IM APIMonitor-x64.exe >nul 2>&1
taskkill /F /IM APIMonitor-x86.exe >nul 2>&1
echo        Done.

echo  [5/8] Killing Network Sniffers and Proxies...
taskkill /F /IM Wireshark.exe >nul 2>&1
taskkill /F /IM tshark.exe >nul 2>&1
taskkill /F /IM dumpcap.exe >nul 2>&1
taskkill /F /IM Fiddler.exe >nul 2>&1
taskkill /F /IM FiddlerEverywhere.exe >nul 2>&1
taskkill /F /IM Charles.exe >nul 2>&1
taskkill /F /IM mitmproxy.exe >nul 2>&1
taskkill /F /IM mitmweb.exe >nul 2>&1
taskkill /F /IM BurpSuiteCommunity.exe >nul 2>&1
taskkill /F /IM BurpSuitePro.exe >nul 2>&1
taskkill /F /IM HTTPDebuggerUI.exe >nul 2>&1
taskkill /F /IM HTTPDebuggerSvc.exe >nul 2>&1
taskkill /F /IM SmartSniff.exe >nul 2>&1
taskkill /F /IM NetworkMiner.exe >nul 2>&1
echo        Done.

echo  [6/8] Killing DLL Injectors and Hooking Tools...
taskkill /F /IM Xenos.exe >nul 2>&1
taskkill /F /IM Xenos64.exe >nul 2>&1
taskkill /F /IM ExtremeInjector.exe >nul 2>&1
taskkill /F /IM RemoteDLL.exe >nul 2>&1
taskkill /F /IM ProcessInjector.exe >nul 2>&1
echo        Done.

echo  [7/8] Killing Sandbox and Analysis Tools...
taskkill /F /IM sandboxie.exe >nul 2>&1
taskkill /F /IM SbieSvc.exe >nul 2>&1
taskkill /F /IM SbieCtrl.exe >nul 2>&1
taskkill /F /IM SandboxiePlus.exe >nul 2>&1
taskkill /F /IM fakenet.exe >nul 2>&1
taskkill /F /IM regshot.exe >nul 2>&1
taskkill /F /IM Autoruns.exe >nul 2>&1
taskkill /F /IM Autoruns64.exe >nul 2>&1
taskkill /F /IM ResourceHacker.exe >nul 2>&1
taskkill /F /IM reshacker.exe >nul 2>&1
taskkill /F /IM die.exe >nul 2>&1
taskkill /F /IM exeinfope.exe >nul 2>&1
taskkill /F /IM PEiD.exe >nul 2>&1
taskkill /F /IM PPEE.exe >nul 2>&1
taskkill /F /IM LordPE.exe >nul 2>&1
echo        Done.

echo  [8/8] Killing .NET Analysis Tools...
taskkill /F /IM dotnet-dump.exe >nul 2>&1
taskkill /F /IM dotnet-trace.exe >nul 2>&1
taskkill /F /IM dotnet-counters.exe >nul 2>&1
taskkill /F /IM PerfView.exe >nul 2>&1
taskkill /F /IM PerfView64.exe >nul 2>&1
taskkill /F /IM MegaDumper.exe >nul 2>&1
taskkill /F /IM ExtremeDumper.exe >nul 2>&1
taskkill /F /IM SimpleAssemblyExplorer.exe >nul 2>&1
taskkill /F /IM SAE.exe >nul 2>&1
echo        Done.

echo.
echo  ========================================================
echo   All debug / RE tools have been terminated.
echo   You may now launch LegitX V2 safely.
echo  ========================================================
echo.
pause
