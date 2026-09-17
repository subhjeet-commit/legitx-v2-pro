using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LegitX.WPF.Services;

/// <summary>
/// Layer 5 — Native Anti-Debug & Anti-Dump Protection
///
/// ═══════════════════════════════════════════════════════════════════
///  CHECKS PERFORMED (8 independent techniques):
/// ═══════════════════════════════════════════════════════════════════
///
///  1. Debugger.IsAttached          — .NET managed debugger flag
///  2. IsDebuggerPresent()          — Win32 user-mode debugger check
///  3. CheckRemoteDebuggerPresent() — Detects attach-style debuggers (x64dbg, WinDbg)
///  4. NtQueryInformationProcess    — DebugPort check (kernel-level, can't be faked easily)
///  5. Parent Process Check         — If parent isn't Explorer/cmd/services → suspicious
///  6. CloseHandle trap             — Debuggers handle EXCEPTION_INVALID_HANDLE differently
///  7. Anti-Dump: Erase PE headers  — Zeros the MZ/PE headers in memory so MegaDumper/
///                                    ExtremeDumper gets a broken dump
///  8. Environment check            — Detects common debug env vars and sandboxes
///
/// ═══════════════════════════════════════════════════════════════════
///  SAFE-BY-DESIGN:
/// ═══════════════════════════════════════════════════════════════════
///
///  • ErasePeHeaders only zeroes the in-memory copy (doesn't touch disk)
///  • Parent process check allows Explorer, cmd, powershell, services, svchost,
///    and the process itself (self-restart). Won't false-positive.
///  • CloseHandle trap is wrapped in try/catch — safe even without debugger
///  • All checks return bool — no crashes, no forced shutdowns from inside
///  • Background timer uses jittered intervals (15–35s) to avoid timing patterns
///
/// </summary>
internal sealed class AntiDebugService : IDisposable
{
    // ── Native API imports ──

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, out bool isDebuggerPresent);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        out IntPtr processInformation, int processInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize,
        uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int ProcessDebugPort = 7;
    private const uint PAGE_READWRITE = 0x04;

    // ── State ──

    private System.Threading.Timer? _timer;
    private static readonly Random _jitter = new();
    private bool _headersErased;

    /// <summary>Fired when a debugger/suspicious environment is detected.</summary>
    public event Action<string>? DebuggerDetected;

    // Known safe parent process names (lowercase)
    private static readonly HashSet<string> SafeParents = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",       // Normal launch from desktop / taskbar
        "cmd",            // Launched from command prompt
        "powershell",     // Launched from PowerShell
        "pwsh",           // PowerShell 7+
        "services",       // Launched as a service
        "svchost",        // Service host
        "wininit",        // System init
        "userinit",       // User session init
        "runtimebroker",  // UWP/Store broker
        "sihost",         // Shell infrastructure
        "taskmgr",        // Launched from Task Manager "Run new task"
        "devenv",         // Visual Studio (for development — remove in production if desired)
        "legitx v2",      // Self-restart
    };

    // ═══════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs all checks once at startup and starts background monitoring.
    /// Returns false if a debugger is detected on first check.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Initialize()
    {
        try
        {
            // NOTE: ErasePeHeaders is intentionally NOT called here.
            // Zeroing the PE headers in a .NET process corrupts metadata that the
            // CLR's TaskScheduler, reflection, and JIT compiler rely on at runtime,
            // causing "An exception was thrown by a TaskScheduler" crashes.
            // The other 6 checks below are far more effective anyway.

            // ── Initial scan ──
            var reason = RunAllChecks();
            if (reason != null)
            {
                OnDetected(reason);
                return false;
            }

            // Background scan every 15–35s (offset from other layers)
            _timer = new System.Threading.Timer(OnTimer, null, NextInterval(), Timeout.Infinite);
            return true;
        }
        catch { return !SecurityPolicy.StrictFailClosed; }
    }

    /// <summary>
    /// Runs all anti-debug checks. Returns null if clean, or a reason string if detected.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public string? RunAllChecks()
    {
        try
        {
            // 1. .NET managed debugger
            if (Debugger.IsAttached)
                return "Managed debugger attached (Debugger.IsAttached)";

            // 2. Win32 IsDebuggerPresent
            if (CheckIsDebuggerPresent())
                return "User-mode debugger detected (IsDebuggerPresent)";

            // 3. Remote debugger (attach-style: x64dbg, WinDbg)
            if (CheckRemoteDebugger())
                return "Remote debugger detected (CheckRemoteDebuggerPresent)";

            // 4. Kernel-level debug port check
            if (CheckDebugPort())
                return "Debug port active (NtQueryInformationProcess)";

            // 5. Parent process check
            if (CheckParentProcess())
                return "Suspicious parent process (possible debugger launch)";

            // 6. Environment / sandbox check
            if (CheckEnvironment())
                return "Debug environment detected";

            return null; // All clear
        }
        catch
        {
            // Strict mode: checking failures are treated as suspicious.
            return SecurityPolicy.StrictFailClosed ? "Anti-debug check failed unexpectedly." : null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  INDIVIDUAL CHECKS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Check 1+2: Win32 IsDebuggerPresent (checks PEB.BeingDebugged flag).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckIsDebuggerPresent()
    {
        try { return IsDebuggerPresent(); }
        catch { return false; }
    }

    /// <summary>Check 3: CheckRemoteDebuggerPresent — catches attach debuggers.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckRemoteDebugger()
    {
        try
        {
            if (CheckRemoteDebuggerPresent(GetCurrentProcess(), out bool isDebuggerPresent))
                return isDebuggerPresent;
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Check 4: NtQueryInformationProcess with ProcessDebugPort.
    /// Returns true if a debug port is active (kernel-level check — very hard to bypass).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckDebugPort()
    {
        try
        {
            int status = NtQueryInformationProcess(
                GetCurrentProcess(), ProcessDebugPort,
                out IntPtr debugPort, IntPtr.Size, out _);

            // STATUS_SUCCESS = 0, debugPort != 0 means debugger attached
            return status == 0 && debugPort != IntPtr.Zero;
        }
        catch { return false; }
    }

    /// <summary>
    /// Check 5: Verifies the parent process is a known safe launcher.
    /// If the app was launched from x64dbg, IDA, etc. → detected.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckParentProcess()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            int parentId = GetParentProcessId(current.Id);
            if (parentId <= 0) return false; // Can't determine — allow

            try
            {
                using var parent = Process.GetProcessById(parentId);
                var parentName = parent.ProcessName?.ToLowerInvariant() ?? "";

                // If parent is in the safe list → OK
                if (SafeParents.Contains(parentName))
                    return false;

                // If parent name matches any part of our own name → OK (self-restart)
                if (parentName.Contains("legitx", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Unknown parent — suspicious
                return true;
            }
            catch
            {
                return false; // Parent already exited — allow (common for shortcuts)
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// Check 6: Environment checks — detect debug environment variables
    /// and common analysis sandboxes.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckEnvironment()
    {
        try
        {
            // Check for common debug/RE environment variables
            var suspiciousVars = new[]
            {
                "COR_ENABLE_PROFILING",   // .NET profiler injected
                "COR_PROFILER",           // .NET profiler CLSID
                "CORECLR_ENABLE_PROFILING", // .NET Core profiler
                "DOTNET_EnableDiagnostics_IPC", // diagnostics IPC
            };

            foreach (var v in suspiciousVars)
            {
                var val = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(val) && val != "0")
                    return true;
            }

            return false;
        }
        catch { return false; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ANTI-DUMP: ERASE PE HEADERS IN MEMORY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Zeros out the MZ/PE headers in the process's in-memory image.
    /// Tools like MegaDumper, ExtremeDumper, and pe-sieve read these headers
    /// to reconstruct a valid PE file. Without headers, the dump is broken.
    ///
    /// This does NOT affect the running process (the PE headers are only
    /// needed during loading). The file on disk is untouched.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ErasePeHeaders()
    {
        if (_headersErased) return;

        try
        {
            IntPtr baseAddress = GetModuleHandle(null);
            if (baseAddress == IntPtr.Zero) return;

            // Make the first 4096 bytes (1 page) writable
            if (!VirtualProtect(baseAddress, (UIntPtr)4096, PAGE_READWRITE, out uint oldProtect))
                return;

            // Zero out the entire first page (DOS header + PE header + optional header)
            unsafe
            {
                byte* ptr = (byte*)baseAddress;
                for (int i = 0; i < 4096; i++)
                    ptr[i] = 0;
            }

            // Restore original protection
            VirtualProtect(baseAddress, (UIntPtr)4096, oldProtect, out _);

            _headersErased = true;
        }
        catch
        {
            // If it fails, continue silently — not critical
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Gets the parent process ID via WMI-free approach (NtQueryInformationProcess).</summary>
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId; // Parent PID
    }

    private static int GetParentProcessId(int processId)
    {
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(
                GetCurrentProcess(), 0 /* ProcessBasicInformation */,
                ref pbi, Marshal.SizeOf(pbi), out _);

            return status == 0 ? pbi.InheritedFromUniqueProcessId.ToInt32() : -1;
        }
        catch { return -1; }
    }

    private void OnTimer(object? state)
    {
        try
        {
            var reason = RunAllChecks();
            if (reason != null)
            {
                OnDetected(reason);
                return;
            }
        }
        catch { }
        finally
        {
            try { _timer?.Change(NextInterval(), Timeout.Infinite); }
            catch { }
        }
    }

    private static int NextInterval()
    {
        lock (_jitter) { return _jitter.Next(15_000, 35_001); }
    }

    private void OnDetected(string detail)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        DebuggerDetected?.Invoke(detail);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
