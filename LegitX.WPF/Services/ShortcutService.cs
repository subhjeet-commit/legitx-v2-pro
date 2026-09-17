using System.Windows.Input;
using Microsoft.Win32;

namespace LegitX.WPF.Services;

/// <summary>
/// Manages keyboard shortcut bindings.
/// Persists user-customized hotkeys to the Windows Registry.
/// </summary>
public static class ShortcutService
{
    private const string REG_KEY = @"SOFTWARE\LegitX V2\Shortcuts";

    public record ShortcutBinding(string Id, string DisplayName, string Description, Key DefaultKey, ModifierKeys DefaultModifiers = ModifierKeys.None);

    /// <summary>
    /// All available shortcut definitions with their defaults.
    /// </summary>
    public static readonly ShortcutBinding[] AllShortcuts =
    [
        new("ToggleVisibility", "Toggle Visibility", "Show or hide the application window", Key.Insert),
        new("PauseSensitivity", "Pause / Resume Sensitivity", "Toggle the mouse sensitivity engine on/off", Key.F9),
    ];

    /// <summary>
    /// Returns the user-configured key for a shortcut, or its default.
    /// </summary>
    public static (Key key, ModifierKeys modifiers) GetShortcut(string id)
    {
        var def = Array.Find(AllShortcuts, s => s.Id == id);
        if (def == null) return (Key.None, ModifierKeys.None);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(REG_KEY, false);
            if (key != null)
            {
                var keyVal = key.GetValue($"{id}_Key");
                var modVal = key.GetValue($"{id}_Mod");
                if (keyVal is int k && modVal is int m)
                    return ((Key)k, (ModifierKeys)m);
            }
        }
        catch { }

        return (def.DefaultKey, def.DefaultModifiers);
    }

    /// <summary>
    /// Returns all shortcuts with their current configured values.
    /// </summary>
    public static Dictionary<string, (Key key, ModifierKeys modifiers)> GetAllShortcuts()
    {
        var result = new Dictionary<string, (Key, ModifierKeys)>();
        foreach (var s in AllShortcuts)
            result[s.Id] = GetShortcut(s.Id);
        return result;
    }

    /// <summary>
    /// Saves a shortcut binding to the registry.
    /// </summary>
    public static void SaveShortcut(string id, Key key, ModifierKeys modifiers)
    {
        try
        {
            using var regKey = Registry.CurrentUser.CreateSubKey(REG_KEY);
            regKey?.SetValue($"{id}_Key", (int)key, RegistryValueKind.DWord);
            regKey?.SetValue($"{id}_Mod", (int)modifiers, RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>
    /// Saves all shortcuts at once.
    /// </summary>
    public static void SaveAllShortcuts(Dictionary<string, (Key key, ModifierKeys modifiers)> shortcuts)
    {
        foreach (var (id, (key, mod)) in shortcuts)
            SaveShortcut(id, key, mod);
    }

    /// <summary>
    /// Resets all shortcuts to their defaults.
    /// </summary>
    public static void ResetToDefaults()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKey(REG_KEY, false);
        }
        catch { }
    }

    /// <summary>
    /// Converts a Key + ModifierKeys to a display string like "Ctrl+Shift+F5".
    /// </summary>
    public static string FormatShortcut(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None) return "None";

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(KeyToString(key));
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Converts a Key enum to a clean display name.
    /// </summary>
    public static string KeyToString(Key key)
    {
        return key switch
        {
            Key.Insert => "INS",
            Key.Delete => "DEL",
            Key.Home => "HOME",
            Key.End => "END",
            Key.PageUp => "PGUP",
            Key.PageDown => "PGDN",
            Key.Escape => "ESC",
            Key.Back => "BKSP",
            Key.Tab => "TAB",
            Key.Return => "ENTER",
            Key.Space => "SPACE",
            Key.OemTilde => "~",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// Gets the virtual key code for RegisterHotKey from a WPF Key.
    /// </summary>
    public static uint KeyToVirtualKey(Key key)
    {
        return (uint)KeyInterop.VirtualKeyFromKey(key);
    }

    /// <summary>
    /// Gets the modifier flags for RegisterHotKey from WPF ModifierKeys.
    /// </summary>
    public static uint ModifiersToFlags(ModifierKeys modifiers)
    {
        uint flags = 0x4000; // MOD_NOREPEAT
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= 0x0008;
        return flags;
    }
}
