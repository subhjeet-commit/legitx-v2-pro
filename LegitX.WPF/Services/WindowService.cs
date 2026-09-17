using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace LegitX.WPF.Services;

/// <summary>
/// Manages global hotkeys for toggling window visibility,
/// streamer mode, form bypass (hide from Alt+Tab), and pin-on-top.
/// Supports user-configurable keyboard shortcuts.
/// </summary>
public class WindowService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int HOTKEY_TOGGLE_VISIBILITY = 1;
    private const int HOTKEY_PAUSE_SENSITIVITY = 2;
    private const int WM_HOTKEY = 0x0312;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;

    public event Action? ToggleVisibilityRequested;
    public event Action? PauseSensitivityRequested;

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        RegisterVisibilityHotkey();
        RegisterPauseSensitivityHotkey();

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    /// <summary>
    /// Registers (or re-registers) the global hotkey for toggling visibility
    /// using the user's configured shortcut.
    /// </summary>
    public void RegisterVisibilityHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Unregister existing
        UnregisterHotKey(_hwnd, HOTKEY_TOGGLE_VISIBILITY);

        // Get user config
        var (key, mod) = ShortcutService.GetShortcut("ToggleVisibility");
        uint vk = ShortcutService.KeyToVirtualKey(key);
        uint flags = ShortcutService.ModifiersToFlags(mod);

        RegisterHotKey(_hwnd, HOTKEY_TOGGLE_VISIBILITY, flags, vk);
    }

    /// <summary>
    /// Registers (or re-registers) the global hotkey for pausing/resuming sensitivity.
    /// </summary>
    public void RegisterPauseSensitivityHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;

        UnregisterHotKey(_hwnd, HOTKEY_PAUSE_SENSITIVITY);

        var (key, mod) = ShortcutService.GetShortcut("PauseSensitivity");
        uint vk = ShortcutService.KeyToVirtualKey(key);
        uint flags = ShortcutService.ModifiersToFlags(mod);

        RegisterHotKey(_hwnd, HOTKEY_PAUSE_SENSITIVITY, flags, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_TOGGLE_VISIBILITY)
            {
                ToggleVisibilityRequested?.Invoke();
                handled = true;
            }
            else if (id == HOTKEY_PAUSE_SENSITIVITY)
            {
                PauseSensitivityRequested?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void SetStreamerMode(bool enabled)
    {
        SetWindowDisplayAffinity(_hwnd, enabled ? 17u : 0u);
    }

    public void SetHideFromAltTab(bool hidden)
    {
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (hidden)
        {
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
        }
        else
        {
            exStyle &= ~WS_EX_TOOLWINDOW;
            exStyle |= WS_EX_APPWINDOW;
        }
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    public void CreateFirewallRule(string exePath)
    {
        RegistryOptimizer.RunSilentCommand(
            $"netsh advfirewall firewall add rule name=\"LegitXBlockNet\" dir=out action=block program=\"{exePath}\" enable=yes");
    }

    public void RemoveFirewallRule(string exePath)
    {
        RegistryOptimizer.RunSilentCommand(
            $"netsh advfirewall firewall delete rule name=\"LegitXBlockNet\" program=\"{exePath}\"");
    }

    public void Dispose()
    {
        UnregisterHotKey(_hwnd, HOTKEY_TOGGLE_VISIBILITY);
        UnregisterHotKey(_hwnd, HOTKEY_PAUSE_SENSITIVITY);
        _hwndSource?.RemoveHook(WndProc);
    }
}
