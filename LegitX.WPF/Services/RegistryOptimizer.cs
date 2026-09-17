using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LegitX.WPF.Services;

/// <summary>
/// Handles all registry-based optimizations, drivers, and cleanup operations.
/// Ported from the original RegistryOptimizer.cs and LegitX.cs event handlers.
/// </summary>
public static class RegistryOptimizer
{
    #region Cleanup / Bypass Operations

    public static void RunSuperCleaner()
    {
        RunSilentCommand("del /s /q %temp%\\*");
        RunSilentCommand("del /s /q C:\\Windows\\Temp\\*");
        RunSilentCommand("del /s /q C:\\Windows\\Prefetch\\*");
    }

    public static void ClearEventViewerLogs()
    {
        RunSilentCommand("for /F \"tokens=*\" %1 in ('wevtutil.exe el') DO wevtutil.exe cl \"%1\"");
    }

    public static void ClearTempFiles()
    {
        RunSilentCommand("del /s /q %temp%\\*");
        RunSilentCommand("rd /s /q %temp%");
        RunSilentCommand("md %temp%");
    }

    public static void ClearWindowsCache()
    {
        RunSilentCommand("ipconfig /flushdns");
        RunSilentCommand("del /s /q \"%localappdata%\\Microsoft\\Windows\\INetCache\\*\"");
    }

    public static void RunLogKiller()
    {
        RunSilentCommand("del /s /q C:\\Windows\\System32\\LogFiles\\*");
    }

    public static void RunStringsCleaner()
    {
        RunSilentCommand("cipher /w:C:\\");
    }

    public static void RunCleanPCAndEmulator()
    {
        RunSuperCleaner();
        ClearTempFiles();
        ClearWindowsCache();
    }

    public static void RunAdvStringBypass()
    {
        RunSuperCleaner();
        ClearEventViewerLogs();
        ClearTempFiles();
    }

    public static void RunCompleteBypass()
    {
        RunSuperCleaner();
        ClearEventViewerLogs();
        ClearTempFiles();
        ClearWindowsCache();
        RunLogKiller();
        RunStringsCleaner();
    }

    #endregion

    #region Self-Destruct

    public static void SelfDestruct()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (File.Exists(exePath))
            {
                byte[] randomData = new byte[new FileInfo(exePath).Length];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(randomData);
                File.WriteAllBytes(exePath, randomData);
                File.Delete(exePath);
            }
        }
        catch { }
        Environment.Exit(0);
    }

    #endregion

    #region Helpers

    public static void RunSilentCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(30000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command error: {ex.Message}");
        }
    }

    private static void SetRegistryValue(RegistryKey root, string subKey, string name, object value, RegistryValueKind kind)
    {
        try
        {
            using var key = root.CreateSubKey(subKey, true);
            key?.SetValue(name, value, kind);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registry error [{subKey}\\{name}]: {ex.Message}");
        }
    }

    #endregion
}
