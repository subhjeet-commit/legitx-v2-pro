using System.Diagnostics;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace LegitX.WPF.Services;

/// <summary>
/// Real driver detection and status checking.
/// Detects HID/mouse/display drivers needed for the app to work properly.
/// </summary>
public static class DriverService
{
    public record DriverInfo(string Name, string Status, string Version, string DeviceClass, bool IsHealthy);

    /// <summary>
    /// Checks all relevant drivers for mouse input and display.
    /// Returns a summary report of driver health.
    /// </summary>
    public static List<DriverInfo> CheckAllDrivers()
    {
        var drivers = new List<DriverInfo>();

        drivers.AddRange(GetDriversByClass("Mouse"));
        drivers.AddRange(GetDriversByClass("HIDClass"));
        drivers.AddRange(GetDriversByClass("Display"));
        drivers.AddRange(GetDriversByClass("Keyboard"));

        return drivers;
    }

    /// <summary>
    /// Queries WMI for PnP devices in a specific device class.
    /// </summary>
    private static List<DriverInfo> GetDriversByClass(string deviceClass)
    {
        var results = new List<DriverInfo>();
        try
        {
            // Use PnP device query to find devices by class
            string query = $"SELECT * FROM Win32_PnPSignedDriver WHERE DeviceClass = '{deviceClass}'";
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["DeviceName"]?.ToString() ?? obj["FriendlyName"]?.ToString() ?? "Unknown";
                string status = obj["Status"]?.ToString() ?? "Unknown";
                string version = obj["DriverVersion"]?.ToString() ?? "N/A";
                bool healthy = status.Equals("OK", StringComparison.OrdinalIgnoreCase);

                results.Add(new DriverInfo(name, status, version, deviceClass, healthy));
            }
        }
        catch
        {
            // WMI might fail in some environments
        }
        return results;
    }

    /// <summary>
    /// Checks if mouclass (the core Windows mouse class driver) is running.
    /// </summary>
    public static bool IsMouclassRunning()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\mouclass", false);
            if (key != null)
            {
                var start = key.GetValue("Start");
                // Start type: 1=System, 2=Auto, 3=Manual, 4=Disabled
                return start is int s && s <= 3;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Checks if a HID-compliant mouse device is present.
    /// </summary>
    public static bool IsHidMousePresent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PointingDevice WHERE PNPDeviceID LIKE '%HID%'");
            return searcher.Get().Count > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Gets a formatted driver health report for display in the UI.
    /// </summary>
    public static string GetDriverHealthReport()
    {
        var sb = new StringBuilder();
        var drivers = CheckAllDrivers();

        var mouseDrivers = drivers.FindAll(d => d.DeviceClass == "Mouse");
        var hidDrivers = drivers.FindAll(d => d.DeviceClass == "HIDClass");
        var displayDrivers = drivers.FindAll(d => d.DeviceClass == "Display");

        sb.AppendLine("═══ Mouse Drivers ═══");
        if (mouseDrivers.Count == 0)
            sb.AppendLine("  ⚠ No mouse drivers detected");
        else
            foreach (var d in mouseDrivers)
                sb.AppendLine($"  {(d.IsHealthy ? "✅" : "❌")} {d.Name} — v{d.Version} [{d.Status}]");

        sb.AppendLine();
        sb.AppendLine("═══ HID Devices ═══");
        if (hidDrivers.Count == 0)
            sb.AppendLine("  ⚠ No HID devices detected");
        else
            foreach (var d in hidDrivers.Take(5)) // Limit to avoid huge lists
                sb.AppendLine($"  {(d.IsHealthy ? "✅" : "❌")} {d.Name} — v{d.Version}");
        if (hidDrivers.Count > 5)
            sb.AppendLine($"  ... and {hidDrivers.Count - 5} more");

        sb.AppendLine();
        sb.AppendLine("═══ Display Drivers ═══");
        if (displayDrivers.Count == 0)
            sb.AppendLine("  ⚠ No display drivers detected");
        else
            foreach (var d in displayDrivers)
                sb.AppendLine($"  {(d.IsHealthy ? "✅" : "❌")} {d.Name} — v{d.Version}");

        sb.AppendLine();
        sb.AppendLine("═══ Core Services ═══");
        sb.AppendLine($"  {(IsMouclassRunning() ? "✅" : "❌")} mouclass (Mouse Class Driver)");
        sb.AppendLine($"  {(IsHidMousePresent() ? "✅" : "❌")} HID Mouse Device");

        return sb.ToString();
    }

    /// <summary>
    /// Returns true if all essential drivers for the app are healthy.
    /// </summary>
    public static bool AreEssentialDriversHealthy()
    {
        return IsMouclassRunning() && IsHidMousePresent();
    }

    /// <summary>
    /// Attempts to restart a driver via devcon-style PnP device reset.
    /// This is safer than the old fake "driver" registry tweaks.
    /// </summary>
    public static bool RestartMouseDriver()
    {
        try
        {
            // Disable and re-enable HID mouse devices to refresh them
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -Command \"Get-PnpDevice -Class Mouse | Where-Object {$_.Status -eq 'OK'} | Disable-PnpDevice -Confirm:$false; Start-Sleep -Seconds 1; Get-PnpDevice -Class Mouse | Enable-PnpDevice -Confirm:$false\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(15000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }
}
