using System.Runtime.InteropServices;
using System.Windows;

namespace LegitX.WPF.Services;

/// <summary>
/// Detects display resolution(s) and provides multi-monitor selection.
/// Uses pure Win32 P/Invoke — no System.Windows.Forms dependency.
/// </summary>
public static class DisplayService
{
    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private const uint MONITORINFOF_PRIMARY = 1;

    #endregion

    public record MonitorInfo(int Index, string DeviceName, int Width, int Height, bool IsPrimary);

    /// <summary>
    /// Enumerates all connected monitors and returns their resolutions.
    /// </summary>
    public static List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<MONITORINFOEX>();

            if (GetMonitorInfo(hMonitor, ref info))
            {
                int w = info.rcMonitor.Right - info.rcMonitor.Left;
                int h = info.rcMonitor.Bottom - info.rcMonitor.Top;
                bool primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;

                monitors.Add(new MonitorInfo(
                    index++,
                    info.szDevice.TrimEnd('\0'),
                    w, h, primary));
            }
            return true; // continue enumeration
        }, IntPtr.Zero);

        return monitors;
    }

    /// <summary>
    /// Gets the primary monitor's resolution. Falls back to 1920x1080.
    /// </summary>
    public static (int Width, int Height) GetPrimaryResolution()
    {
        var monitors = GetAllMonitors();
        var primary = monitors.Find(m => m.IsPrimary);
        return primary != null ? (primary.Width, primary.Height) : (1920, 1080);
    }

    /// <summary>
    /// Auto-detects display resolution:
    ///  - Single monitor: returns it immediately.
    ///  - Multiple monitors: shows a picker dialog and returns the chosen one.
    /// Returns (width, height) of the selected monitor.
    /// </summary>
    public static (int Width, int Height) AutoDetectResolution()
    {
        var monitors = GetAllMonitors();

        if (monitors.Count == 0)
            return (1920, 1080);

        if (monitors.Count == 1)
            return (monitors[0].Width, monitors[0].Height);

        // Multiple monitors — ask the user
        return ShowMonitorPicker(monitors);
    }

    private static (int Width, int Height) ShowMonitorPicker(List<MonitorInfo> monitors)
    {
        // Build the dialog in code so we don't need another XAML file
        var win = new Window
        {
            Title = "Select Display",
            Width = 400,
            Height = 280 + monitors.Count * 48,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize
        };

        // Outer border matching the app theme
        var outerBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF0A0A10")),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF1E1E2E")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24)
        };

        var stack = new System.Windows.Controls.StackPanel();

        // Title
        var title = new System.Windows.Controls.TextBlock
        {
            Text = "🖥  Multiple Displays Detected",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        stack.Children.Add(title);

        var subtitle = new System.Windows.Controls.TextBlock
        {
            Text = "Select which display to use for resolution:",
            FontSize = 12,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF71717a")),
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(subtitle);

        int selectedWidth = monitors[0].Width;
        int selectedHeight = monitors[0].Height;

        foreach (var mon in monitors)
        {
            var btn = new System.Windows.Controls.Button
            {
                Height = 42,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = mon
            };

            // Style the button
            btn.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF161622"));
            btn.Foreground = System.Windows.Media.Brushes.White;
            btn.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF2A2A3A"));
            btn.BorderThickness = new Thickness(1);
            btn.FontSize = 13;

            var label = mon.IsPrimary
                ? $"⭐  Display {mon.Index + 1} — {mon.Width} × {mon.Height}  (Primary)"
                : $"      Display {mon.Index + 1} — {mon.Width} × {mon.Height}";

            btn.Content = label;

            btn.Click += (_, _) =>
            {
                var m = (MonitorInfo)btn.Tag;
                selectedWidth = m.Width;
                selectedHeight = m.Height;
                win.DialogResult = true;
                win.Close();
            };

            stack.Children.Add(btn);
        }

        // Cancel button
        var cancel = new System.Windows.Controls.Button
        {
            Content = "Use Primary Display",
            Height = 36,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF71717a")),
            BorderThickness = new Thickness(0)
        };
        cancel.Click += (_, _) =>
        {
            var primary = monitors.Find(m => m.IsPrimary) ?? monitors[0];
            selectedWidth = primary.Width;
            selectedHeight = primary.Height;
            win.DialogResult = true;
            win.Close();
        };
        stack.Children.Add(cancel);

        outerBorder.Child = stack;
        win.Content = outerBorder;

        // Allow dragging
        outerBorder.MouseLeftButtonDown += (_, _) => win.DragMove();

        win.ShowDialog();

        return (selectedWidth, selectedHeight);
    }
}
