using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace LegitX.WPF.Services;

/// <summary>
/// Manages the Interception kernel driver — extraction, installation, uninstallation, status check.
/// All binaries are embedded as resources inside the exe.
/// </summary>
public static class InterceptionDriverService
{
    // ── Minimum valid DLL size (interception.dll is ~10 KB; anything < 4 KB is corrupt/empty) ──
    private const long MIN_DLL_SIZE = 4096;

    #region Detection

    /// <summary>
    /// Checks if the Interception kernel driver is installed.
    /// Uses registry (services, class filters, dedicated service key), optional WMI, and
    /// oblitum-style <c>mouse.sys</c>/<c>keyboard.sys</c> (or <c>interception.sys</c>) in System32\drivers.
    /// </summary>
    public static bool IsDriverInstalled()
    {
        try
        {
            if (RegistryIndicatesInterception())
                return true;
            if (InterceptionServiceKeyExists())
                return true;
            if (WmiListsInterceptionDriver())
                return true;
            if (DriversFolderIndicatesInterception())
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Mouse, keyboard, and HID setup class GUIDs (Device Manager).</summary>
    private const string MouseClassGuid = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96f-e325-11ce-bfc1-08002be10318}";
    private const string KeyboardClassGuid = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}";
    private const string HidClassGuid = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96d-e325-11ce-bfc1-08002be10318}";

    private static bool RegistryIndicatesInterception()
    {
        const string filter = "interception";

        if (ServiceKeyHasFilter(@"SYSTEM\CurrentControlSet\Services\mouclass", filter))
            return true;
        if (ServiceKeyHasFilter(@"SYSTEM\CurrentControlSet\Services\kbdclass", filter))
            return true;

        if (ClassInstancesHaveFilter(MouseClassGuid, filter))
            return true;
        if (ClassInstancesHaveFilter(KeyboardClassGuid, filter))
            return true;
        if (ClassInstancesHaveFilter(HidClassGuid, filter))
            return true;

        return false;
    }

    /// <summary>Interception registers a <c>Services\interception</c> (or similar) driver service on many builds.</summary>
    private static bool InterceptionServiceKeyExists()
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
            if (services == null) return false;

            foreach (var name in services.GetSubKeyNames())
            {
                if (string.Equals(name, "interception", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { /* ignore */ }

        return false;
    }

    private static bool WmiListsInterceptionDriver()
    {
        try
        {
            const string q = "SELECT Name FROM Win32_SystemDriver WHERE Name LIKE '%interception%'";
            using var searcher = new ManagementObjectSearcher(q);
            using var results = searcher.Get();
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Oblitum installer drops these modules; some variants use <c>interception.sys</c> alone.</summary>
    private static bool DriversFolderIndicatesInterception()
    {
        try
        {
            string driversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
            string interceptionSys = Path.Combine(driversDir, "interception.sys");
            if (File.Exists(interceptionSys) && new FileInfo(interceptionSys).Length >= 1024)
                return true;

            string mouse = Path.Combine(driversDir, "mouse.sys");
            string keyboard = Path.Combine(driversDir, "keyboard.sys");
            if (File.Exists(mouse) && File.Exists(keyboard)
                && new FileInfo(mouse).Length >= 1024
                && new FileInfo(keyboard).Length >= 1024)
                return true;
        }
        catch { /* ignore */ }

        return false;
    }

    private static bool ServiceKeyHasFilter(string subKey, string driverName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey, false);
        if (key == null) return false;
        if (ValueListsFilter(key.GetValue("UpperFilters"), driverName)) return true;
        if (ValueListsFilter(key.GetValue("LowerFilters"), driverName)) return true;
        return false;
    }

    /// <summary>Scans all subkeys under a setup class (0000, 0001, …) for Upper/LowerFilters.</summary>
    private static bool ClassInstancesHaveFilter(string classKeyPath, string driverName)
    {
        using var classKey = Registry.LocalMachine.OpenSubKey(classKeyPath, false);
        if (classKey == null) return false;

        foreach (var subName in classKey.GetSubKeyNames())
        {
            if (string.Equals(subName, "Properties", StringComparison.OrdinalIgnoreCase))
                continue;

            using var instance = classKey.OpenSubKey(subName, false);
            if (instance == null) continue;

            if (ValueListsFilter(instance.GetValue("UpperFilters"), driverName)) return true;
            if (ValueListsFilter(instance.GetValue("LowerFilters"), driverName)) return true;
        }

        return false;
    }

    /// <summary>
    /// True if a filter value (REG_MULTI_SZ, REG_SZ, or space-separated text) references Interception.
    /// </summary>
    private static bool ValueListsFilter(object? val, string driverName)
    {
        if (val == null) return false;

        if (val is string[] parts)
        {
            foreach (var p in parts)
            {
                if (TokenReferencesDriver(p, driverName))
                    return true;
            }
            return false;
        }

        if (val is string s)
        {
            foreach (var token in SplitFilterTokens(s))
            {
                if (TokenReferencesDriver(token, driverName))
                    return true;
            }
            return false;
        }

        return false;
    }

    private static IEnumerable<string> SplitFilterTokens(string s)
    {
        if (string.IsNullOrEmpty(s)) yield break;
        foreach (var token in s.Split(new[] { '\0', ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            if (t.Length > 0) yield return t;
        }
    }

    private static bool TokenReferencesDriver(string? token, string driverName)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim();
        // Match service name "interception" or embedded in composite tokens (case-insensitive).
        return token.Contains(driverName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the current process is running as administrator.
    /// </summary>
    public static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    #endregion

    #region DLL Extraction

    /// <summary>
    /// Extracts the correct interception.dll (x64 or x86) next to the running exe.
    /// Called on app startup. Re-extracts if the file is missing or corrupt (zero-length / too small).
    /// </summary>
    public static void EnsureDllExtracted()
    {
        try
        {
            string exeDir = AppContext.BaseDirectory;
            string dllPath = Path.Combine(exeDir, "interception.dll");

            // Skip only if the file already exists AND is a valid size
            if (File.Exists(dllPath))
            {
                var info = new FileInfo(dllPath);
                if (info.Length >= MIN_DLL_SIZE)
                    return; // looks healthy

                // Corrupt/empty — delete so we can re-extract
                Debug.WriteLine($"[InterceptionDriverService] interception.dll is only {info.Length} bytes — re-extracting");
                try { File.Delete(dllPath); } catch { }
            }

            string resourceName = Environment.Is64BitProcess ? "interception_x64.dll" : "interception_x86.dll";
            ExtractResource(resourceName, dllPath);
            Debug.WriteLine("[InterceptionDriverService] interception.dll extracted successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InterceptionDriverService] DLL extraction failed: {ex.Message}");
        }
    }

    #endregion

    #region Install / Uninstall

    /// <summary>
    /// Installs the Interception kernel driver. Requires admin privileges.
    /// Returns (success, message).
    /// </summary>
    public static (bool success, string message) InstallDriver()
    {
        if (!IsAdmin())
            return (false, "Administrator privileges required.\n\nPlease restart LegitX V2 as Administrator.");

        try
        {
            string installerPath = ExtractInstaller();

            var (exitCode, output) = RunInstaller(installerPath, "/install");
            CleanupInstaller(installerPath);

            if (exitCode == 0)
            {
                Debug.WriteLine("[InterceptionDriverService] Driver installed (exit 0)");
                return (true, "Interception kernel driver installed successfully!\n\nYou must restart your computer for the driver to become active.");
            }
            else
            {
                Debug.WriteLine($"[InterceptionDriverService] Install failed (exit {exitCode}): {output}");
                string detail = !string.IsNullOrWhiteSpace(output) ? output
                    : "Make sure no antivirus is blocking the installer and that you are running as Administrator.";
                return (false, $"Driver installation failed (exit code {exitCode}).\n\n{detail}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined the UAC prompt
            return (false, "Installation was cancelled.\n\nYou must approve the administrator prompt to install the kernel driver.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InterceptionDriverService] Install exception: {ex.Message}");
            return (false, $"Installation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Uninstalls the Interception kernel driver. Requires admin privileges.
    /// Returns (success, message).
    /// </summary>
    public static (bool success, string message) UninstallDriver()
    {
        if (!IsAdmin())
            return (false, "Administrator privileges required.\n\nPlease restart LegitX V2 as Administrator.");

        try
        {
            string installerPath = ExtractInstaller();

            var (exitCode, output) = RunInstaller(installerPath, "/uninstall");
            CleanupInstaller(installerPath);

            if (exitCode == 0)
            {
                Debug.WriteLine("[InterceptionDriverService] Driver uninstalled (exit 0)");
                return (true, "Interception kernel driver removed successfully!\n\nYou must restart your computer for the removal to take effect.\n\nLegitX V2 will automatically use User Mode after restart.");
            }
            else
            {
                Debug.WriteLine($"[InterceptionDriverService] Uninstall failed (exit {exitCode}): {output}");
                string detail = !string.IsNullOrWhiteSpace(output) ? output
                    : "Make sure no antivirus is blocking the operation and that you are running as Administrator.";
                return (false, $"Driver removal failed (exit code {exitCode}).\n\n{detail}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Uninstallation was cancelled.\n\nYou must approve the administrator prompt to remove the kernel driver.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InterceptionDriverService] Uninstall exception: {ex.Message}");
            return (false, $"Uninstallation error: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Runs the official <c>install-interception.exe</c> with <c>/install</c> or <c>/uninstall</c>.
    /// <see cref="InstallDriver"/> already requires an elevated process, so we start the child with
    /// <see cref="ProcessStartInfo.UseShellExecute"/> <c>false</c> (no <c>runas</c> verb). That avoids a
    /// second UAC hop and ensures <see cref="Process.ExitCode"/> reflects the real installer result.
    /// </summary>
    private static (int exitCode, string output) RunInstaller(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Do not redirect stdio — native installers can fill buffers and deadlock WaitForExit.
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start installer process.");

        if (!proc.WaitForExit(120_000))
        {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException("Installer process did not finish within 120 seconds.");
        }

        return (proc.ExitCode, string.Empty);
    }

    /// <summary>
    /// Extracts install-interception.exe next to our own running EXE.
    /// This ensures it runs from a directory the admin process already owns,
    /// avoiding permission issues with temp folders.
    /// </summary>
    private static string ExtractInstaller()
    {
        string exeDir = AppContext.BaseDirectory;
        string path = Path.Combine(exeDir, "install-interception.exe");

        // Always overwrite to ensure latest embedded version
        ExtractResource("install-interception.exe", path);
        return path;
    }

    /// <summary>
    /// Tries to delete the installer exe after use. Silently ignores errors.
    /// </summary>
    private static void CleanupInstaller(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* file may still be locked briefly — gets overwritten next time anyway */ }

        // Also clean up old temp folder leftovers from previous versions
        try
        {
            string oldTempDir = Path.Combine(Path.GetTempPath(), "LegitX_Interception");
            if (Directory.Exists(oldTempDir))
                Directory.Delete(oldTempDir, true);
        }
        catch { }
    }

    /// <summary>
    /// Extracts an embedded resource to a file path.
    /// </summary>
    private static void ExtractResource(string logicalName, string outputPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded resource '{logicalName}' not found.");

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(fs);
    }

    #endregion
}
