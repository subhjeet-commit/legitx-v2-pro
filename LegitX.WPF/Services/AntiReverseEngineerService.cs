using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LegitX.WPF.Services;

/// <summary>Layer 3 — Anti-dnSpy / Anti-Decompiler (Process Name Check)</summary>
internal sealed class AntiReverseEngineerService : IDisposable
{
    private System.Threading.Timer? _timer;
    private static readonly Random _jitter = new();

    public event Action<string>? ToolDetected;

    // Process names (without .exe) — lowercase, EXACT match only
    private static readonly HashSet<string> BannedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET decompilers / editors
        "dnspy", "dnspy-x86", "dnspy.console",
        "ilspy", "avaloniailspy",
        "dotpeek", "dotpeek64",
        "justdecompile",
        "de4dot", "de4dot-x64",
        "reflexil",

        // Debuggers
        "x64dbg", "x32dbg",
        "ollydbg",
        "windbg", "windbgx",
        "ida64", "idag", "idag64", "idaq", "idaq64",
        "immunitydebugger",
        "radare2",
        "ghidra", "ghidrarun",

        // Memory editors
        "cheatengine-x86_64", "cheatengine-i386", "cheatengine",

        // Process inspectors
        "processhacker",
        "apimonitor-x86", "apimonitor-x64",

        // .NET profilers / IL manipulators
        "dotnet-dump", "dotnet-trace",
        "mdbg",

        // Packet sniffers
        "fiddler", "fiddlereverywhere",
        "httpdebuggerpro", "httpdebugger",

        // Hex editors
        "hxd64",
        "010editor",

        // Network RE
        "wireshark", "tshark",
    };

    // Window title fragments (catches renamed executables)
    private static readonly string[] BannedTitleFragments =
    [
        "dnspy",
        "ilspy",
        "dotpeek",
        "cheat engine",
        "x64dbg",
        "x32dbg",
        "ollydbg",
        "ida pro",
        "ida -",
        "ghidra",
        "de4dot",
        "process hacker",
        ".net reflector",
        "simple assembly explorer",
        "reflexil",
        "megadumper",
        "exeinfope",
        "detect it easy",
    ];

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Initialize()
    {
        try
        {
            // Initial scan
            var found = ScanNow();
            if (found != null)
            {
                OnDetected(found);
                return false;
            }

            // Background scan every 20–40s
            _timer = new System.Threading.Timer(OnTimer, null, NextInterval(), Timeout.Infinite);
            return true;
        }
        catch { return !SecurityPolicy.StrictFailClosed; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public string? ScanNow()
    {
        try
        {
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return null; }

            foreach (var p in procs)
            {
                try
                {
                    // Exact process name match only — no substring
                    var name = p.ProcessName ?? "";
                    if (BannedProcesses.Contains(name))
                    {
                        var result = $"{p.ProcessName} (pid {p.Id})";
                        p.Dispose();
                        DisposeAll(procs);
                        return result;
                    }

                    // Window title fragment check (catches renamed exes)
                    var title = p.MainWindowTitle?.ToLowerInvariant() ?? "";
                    if (title.Length > 3)
                    {
                        foreach (var frag in BannedTitleFragments)
                        {
                            if (title.Contains(frag, StringComparison.Ordinal))
                            {
                                var result = $"{p.ProcessName} (pid {p.Id}, title match)";
                                p.Dispose();
                                DisposeAll(procs);
                                return result;
                            }
                        }
                    }

                    p.Dispose();
                }
                catch
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return null;
        }
        catch { return SecurityPolicy.StrictFailClosed ? "Anti-RE scan failed unexpectedly." : null; }
    }

    private void OnTimer(object? state)
    {
        try
        {
            var found = ScanNow();
            if (found != null)
            {
                OnDetected(found);
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
        lock (_jitter) { return _jitter.Next(20_000, 40_001); }
    }

    private static void DisposeAll(Process[] procs)
    {
        foreach (var p in procs)
            try { p.Dispose(); } catch { }
    }

    private void OnDetected(string detail)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        ToolDetected?.Invoke(detail);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
