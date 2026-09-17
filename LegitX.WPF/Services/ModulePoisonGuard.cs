using System.Reflection;
using System.Runtime.CompilerServices;

namespace LegitX.WPF.Services;

/// <summary>
/// Detects common in-process patch / hook frameworks loaded into the CLR (Harmony, MonoMod, etc.).
/// These are frequently used to bypass license or integrity checks without editing files on disk.
/// </summary>
internal static class ModulePoisonGuard
{
    private static readonly string[] SuspiciousSubstrings =
    [
        "0Harmony", "Lib.Harmony", "HarmonyLib", "MonoMod", "Mono.Cecil", "dnlib",
    ];

    /// <summary>Returns null if clean, otherwise a short description of the first hit.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static string? ScanLoadedAssemblies()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try
                {
                    name = asm.GetName().Name ?? "";
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name))
                    continue;

                // Our own assemblies — never flag
                if (name.StartsWith("LegitX", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var bad in SuspiciousSubstrings)
                {
                    if (name.Contains(bad, StringComparison.OrdinalIgnoreCase))
                        return $"Blocked assembly load: {name}";
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
