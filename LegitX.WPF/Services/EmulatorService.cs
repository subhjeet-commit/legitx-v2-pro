using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LegitX.WPF.Services;

/// <summary>
/// Detects running Android emulators, enumerates HID devices, and optimizes emulator settings.
/// </summary>
public static class EmulatorService
{
    // ── Win32 for window rect / fullscreen detection ──
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_STYLE = -16;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_CAPTION = 0x00C00000;
    // ── Known emulator process names (BlueStacks & MSI App Player only) ──
    //
    // IMPORTANT: BlueStacks 4 / MSI 4 spawns multiple processes for ONE instance:
    //   Bluestacks.exe (×2), HD-Agent.exe, HD-Player.exe
    // We must NOT count each process as a separate emulator instance.
    // Instead we group by "family" and count distinct families.
    //
    // Family grouping:
    //   "BS5" = BlueStacks 5 / MSI 5 (main process: HD-Player.exe)
    //   "BS4" = BlueStacks 4 / MSI 4 (main process: Bluestacks.exe, also spawns HD-Player.exe + HD-Agent.exe)
    //
    public static readonly (string ProcessName, string DisplayName, string Family)[] KnownEmulators =
    [
        ("HD-Player",     "BlueStacks 5 / MSI App Player 5", "BS5"),
        ("HD-Player2",    "BlueStacks 5 (Instance 2)",       "BS5_I2"),
        ("Bluestacks",    "BlueStacks 4 / MSI App Player 4", "BS4"),
        ("BlueStacksGP",  "BlueStacks 4 (Legacy)",           "BS4"),
    ];

