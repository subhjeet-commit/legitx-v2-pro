using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace LegitX.WPF.Services;

/// <summary>
/// Persists all user settings to/from Windows Registry.
/// </summary>
public static class SettingsService
{
    private const string REG_KEY = @"SOFTWARE\LegitX V2";
    private const string SENS_KEY = @"SOFTWARE\LegitX V2\MouseSensitivity";

    #region Main Sensitivity Settings (Trackbar-based)

    public static void SaveMainSettings(double mouseSens, double xAxis, double yAxis,
        int displayW, int displayH, int emuW, int emuH, int curveType = 0)
    {
        using var key = Registry.CurrentUser.CreateSubKey(REG_KEY);
        if (key == null) return;
        key.SetValue("MouseSensitivity", mouseSens.ToString("0.00", CultureInfo.InvariantCulture));
        key.SetValue("XAxisSensitivity", xAxis.ToString("0.00", CultureInfo.InvariantCulture));
        key.SetValue("YAxisSensitivity", yAxis.ToString("0.00", CultureInfo.InvariantCulture));
        key.SetValue("DisplayWidth", displayW);
        key.SetValue("DisplayHeight", displayH);
        key.SetValue("EmulatorWidth", emuW);
        key.SetValue("EmulatorHeight", emuH);
        key.SetValue("CurveType", curveType);
    }

