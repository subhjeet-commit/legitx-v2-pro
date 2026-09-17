using System.IO;
using System.Runtime.InteropServices;

namespace LegitX.WPF.Services;

/// <summary>
/// Shows a console window with a maintenance message and waits for the user to press Enter.
/// Used when the admin enables maintenance mode from the admin panel.
/// </summary>
public static class MaintenanceConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleTitle(string lpConsoleTitle);

    /// <summary>
    /// Allocates a console window, prints the maintenance banner, waits for Enter, then exits the process.
    /// </summary>
    public static void ShowAndExit(string message)
    {
        try
        {
            // Hide all WPF windows first
            foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
            {
                try { w.Hide(); } catch { }
            }

            AllocConsole();
            SetConsoleTitle("LegitX V2 — Maintenance");

            // Bring the console to the front
            var hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd);

            // Re-open stdout/stdin for the new console
            var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdOut);
            var stdIn = new StreamReader(Console.OpenStandardInput());
            Console.SetIn(stdIn);

            // Set console colors
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Clear();

            // Print banner
            Console.WriteLine();
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║                                                              ║");
            Console.WriteLine("  ║              ██╗     ███████╗ ██████╗ ██╗████████╗            ║");
            Console.WriteLine("  ║              ██║     ██╔════╝██╔════╝ ██║╚══██╔══╝            ║");
            Console.WriteLine("  ║              ██║     █████╗  ██║  ███╗██║   ██║               ║");
            Console.WriteLine("  ║              ██║     ██╔══╝  ██║   ██║██║   ██║               ║");
            Console.WriteLine("  ║              ███████╗███████╗╚██████╔╝██║   ██║               ║");
            Console.WriteLine("  ║              ╚══════╝╚══════╝ ╚═════╝ ╚═╝   ╚═╝               ║");
            Console.WriteLine("  ║                         V2                                    ║");
            Console.WriteLine("  ║                                                              ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ┌──────────────────────────────────────────────────────────────┐");
            Console.WriteLine("  │                    ⚠  MAINTENANCE MODE  ⚠                   │");
            Console.WriteLine("  └──────────────────────────────────────────────────────────────┘");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.White;
            // Word-wrap the message at 60 chars
            var lines = WordWrap(message, 60);
            foreach (var line in lines)
                Console.WriteLine($"    {line}");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  ──────────────────────────────────────────────────────────────");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine();
            Console.WriteLine("    The software is temporarily unavailable while the");
            Console.WriteLine("    developers perform updates and improvements.");
            Console.WriteLine();
            Console.WriteLine("    Please try again later.");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  ──────────────────────────────────────────────────────────────");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("    Press Enter to exit...");
            Console.ResetColor();

            stdIn.ReadLine();
        }
        catch { }

        Environment.Exit(0);
    }

    private static string[] WordWrap(string text, int maxWidth)
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                if (current.Length + word.Length + 1 > maxWidth)
                {
                    result.Add(current);
                    current = word;
                }
                else
                {
                    current = current.Length == 0 ? word : current + " " + word;
                }
            }
            if (current.Length > 0)
                result.Add(current);
            else
                result.Add("");
        }
        return result.ToArray();
    }
}