    // Processes that are background services / helpers for BlueStacks,
    // NOT separate emulator instances. These should always be ignored
    // when counting running emulator instances.
    private static readonly HashSet<string> BlueStacksHelperProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "HD-Agent",        // BlueStacks notification/tray agent
        "BstkSVC",         // BlueStacks service
        "BlueStacksHelper",
    };

    /// <summary>
    /// Gets a list of currently running emulator instances.
    /// Groups by emulator family so that BlueStacks 4's multiple processes
    /// (Bluestacks.exe ×2, HD-Player.exe, HD-Agent.exe) count as ONE instance.
    /// </summary>
    public static List<RunningEmulator> GetRunningEmulators()
    {
        var result = new List<RunningEmulator>();
        var seenFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // First pass: detect which families are present
            var familyProcesses = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (procName, displayName, family) in KnownEmulators)
            {
                var processes = Process.GetProcessesByName(procName);
                if (processes.Length > 0)
                {
                    if (!familyProcesses.ContainsKey(family))
                        familyProcesses[family] = new List<Process>();

                    familyProcesses[family].AddRange(processes);
                }
            }

            // Special case: If BS4 family is detected AND HD-Player is also found,
            // the HD-Player belongs to BS4 (it's part of the same emulator), NOT a separate BS5.
            // Only count HD-Player as BS5 if no BS4 processes are present.
            bool bs4Running = familyProcesses.ContainsKey("BS4");
            if (bs4Running && familyProcesses.ContainsKey("BS5"))
            {
                // HD-Player.exe is part of BS4 — remove from BS5 family
                foreach (var p in familyProcesses["BS5"])
                    p.Dispose();
                familyProcesses.Remove("BS5");
            }

            // Second pass: pick one representative process per family
            foreach (var (family, processes) in familyProcesses)
            {
                if (seenFamilies.Contains(family)) continue;
                seenFamilies.Add(family);

                // Pick the first process with a window title, or just the first one
                var representative = processes.FirstOrDefault(p =>
                {
                    try { return !string.IsNullOrEmpty(p.MainWindowTitle); } catch { return false; }
                }) ?? processes[0];

                // Find the display name for this family
                var displayName = KnownEmulators
                    .Where(e => e.Family == family)
                    .Select(e => e.DisplayName)
                    .FirstOrDefault() ?? family;

                long memMb = 0;
                try { memMb = representative.WorkingSet64 / (1024 * 1024); } catch { }

                result.Add(new RunningEmulator
                {
                    ProcessId = representative.Id,
                    ProcessName = representative.ProcessName,
                    DisplayName = displayName,
                    WindowTitle = TryGetTitle(representative),
                    MemoryMB = memMb,
                    StartTime = TryGetStartTime(representative)
                });

                // Dispose all processes in this family
                foreach (var p in processes)
                    p.Dispose();
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Gets detailed info about the first running emulator (for the info panel).
    /// </summary>
    public static EmulatorInfo? GetRunningEmulatorInfo()
    {
        try
        {
            var running = GetRunningEmulators();
            if (running.Count == 0) return null;

            var emu = running[0];
            var info = new EmulatorInfo
            {
                Name = emu.DisplayName,
                ProcessName = emu.ProcessName + ".exe",
                PID = emu.ProcessId,
                MemoryMB = emu.MemoryMB,
                WindowTitle = emu.WindowTitle,
                StartTime = emu.StartTime
            };

            // Try to detect BlueStacks version and config path
            try
            {
                using var bsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_nxt");
                if (bsKey != null)
                {
                    info.InstallPath = bsKey.GetValue("InstallDir")?.ToString() ?? "";
                    info.DataPath = bsKey.GetValue("UserDefinedDir")?.ToString() ?? "";
                    info.Version = bsKey.GetValue("Version")?.ToString() ?? "";
                    info.Engine = "BlueStacks 5 / MSI App Player 5";
                }
                else
                {
                    using var bs4Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks");
                    if (bs4Key != null)
                    {
                        info.InstallPath = bs4Key.GetValue("InstallDir")?.ToString() ?? "";
                        info.DataPath = bs4Key.GetValue("UserDefinedDir")?.ToString() ?? "";
                        info.Version = bs4Key.GetValue("Version")?.ToString() ?? "";
                        info.Engine = "BlueStacks 4 / MSI App Player 4";
                    }
                }
            }
            catch { }

            // Detect allocated CPU/RAM from bluestacks.conf
            try
            {
                var confPath = FindBlueStacksConf(info.DataPath, info.InstallPath);
                if (!string.IsNullOrEmpty(confPath) && File.Exists(confPath))
                {
                    var lines = File.ReadAllLines(confPath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("bst.instance.Nougat64.cpus=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("bst.instance.Pie64.cpus=", StringComparison.OrdinalIgnoreCase))
                            info.AllocatedCPU = line.Split('=').Last().Trim().Replace("\"", "");

                        if (line.StartsWith("bst.instance.Nougat64.ram=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("bst.instance.Pie64.ram=", StringComparison.OrdinalIgnoreCase))
                            info.AllocatedRAM = line.Split('=').Last().Trim().Replace("\"", "");

                        if (line.StartsWith("bst.instance.Nougat64.dpi=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("bst.instance.Pie64.dpi=", StringComparison.OrdinalIgnoreCase))
                            info.DPI = line.Split('=').Last().Trim().Replace("\"", "");

                        if (line.StartsWith("bst.instance.Nougat64.fps=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("bst.instance.Pie64.fps=", StringComparison.OrdinalIgnoreCase))
                            info.FPS = line.Split('=').Last().Trim().Replace("\"", "");

                        if (line.StartsWith("bst.instance.Nougat64.width=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("bst.instance.Pie64.width=", StringComparison.OrdinalIgnoreCase))
                            info.Resolution = line.Split('=').Last().Trim().Replace("\"", "");
                    }
                }
            }
            catch { }

            return info;
        }
        catch { return null; }
    }

    /// <summary>
    /// Applies aggressive optimizations to the running BlueStacks/MSI App Player emulator
    /// for maximum FPS and buttery smooth aim. Modifies registry + config files.
    /// </summary>
    public static (bool success, string message) OptimizeConnectedEmulator()
    {
        var results = new List<string>();
        int applied = 0;

        try
        {
            // ═══════════ 1. REGISTRY OPTIMIZATIONS ═══════════

            // BlueStacks 5 performance registry tweaks
            try
            {
                using var bsKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\BlueStacks_nxt");
                if (bsKey != null)
                {
                    // Force high performance GPU mode
                    bsKey.SetValue("GlMode", 2, RegistryValueKind.DWord);           // OpenGL mode
                    bsKey.SetValue("GlRenderer", "2", RegistryValueKind.String);     // Prefer dedicated GPU
                    bsKey.SetValue("GpuDecoding", "1", RegistryValueKind.String);    // GPU video decoding
                    bsKey.SetValue("AstcDecoding", "1", RegistryValueKind.String);   // ASTC texture decoding
                    results.Add("• BlueStacks 5 GPU mode: High Performance");
                    applied++;
                }
            }
            catch { }

            // BlueStacks 4 registry tweaks
            try
            {
                using var bs4Key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\BlueStacks");
                if (bs4Key != null)
                {
                    bs4Key.SetValue("GlMode", 2, RegistryValueKind.DWord);
                    bs4Key.SetValue("GlRenderer", "2", RegistryValueKind.String);
                    results.Add("• BlueStacks 4 GPU mode: High Performance");
                    applied++;
                }
            }
            catch { }

            // Disable Windows Fullscreen Optimizations for HD-Player.exe
            try
            {
                using var layersKey = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                layersKey?.SetValue("HD-Player.exe", "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE", RegistryValueKind.String);
                results.Add("• Fullscreen optimization disabled for HD-Player.exe");
                applied++;
            }
            catch { }

            // GPU scheduling priority for HD-Player
            try
            {
                using var gpuKey = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\HD-Player.exe\PerfOptions");
                gpuKey?.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord);     // High priority
                gpuKey?.SetValue("IoPriority", 3, RegistryValueKind.DWord);            // High IO priority
                gpuKey?.SetValue("GPUPriority", 8, RegistryValueKind.DWord);           // Max GPU priority
                results.Add("• HD-Player.exe CPU/GPU priority: Maximum");
                applied++;
            }
            catch { }

            // Reduce input lag — disable mouse smoothing system-wide (helps emulator input)
            try
            {
                using var mouseKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                mouseKey?.SetValue("SmoothMouseXCurve", new byte[] {
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x80, 0x99, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x40, 0x66, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x33, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00
                }, RegistryValueKind.Binary);
                mouseKey?.SetValue("SmoothMouseYCurve", new byte[] {
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00
                }, RegistryValueKind.Binary);
                mouseKey?.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                mouseKey?.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                mouseKey?.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                results.Add("• Mouse acceleration: Disabled (flat 1:1 curve)");
                applied++;
            }
            catch { }

            // Reduce USB polling / HID latency for emulator
            try
            {
                using var hidKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\mouclass\Parameters");
                hidKey?.SetValue("MouseDataQueueSize", 0x20, RegistryValueKind.DWord);
                using var kbKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters");
                kbKey?.SetValue("KeyboardDataQueueSize", 0x20, RegistryValueKind.DWord);
                results.Add("• Input queue size: Optimized (reduced latency)");
                applied++;
            }
            catch { }

            // ═══════════ 2. CONFIG FILE OPTIMIZATIONS ═══════════

            string? confPath = null;
            try
            {
                using var bsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_nxt");
                var installDir = bsKey?.GetValue("InstallDir")?.ToString();
                var dataDir = bsKey?.GetValue("UserDefinedDir")?.ToString();
                confPath = FindBlueStacksConf(dataDir, installDir);
            }
            catch { }

            if (confPath == null)
            {
                try
                {
                    using var bs4Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks");
                    var installDir = bs4Key?.GetValue("InstallDir")?.ToString();
                    var dataDir = bs4Key?.GetValue("UserDefinedDir")?.ToString();
                    confPath = FindBlueStacksConf(dataDir, installDir);
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(confPath) && File.Exists(confPath))
            {
                try
                {
                    var lines = File.ReadAllLines(confPath).ToList();
                    var modified = false;

                    // Helper to set or add a config value
                    void SetConf(string prefix, string value)
                    {
                        bool found = false;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                lines[i] = prefix + "\"" + value + "\"";
                                found = true;
                                modified = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            lines.Add(prefix + "\"" + value + "\"");
                            modified = true;
                        }
                    }

                    // Try common instance prefixes
                    foreach (var inst in new[] { "Nougat64", "Pie64", "Rvc64" })
                    {
                        var p = $"bst.instance.{inst}.";
                        SetConf($"{p}fps=", "120");           // Max FPS
                        SetConf($"{p}enable_fps_display=", "1");
                        SetConf($"{p}astc_decoding=", "1");    // GPU ASTC
                        SetConf($"{p}vsync=", "0");            // Disable VSync for lowest input lag
                    }

                    // Global performance settings
                    SetConf("bst.feature.rooting=", "0");
                    SetConf("bst.feature.game_astc_decoding=", "1");

                    if (modified)
                    {
                        File.WriteAllLines(confPath, lines);
                        results.Add("• Config: FPS → 120, VSync → OFF, ASTC → GPU");
                        applied++;
                    }
                }
                catch { results.Add("• Config: Could not modify (emulator may be running)"); }
            }

            // ═══════════ 3. PROCESS PRIORITY BOOST ═══════════
            try
            {
                var hdPlayers = Process.GetProcessesByName("HD-Player");
                foreach (var p in hdPlayers)
                {
                    try { p.PriorityClass = ProcessPriorityClass.High; } catch { }
                    p.Dispose();
                }
                if (hdPlayers.Length > 0)
                {
                    results.Add("• HD-Player.exe process priority: High");
                    applied++;
                }
            }
            catch { }

            if (applied > 0)
            {
                var msg = $"✅ {applied} optimization(s) applied!\n\n" + string.Join("\n", results) +
                          "\n\n💡 Restart the emulator for config changes to take full effect.";
                return (true, msg);
            }
            return (false, "No optimizations could be applied. Make sure BlueStacks/MSI App Player is installed.");
        }
        catch (Exception ex)
        {
            return (false, $"Optimization failed: {ex.Message}\nTry running as Administrator.");
        }
    }

    /// <summary>Counts distinct running emulator applications.</summary>
    public static int CountDistinctRunningEmulators()
    {
        var running = GetRunningEmulators();
        return running.Select(r => r.DisplayName).Distinct().Count();
    }

    /// <summary>Counts total running emulator instances.</summary>
    public static int CountRunningEmulatorInstances() => GetRunningEmulators().Count;

    /// <summary>Enumerates mouse devices via WMI.</summary>
    public static List<DeviceInfo> GetMouseDevices()
    {
        var result = new List<DeviceInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PointingDevice");
            foreach (var obj in searcher.Get())
            {
                result.Add(new DeviceInfo
                {
                    Name = obj["Name"]?.ToString() ?? "Unknown Mouse",
                    DeviceId = obj["DeviceID"]?.ToString() ?? "",
                    Status = obj["Status"]?.ToString() ?? "Unknown"
                });
                obj.Dispose();
            }
        }
        catch { result.Add(new DeviceInfo { Name = "Default Mouse", DeviceId = "", Status = "OK" }); }
        return result;
    }

    /// <summary>Enumerates keyboard devices via WMI with real product names resolved from PnP entities.</summary>
    public static List<DeviceInfo> GetKeyboardDevices()
    {
        var result = new List<DeviceInfo>();
        try
        {
            // Build a VID/PID → friendly name lookup from Win32_PnPEntity
            var pnpNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var pnpSearcher = new ManagementObjectSearcher(
                "SELECT PNPDeviceID, Name, Manufacturer FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\\\VID_%'"))
            {
                foreach (var pnp in pnpSearcher.Get())
                {
                    var pnpId = pnp["PNPDeviceID"]?.ToString() ?? "";
                    var name = pnp["Name"]?.ToString() ?? "";
                    var mfr = pnp["Manufacturer"]?.ToString() ?? "";

                    // Extract VID&PID key (ignore MI and instance)
                    var match = System.Text.RegularExpressions.Regex.Match(
                        pnpId, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
                    if (!match.Success) continue;

                    var vidPid = $"{match.Groups[1].Value}_{match.Groups[2].Value}".ToUpperInvariant();

                    // Prefer entries with real product names (not "USB Input Device" / "USB Composite Device")
                    if (name is "USB Input Device" or "USB Composite Device" or "USB Root Hub"
                        or "Generic USB Hub" or "HID Keyboard Device") continue;

                    if (!pnpNames.ContainsKey(vidPid))
                        pnpNames[vidPid] = !string.IsNullOrEmpty(mfr) && mfr != "(Standard system devices)"
                            ? $"{name} ({mfr})"
                            : name;
                    pnp.Dispose();
                }
            }

            // Now enumerate actual keyboards and resolve their names
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Keyboard");
            foreach (var obj in searcher.Get())
            {
                var deviceId = obj["DeviceID"]?.ToString() ?? "";
                var description = obj["Description"]?.ToString() ?? "";
                var status = obj["Status"]?.ToString() ?? "Unknown";

                // Extract VID/PID from the keyboard's DeviceID
                var match = System.Text.RegularExpressions.Regex.Match(
                    deviceId, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");

                string friendlyName;
                string dedupKey;

                if (match.Success)
                {
                    var vidPid = $"{match.Groups[1].Value}_{match.Groups[2].Value}".ToUpperInvariant();
                    dedupKey = vidPid;

                    // Try resolved PnP name first
                    if (pnpNames.TryGetValue(vidPid, out var resolved))
                        friendlyName = resolved;
                    else if (!string.IsNullOrEmpty(description) && description != "HID Keyboard Device")
                        friendlyName = $"{description} [{vidPid}]";
                    else
                        friendlyName = $"Keyboard [{vidPid}]";
                }
                else
                {
                    dedupKey = deviceId;
                    friendlyName = !string.IsNullOrEmpty(description)
                        ? description
                        : obj["Name"]?.ToString() ?? "Unknown Keyboard";
                }

                // Deduplicate — many HID collection entries share the same physical device
                if (seen.Contains(dedupKey)) { obj.Dispose(); continue; }
                seen.Add(dedupKey);

                result.Add(new DeviceInfo
                {
                    Name = friendlyName,
                    DeviceId = deviceId,
                    Status = status
                });
                obj.Dispose();
            }
        }
        catch { result.Add(new DeviceInfo { Name = "Default Keyboard", DeviceId = "", Status = "OK" }); }

        if (result.Count == 0)
            result.Add(new DeviceInfo { Name = "Default Keyboard", DeviceId = "", Status = "OK" });

        return result;
    }

    /// <summary>Returns the supported emulator display names.</summary>
    public static string[] GetSupportedEmulatorNames() =>
    [
        "BlueStacks 5",
        "BlueStacks 4",
        "MSI App Player 5",
        "MSI App Player 4"
    ];

    // ── Helpers ──

    private static string? FindBlueStacksConf(string? dataDir, string? installDir)
    {
        // Check common paths for bluestacks.conf
        string[] candidates =
        [
            Path.Combine(dataDir ?? "", "bluestacks.conf"),
            Path.Combine(installDir ?? "", "bluestacks.conf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueStacks_nxt", "bluestacks.conf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueStacks", "bluestacks.conf"),
            @"C:\ProgramData\BlueStacks_nxt\bluestacks.conf",
            @"C:\ProgramData\BlueStacks\bluestacks.conf",
        ];

        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(c))
                return c;
        }
        return null;
    }

    private static string TryGetTitle(Process p)
    {
        try { return p.MainWindowTitle; }
        catch { return ""; }
    }

    private static DateTime? TryGetStartTime(Process p)
    {
        try { return p.StartTime; }
        catch { return null; }
    }

    // ── Models ──

    public class RunningEmulator
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string WindowTitle { get; init; } = "";
        public long MemoryMB { get; init; }
        public DateTime? StartTime { get; init; }
    }

    public class EmulatorInfo
    {
        public string Name { get; set; } = "";
        public string ProcessName { get; set; } = "HD-Player.exe";
        public int PID { get; set; }
        public long MemoryMB { get; set; }
        public string WindowTitle { get; set; } = "";
        public DateTime? StartTime { get; set; }
        public string InstallPath { get; set; } = "";
        public string DataPath { get; set; } = "";
        public string Version { get; set; } = "";
        public string Engine { get; set; } = "";
        public string AllocatedCPU { get; set; } = "—";
        public string AllocatedRAM { get; set; } = "—";
        public string DPI { get; set; } = "—";
        public string FPS { get; set; } = "—";
        public string Resolution { get; set; } = "—";
    }

    public class DeviceInfo
    {
        public string Name { get; init; } = "";
        public string DeviceId { get; init; } = "";
        public string Status { get; init; } = "";
    }

    // ═══════════════════════════════════════════════════════════════
    // EMULATOR WINDOW & RESOLUTION DETECTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Result of emulator resolution detection — includes multiple data sources
    /// for the most accurate result possible.
    /// </summary>
    public class EmulatorResolutionInfo
    {
        /// <summary>The detected width (best source wins).</summary>
        public int Width { get; set; }
        /// <summary>The detected height (best source wins).</summary>
        public int Height { get; set; }
        /// <summary>Where the resolution was read from.</summary>
        public string Source { get; set; } = "";
        /// <summary>True if the emulator window is fullscreen / maximized.</summary>
        public bool IsFullscreen { get; set; }
        /// <summary>True if the window is borderless fullscreen.</summary>
        public bool IsBorderless { get; set; }
        /// <summary>The actual live window client area (may differ from config).</summary>
        public int LiveClientWidth { get; set; }
        /// <summary>The actual live window client area height.</summary>
        public int LiveClientHeight { get; set; }
        /// <summary>Ratio of how much of the monitor the emulator covers (0.0-1.0).</summary>
        public double ScreenCoverage { get; set; }
        /// <summary>Monitor width the emulator is on.</summary>
        public int MonitorWidth { get; set; }
        /// <summary>Monitor height the emulator is on.</summary>
        public int MonitorHeight { get; set; }
    }

    /// <summary>
    /// Detects the emulator's resolution from the best available source:
    ///   1. Live window client area (most accurate — actual rendered size)
    ///   2. BlueStacks conf file (width/height settings)
    ///   3. BlueStacks registry
    ///   4. Fallback to 1280x720
    ///
    /// Also detects if the window is fullscreen/maximized and calculates screen coverage.
    /// </summary>
    public static EmulatorResolutionInfo DetectEmulatorResolution()
    {
        var result = new EmulatorResolutionInfo
        {
            Width = 1280,
            Height = 720,
            Source = "Default"
        };

        // ── SOURCE 1: Live window measurement (highest priority) ──
        try
        {
            var procs = Process.GetProcessesByName("HD-Player");
            if (procs.Length == 0)
                procs = Process.GetProcessesByName("Bluestacks");

            foreach (var p in procs)
            {
                try
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
                        continue;

                    // Get client rect (the actual rendering area, no borders/titlebar)
                    if (GetClientRect(hwnd, out var clientRect))
                    {
                        int cw = clientRect.Right - clientRect.Left;
                        int ch = clientRect.Bottom - clientRect.Top;
                        if (cw > 100 && ch > 100)
                        {
                            result.LiveClientWidth = cw;
                            result.LiveClientHeight = ch;
                            result.Width = cw;
                            result.Height = ch;
                            result.Source = "Live Window";
                        }
                    }

                    // Get window rect (includes borders)
                    if (GetWindowRect(hwnd, out var windowRect))
                    {
                        int ww = windowRect.Right - windowRect.Left;
                        int wh = windowRect.Bottom - windowRect.Top;

                        // Get the monitor this window is on
                        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                        var monInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                        if (GetMonitorInfo(hMon, ref monInfo))
                        {
                            int monW = monInfo.rcMonitor.Right - monInfo.rcMonitor.Left;
                            int monH = monInfo.rcMonitor.Bottom - monInfo.rcMonitor.Top;
                            result.MonitorWidth = monW;
                            result.MonitorHeight = monH;

                            // Calculate screen coverage
                            double coverage = (double)(ww * wh) / (monW * monH);
                            result.ScreenCoverage = Math.Clamp(coverage, 0, 1);

                            // Check fullscreen: window covers >= 95% of monitor
                            result.IsFullscreen = coverage >= 0.95;

                            // Check borderless: no thick frame, no caption, popup style
                            int style = GetWindowLong(hwnd, GWL_STYLE);
                            result.IsBorderless = (style & WS_THICKFRAME) == 0 && (style & WS_CAPTION) == 0;

                            // Also check if maximized
                            if (IsZoomed(hwnd))
                                result.IsFullscreen = true;
                        }
                    }

                    break; // Use first valid window
                }
                finally { p.Dispose(); }
            }
        }
        catch { /* Fall through to config-based detection */ }

        // ── SOURCE 2: BlueStacks config file ──
        if (result.Source == "Default")
        {
            try
            {
                string? confPath = null;
                using (var bsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_nxt"))
                {
                    var installDir = bsKey?.GetValue("InstallDir")?.ToString();
                    var dataDir = bsKey?.GetValue("UserDefinedDir")?.ToString();
                    confPath = FindBlueStacksConf(dataDir, installDir);
                }

                if (confPath == null)
                {
                    using var bs4Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks");
                    var installDir = bs4Key?.GetValue("InstallDir")?.ToString();
                    var dataDir = bs4Key?.GetValue("UserDefinedDir")?.ToString();
                    confPath = FindBlueStacksConf(dataDir, installDir);
                }

                if (!string.IsNullOrEmpty(confPath) && File.Exists(confPath))
                {
                    int cfgW = 0, cfgH = 0;
                    var lines = File.ReadAllLines(confPath);
                    foreach (var line in lines)
                    {
                        // Try all known instance prefixes
                        if ((line.Contains(".width=", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains(".display_width=", StringComparison.OrdinalIgnoreCase)) &&
                            !line.Contains("dpi"))
                        {
                            var val = line.Split('=').Last().Trim().Replace("\"", "");
                            if (int.TryParse(val, out int w) && w > 100) cfgW = w;
                        }
                        if ((line.Contains(".height=", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains(".display_height=", StringComparison.OrdinalIgnoreCase)) &&
                            !line.Contains("dpi"))
                        {
                            var val = line.Split('=').Last().Trim().Replace("\"", "");
                            if (int.TryParse(val, out int h) && h > 100) cfgH = h;
                        }
                    }

                    if (cfgW > 100 && cfgH > 100)
                    {
                        result.Width = cfgW;
                        result.Height = cfgH;
                        result.Source = "BlueStacks Config";
                    }
                }
            }
            catch { }
        }

        // ── SOURCE 3: Registry ──
        if (result.Source == "Default")
        {
            try
            {
                using var bsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_nxt\Guests\Android\FrameBuffer\0");
                if (bsKey != null)
                {
                    var w = bsKey.GetValue("GuestWidth");
                    var h = bsKey.GetValue("GuestHeight");
                    if (w != null && h != null)
                    {
                        if (int.TryParse(w.ToString(), out int rw) && int.TryParse(h.ToString(), out int rh))
                        {
                            if (rw > 100 && rh > 100)
                            {
                                result.Width = rw;
                                result.Height = rh;
                                result.Source = "Registry (FrameBuffer)";
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return result;
    }

    /// <summary>
    /// Quick check: is the emulator window fullscreen/maximized?
    /// Returns (isFullscreen, screenCoverage, liveWidth, liveHeight).
    /// </summary>
    public static (bool isFullscreen, double coverage, int clientW, int clientH) CheckEmulatorFullscreen()
    {
        try
        {
            var procs = Process.GetProcessesByName("HD-Player");
            if (procs.Length == 0)
                procs = Process.GetProcessesByName("Bluestacks");

            foreach (var p in procs)
            {
                try
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
                        continue;

                    int cw = 0, ch = 0;
                    if (GetClientRect(hwnd, out var cr))
                    {
                        cw = cr.Right - cr.Left;
                        ch = cr.Bottom - cr.Top;
                    }

                    if (GetWindowRect(hwnd, out var wr))
                    {
                        int ww = wr.Right - wr.Left;
                        int wh = wr.Bottom - wr.Top;

                        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                        var monInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                        if (GetMonitorInfo(hMon, ref monInfo))
                        {
                            int monW = monInfo.rcMonitor.Right - monInfo.rcMonitor.Left;
                            int monH = monInfo.rcMonitor.Bottom - monInfo.rcMonitor.Top;
                            double coverage = (double)(ww * wh) / (monW * monH);

                            bool fullscreen = coverage >= 0.95 || IsZoomed(hwnd);
                            return (fullscreen, Math.Clamp(coverage, 0, 1), cw, ch);
                        }
                    }
                }
                finally { p.Dispose(); }
            }
        }
        catch { }
        return (false, 0, 0, 0);
    }
}