    /// <summary>Main speed fields are doubles; negative sentinel means "use app default".</summary>
    public static (double mouseSens, double xAxis, double yAxis, int displayW, int displayH, int emuW, int emuH, int curveType)
        LoadMainSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
        return (
            ReadMainSpeed(key, "MouseSensitivity", -1.0),
            ReadMainSpeed(key, "XAxisSensitivity", -1.0),
            ReadMainSpeed(key, "YAxisSensitivity", -1.0),
            GetInt(key, "DisplayWidth", 1920),
            GetInt(key, "DisplayHeight", 1080),
            GetInt(key, "EmulatorWidth", 1280),
            GetInt(key, "EmulatorHeight", 720),
            GetInt(key, "CurveType", 0)
        );
    }

    private static double ReadMainSpeed(RegistryKey? key, string name, double unset)
    {
        if (key == null) return unset;
        var val = key.GetValue(name);
        if (val == null) return unset;
        if (val is int i) return i;
        if (val is long l) return l;
        string? s = val.ToString()?.Replace(',', '.');
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d;
        return unset;
    }

    #endregion

    #region Advanced Sensitivity Settings (Text-based)

    public static void SaveAdvancedSettings(double generalSens, double sensLimit,
        double preScaleX, double preScaleY,
        double postScaleX, double postScaleY, double acceleration,
        double recoilControl = 0, double smoothStrength = 0.35, double steadyAim = 0)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SENS_KEY);
        if (key == null) return;
        key.SetValue("GeneralSensitivity", generalSens.ToString("0.00"));
        key.SetValue("SensitivityCap", sensLimit.ToString("0.00"));
        key.SetValue("PreScaleX", preScaleX.ToString("0.00"));
        key.SetValue("PreScaleY", preScaleY.ToString("0.00"));
        key.SetValue("PostScaleX", postScaleX.ToString("0.00"));
        key.SetValue("PostScaleY", postScaleY.ToString("0.00"));
        key.SetValue("Acceleration", acceleration.ToString("0.00"));
        key.SetValue("RecoilControl", recoilControl.ToString("0.00"));
        key.SetValue("SmoothStrength", smoothStrength.ToString("0.00"));
        key.SetValue("SteadyAim", steadyAim.ToString("0.00"));
    }

    public static (double generalSens, double sensLimit,
        double preScaleX, double preScaleY, double postScaleX, double postScaleY, double acceleration,
        double recoilControl, double smoothStrength, double steadyAim)
        LoadAdvancedSettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SENS_KEY);
        return (
            GetDouble(key, "GeneralSensitivity", 1.0),
            GetDouble(key, "SensitivityCap", 10.0),
            GetDouble(key, "PreScaleX", 1.0),
            GetDouble(key, "PreScaleY", 1.0),
            GetDouble(key, "PostScaleX", 1.0),
            GetDouble(key, "PostScaleY", 1.0),
            GetDouble(key, "Acceleration", 1.0),
            GetDouble(key, "RecoilControl", 0.0),
            GetDouble(key, "SmoothStrength", 0.35),
            GetDouble(key, "SteadyAim", 0.0)
        );
    }

    #endregion

    #region Helpers

    private static int GetInt(RegistryKey? key, string name, int def)
    {
        if (key == null) return def;
        var val = key.GetValue(name);
        return val is int i ? i : def;
    }

    private static double GetDouble(RegistryKey? key, string name, double def)
    {
        if (key == null) return def;
        var val = key.GetValue(name);
        if (val != null && double.TryParse(val.ToString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d;
        return def;
    }

    #endregion

    #region Preferences (simple key-value)

    public static void SavePreference(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(REG_KEY);
            key?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    public static int LoadPreference(string name, int defaultValue = 0)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
            return GetInt(key, name, defaultValue);
        }
        catch { return defaultValue; }
    }

    #endregion

    #region Device & Emulator Settings

    public static void SaveDeviceSettings(int mouseIndex, int keyboardIndex, int emulatorIndex)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(REG_KEY);
            if (key == null) return;
            key.SetValue("SelectedMouseIndex", mouseIndex);
            key.SetValue("SelectedKeyboardIndex", keyboardIndex);
            key.SetValue("SelectedEmulatorIndex", emulatorIndex);
        }
        catch { }
    }

    public static (int mouseIndex, int keyboardIndex, int emulatorIndex) LoadDeviceSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
            return (
                GetInt(key, "SelectedMouseIndex", 0),
                GetInt(key, "SelectedKeyboardIndex", 0),
                GetInt(key, "SelectedEmulatorIndex", 0)
            );
        }
        catch { return (0, 0, 0); }
    }

    #endregion

    #region Window Size Persistence

    public static void SaveWindowSize(double width, double height, double left, double top, bool isMaximized)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(REG_KEY);
            if (key == null) return;
            key.SetValue("WindowWidth", (int)width);
            key.SetValue("WindowHeight", (int)height);
            key.SetValue("WindowLeft", (int)left);
            key.SetValue("WindowTop", (int)top);
            key.SetValue("WindowMaximized", isMaximized ? 1 : 0);
        }
        catch { }
    }

    public static (double width, double height, double left, double top, bool isMaximized) LoadWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
            return (
                GetInt(key, "WindowWidth", 560),
                GetInt(key, "WindowHeight", 740),
                GetInt(key, "WindowLeft", -1),
                GetInt(key, "WindowTop", -1),
                GetInt(key, "WindowMaximized", 0) == 1
            );
        }
        catch { return (560, 740, -1, -1, false); }
    }

    #endregion

    #region Chat History Persistence (Multi-Conversation)

    private const string CHAT_ROOT = @"SOFTWARE\LegitX V2\Conversations";
    private const string CHAT_META = @"SOFTWARE\LegitX V2\ConversationMeta";

    /// <summary>Chat session info stored in the history sidebar.</summary>
    public record ChatSession(string Id, string Subject, DateTime CreatedAt, int MessageCount);

    /// <summary>Save a conversation with a unique ID and AI-generated subject.</summary>
    public static void SaveConversation(string conversationId, string subject, List<(string role, string text)> messages)
    {
        try
        {
            // Save metadata
            using var meta = Registry.CurrentUser.CreateSubKey($@"{CHAT_META}\{conversationId}");
            if (meta == null) return;
            meta.SetValue("Subject", subject);
            meta.SetValue("CreatedAt", DateTime.UtcNow.ToString("o"));
            meta.SetValue("MessageCount", messages.Count);

            // Save messages
            using var key = Registry.CurrentUser.CreateSubKey($@"{CHAT_ROOT}\{conversationId}");
            if (key == null) return;

            // Clear existing
            foreach (var name in key.GetValueNames())
                key.DeleteValue(name);

            key.SetValue("Count", messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                key.SetValue($"Role_{i}", messages[i].role);
                key.SetValue($"Text_{i}", messages[i].text);
            }
        }
        catch { }
    }

    /// <summary>Load all conversation metadata, sorted newest first.</summary>
    public static List<ChatSession> LoadAllConversations()
    {
        var result = new List<ChatSession>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(CHAT_META);
            if (root == null) return result;

            foreach (var id in root.GetSubKeyNames())
            {
                try
                {
                    using var meta = root.OpenSubKey(id);
                    if (meta == null) continue;

                    var subject = meta.GetValue("Subject")?.ToString() ?? "Untitled";
                    var createdStr = meta.GetValue("CreatedAt")?.ToString() ?? "";
                    var count = 0;
                    if (meta.GetValue("MessageCount") is int c) count = c;
                    else if (int.TryParse(meta.GetValue("MessageCount")?.ToString(), out var parsed)) count = parsed;

                    DateTime.TryParse(createdStr, out var created);
                    result.Add(new ChatSession(id, subject, created, count));
                }
                catch { }
            }

            result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        }
        catch { }
        return result;
    }

    /// <summary>Load messages for a specific conversation ID.</summary>
    public static List<(string role, string text)> LoadConversation(string conversationId)
    {
        var result = new List<(string, string)>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{CHAT_ROOT}\{conversationId}");
            if (key == null) return result;

            int count = GetInt(key, "Count", 0);
            for (int i = 0; i < count; i++)
            {
                var role = key.GetValue($"Role_{i}")?.ToString() ?? "";
                var text = key.GetValue($"Text_{i}")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(role))
                    result.Add((role, text));
            }
        }
        catch { }
        return result;
    }

    /// <summary>Delete a single conversation.</summary>
    public static void DeleteConversation(string conversationId)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree($@"{CHAT_ROOT}\{conversationId}", false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree($@"{CHAT_META}\{conversationId}", false); } catch { }
    }

    /// <summary>Delete all conversations.</summary>
    public static void ClearAllConversations()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(CHAT_ROOT, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(CHAT_META, false); } catch { }
    }

    /// <summary>Generate a new unique conversation ID.</summary>
    public static string NewConversationId() => Guid.NewGuid().ToString("N")[..12];

    // Legacy compatibility
    private const string CHAT_KEY = @"SOFTWARE\LegitX V2\ChatHistory";
    public static void SaveChatHistory(List<(string role, string text)> messages) { }
    public static List<(string role, string text)> LoadChatHistory() => [];
    public static void ClearChatHistory()
    {
        try { Registry.CurrentUser.DeleteSubKey(CHAT_KEY, false); } catch { }
    }

    #endregion

    #region Import / Export Settings

    /// <summary>
    /// Data model for the exported JSON file.
    /// Contains all Sensitivity + Advanced settings that a user can share.
    /// </summary>
    public class ExportedSettings
    {
        public string Format { get; set; } = "LegitX_V2_Settings";
        public int Version { get; set; } = 2;
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

        // ── Main Sensitivity (doubles; JSON may still contain integers from older exports) ──
        public double MouseSensitivity { get; set; }
        public double XAxisSensitivity { get; set; }
        public double YAxisSensitivity { get; set; }
        public int DisplayWidth { get; set; }
        public int DisplayHeight { get; set; }
        public int EmulatorWidth { get; set; }
        public int EmulatorHeight { get; set; }
        public int CurveType { get; set; }

        // ── Advanced Tuning ──
        public double GeneralSensitivity { get; set; }
        public double SensitivityCap { get; set; }
        public double PreScaleX { get; set; }
        public double PreScaleY { get; set; }
        public double PostScaleX { get; set; }
        public double PostScaleY { get; set; }
        public double Acceleration { get; set; }

        // ── PRO Aim Features ──
        public double RecoilControl { get; set; }
        public double SmoothStrength { get; set; }
        public double SteadyAim { get; set; }

        // ── Mode-specific lock tuning ──
        public int UserLockStrictness { get; set; }
        public int KernelLockStrictness { get; set; }
        public double UserAccelX { get; set; }
        public double UserAccelY { get; set; }
        public double KernelAccelX { get; set; }
        public double KernelAccelY { get; set; }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions _jsonReadOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Export current Sensitivity + Advanced settings to a JSON file.</summary>
    public static string ExportSettingsToJson()
    {
        var (ms, xa, ya, dw, dh, ew, eh, ct) = LoadMainSettings();
        var (gs, sl, px, py, sx, sy, ac, rc, smooth, steady) = LoadAdvancedSettings();

        var data = new ExportedSettings
        {
            MouseSensitivity = ms < 0 ? 25.0 : ms,
            XAxisSensitivity = xa < 0 ? 15.0 : xa,
            YAxisSensitivity = ya < 0 ? 15.0 : ya,
            DisplayWidth = dw,
            DisplayHeight = dh,
            EmulatorWidth = ew,
            EmulatorHeight = eh,
            CurveType = ct,
            GeneralSensitivity = gs,
            SensitivityCap = sl,
            PreScaleX = px,
            PreScaleY = py,
            PostScaleX = sx,
            PostScaleY = sy,
            Acceleration = ac,
            RecoilControl = rc,
            SmoothStrength = smooth,
            SteadyAim = steady,
            UserLockStrictness = Math.Clamp(LoadPreference("UserLockStrictness", 1), 0, 3),
            KernelLockStrictness = Math.Clamp(LoadPreference("KernelLockStrictness", 2), 0, 3),
            UserAccelX = Math.Clamp(LoadPreference("UserAccelXx100", 100) / 100.0, 0.50, 1.80),
            UserAccelY = Math.Clamp(LoadPreference("UserAccelYx100", 92) / 100.0, 0.50, 1.80),
            KernelAccelX = Math.Clamp(LoadPreference("KernelAccelXx100", 100) / 100.0, 0.50, 1.80),
            KernelAccelY = Math.Clamp(LoadPreference("KernelAccelYx100", 85) / 100.0, 0.50, 1.80)
        };

        return JsonSerializer.Serialize(data, _jsonOpts);
    }

    /// <summary>Import settings from a JSON string. Returns null on success or an error message.</summary>
    public static string? ImportSettingsFromJson(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<ExportedSettings>(json, _jsonReadOpts);
            if (data == null) return "Invalid settings file — could not parse.";
            if (data.Format != "LegitX_V2_Settings") return "This file is not a LegitX V2 settings file.";

            SaveMainSettings(
                Math.Clamp(data.MouseSensitivity, 0.01, 100.0),
                Math.Clamp(data.XAxisSensitivity, 0.01, 100.0),
                Math.Clamp(data.YAxisSensitivity, 0.01, 100.0),
                data.DisplayWidth > 0 ? data.DisplayWidth : 1920,
                data.DisplayHeight > 0 ? data.DisplayHeight : 1080,
                data.EmulatorWidth > 0 ? data.EmulatorWidth : 1280,
                data.EmulatorHeight > 0 ? data.EmulatorHeight : 720,
                Math.Clamp(data.CurveType, 0, 2)
            );

            SaveAdvancedSettings(
                data.GeneralSensitivity,
                data.SensitivityCap,
                data.PreScaleX,
                data.PreScaleY,
                data.PostScaleX,
                data.PostScaleY,
                data.Acceleration,
                data.RecoilControl,
                data.SmoothStrength,
                data.SteadyAim
            );

            SavePreference("UserLockStrictness", Math.Clamp(data.UserLockStrictness, 0, 3));
            SavePreference("KernelLockStrictness", Math.Clamp(data.KernelLockStrictness, 0, 3));
            SavePreference("UserAccelXx100", (int)Math.Round(Math.Clamp(data.UserAccelX, 0.50, 1.80) * 100.0));
            SavePreference("UserAccelYx100", (int)Math.Round(Math.Clamp(data.UserAccelY, 0.50, 1.80) * 100.0));
            SavePreference("KernelAccelXx100", (int)Math.Round(Math.Clamp(data.KernelAccelX, 0.50, 1.80) * 100.0));
            SavePreference("KernelAccelYx100", (int)Math.Round(Math.Clamp(data.KernelAccelY, 0.50, 1.80) * 100.0));

            return null; // success
        }
        catch (JsonException)
        {
            return "Invalid settings file — JSON format error.";
        }
        catch (Exception ex)
        {
            return $"Import failed: {ex.Message}";
        }
    }

    #endregion
}
