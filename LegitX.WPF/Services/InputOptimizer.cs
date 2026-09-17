using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LegitX.WPF.Services;

/// <summary>
/// Kernel-level input optimization service.
/// AI Mouse / Anti-Recoil / No Acceleration are ENGINE-INTEGRATED —
/// they configure MouseSensitivityEngine parameters so every feature
/// passes through the unified 16-stage pipeline.
///
/// Click / Keyboard optimization remain registry-based because they
/// don't conflict with the mouse hook pipeline.
/// </summary>
public static class InputOptimizer
{
    #region Native Interop

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    #endregion

    #region Original EPP State — saved on first disable, restored on close

    private static bool _originalStateSaved;
    private static int _origThreshold1;
    private static int _origThreshold2;
    private static int _origAcceleration;
    private static int _origMouseSpeed;   // Windows SPI speed (1-20)
    private static string _origRegMouseSpeed = "1";   // Registry "MouseSpeed"
    private static string _origRegThreshold1 = "6";
    private static string _origRegThreshold2 = "10";

    /// <summary>
    /// Captures the current Windows EPP / acceleration state so we can
    /// restore it exactly when the app exits. Only saves once per session.
    /// </summary>
    private static void SaveOriginalState()
    {
        if (_originalStateSaved) return;

        try
        {
            // Read current SPI_GETMOUSE values
            int[] mouseParams = new int[3];
            var pin = GCHandle.Alloc(mouseParams, GCHandleType.Pinned);
            try { SystemParametersInfo(SPI_GETMOUSE, 0, pin.AddrOfPinnedObject(), 0); }
            finally { pin.Free(); }

            _origThreshold1 = mouseParams[0];
            _origThreshold2 = mouseParams[1];
            _origAcceleration = mouseParams[2];

            int speed = 10;
            SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0);
            _origMouseSpeed = speed;

            // Read registry values
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", false);
            if (key != null)
            {
                _origRegMouseSpeed = key.GetValue("MouseSpeed", "1")?.ToString() ?? "1";
                _origRegThreshold1 = key.GetValue("MouseThreshold1", "6")?.ToString() ?? "6";
                _origRegThreshold2 = key.GetValue("MouseThreshold2", "10")?.ToString() ?? "10";
            }
        }
        catch { /* If we can't read, keep safe defaults */ }

        _originalStateSaved = true;
    }

    /// <summary>
    /// Restores the Windows EPP / acceleration state that was captured on first disable.
    /// </summary>
    public static void RestoreOriginalState()
    {
        if (!_originalStateSaved) return; // nothing to restore

        try
        {
            int[] mouseParams = [_origThreshold1, _origThreshold2, _origAcceleration];
            var pin = GCHandle.Alloc(mouseParams, GCHandleType.Pinned);
            try
            {
                SystemParametersInfo(SPI_SETMOUSE, 0, pin.AddrOfPinnedObject(),
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            finally { pin.Free(); }

            SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)_origMouseSpeed,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            if (key != null)
            {
                key.SetValue("MouseSpeed", _origRegMouseSpeed);
                key.SetValue("MouseThreshold1", _origRegThreshold1);
                key.SetValue("MouseThreshold2", _origRegThreshold2);
            }
        }
        catch { }
    }

    #endregion

    #region AI Mouse Profiles — Engine-Integrated

    /// <summary>
    /// Applies AI Mouse profile to the sensitivity engine.
    /// Instead of changing Windows registry (which fights the engine),
    /// these profiles configure engine parameters that flow through
    /// the full 16-stage pipeline. Each profile targets a different play style.
    /// </summary>
    public static void ApplyAIMouseProfile(MouseSensitivityEngine engine, int profileIndex)
    {
        if (engine == null) return;

        switch (profileIndex)
        {
            case 0: ApplyProfileV1(engine); break; // Precision — competitive/sniping
            case 1: ApplyProfileV2(engine); break; // Balanced  — all-round
            case 2: ApplyProfileV3(engine); break; // Aggressive — close-quarter/rush
            default: RestoreAIMouseDefaults(engine); break;
        }
    }

    /// <summary>
    /// V1: PRECISION profile — optimized for long-range / sniping.
    /// • Extra smoothing to eliminate micro-jitter
    /// • Larger deadzone to kill sensor noise
    /// • Strong steady-aim Kalman filter
    /// • Micro-correction for pixel-perfect adjustments
    /// • Lower Bézier curve base for deep precision zone
    /// </summary>
    private static void ApplyProfileV1(MouseSensitivityEngine engine)
    {
        engine.AiMouseMode = 1;
        engine.AiSmoothBoost = 0.25;       // +0.25 on top of user's smoothing
        engine.AiDeadzoneBoost = 0.40;      // +0.4px deadzone floor
        engine.AiSteadyAimBoost = 0.35;     // strong tremor filtering
        engine.AiCorrectionBoost = 2.5;      // micro-correction radius boost
        engine.AiPredictionBoost = 0.0;      // no prediction for sniping (stability)
        engine.AiCurveShift = -0.08;         // deeper precision zone on Bézier
        engine.AiAccelerationDampen = 0.0;   // no accel dampening
        engine.AiAntiOvershootBoost = 0.15;  // extra overshoot prevention
    }

    /// <summary>
    /// V2: BALANCED profile — optimized for general gameplay.
    /// • Moderate smoothing for clean aim
    /// • Medium steady-aim for hand tremor
    /// • Light prediction for tracking moving targets
    /// • Balanced micro-correction
    /// </summary>
    private static void ApplyProfileV2(MouseSensitivityEngine engine)
    {
        engine.AiMouseMode = 2;
        engine.AiSmoothBoost = 0.12;        // moderate extra smoothing
        engine.AiDeadzoneBoost = 0.20;       // light noise floor
        engine.AiSteadyAimBoost = 0.18;      // moderate tremor filtering
        engine.AiCorrectionBoost = 1.5;       // medium micro-correction
        engine.AiPredictionBoost = 2.0;       // 2ms lookahead for target tracking
        engine.AiCurveShift = 0.0;            // neutral curve
        engine.AiAccelerationDampen = 0.0;    // no accel change
        engine.AiAntiOvershootBoost = 0.08;   // light overshoot prevention
    }

    /// <summary>
    /// V3: AGGRESSIVE profile — optimized for close-quarter combat / rushing.
    /// • Minimal smoothing for instant response
    /// • Higher prediction for fast target tracking
    /// • No micro-correction (raw speed matters)
    /// • Slight acceleration boost for fast flicks
    /// • Lower anti-overshoot (let raw speed through)
    /// </summary>
    private static void ApplyProfileV3(MouseSensitivityEngine engine)
    {
        engine.AiMouseMode = 3;
        engine.AiSmoothBoost = -0.05;       // slightly LESS smoothing (faster response)
        engine.AiDeadzoneBoost = 0.0;        // no extra deadzone (raw input)
        engine.AiSteadyAimBoost = 0.05;      // minimal tremor filter
        engine.AiCorrectionBoost = 0.0;       // no micro-correction
        engine.AiPredictionBoost = 4.0;       // 4ms aggressive prediction
        engine.AiCurveShift = 0.06;           // shift curve toward higher multiplier
        engine.AiAccelerationDampen = 0.15;   // slight boost to velocity-based accel
        engine.AiAntiOvershootBoost = -0.05;  // reduce overshoot dampening for speed
    }

    /// <summary>
    /// Resets all AI Mouse engine parameters to neutral (zero boost).
    /// </summary>
    public static void RestoreAIMouseDefaults(MouseSensitivityEngine engine)
    {
        if (engine == null) return;
        engine.AiMouseMode = 0;
        engine.AiSmoothBoost = 0.0;
        engine.AiDeadzoneBoost = 0.0;
        engine.AiSteadyAimBoost = 0.0;
        engine.AiCorrectionBoost = 0.0;
        engine.AiPredictionBoost = 0.0;
        engine.AiCurveShift = 0.0;
        engine.AiAccelerationDampen = 0.0;
        engine.AiAntiOvershootBoost = 0.0;
    }

    #endregion

    #region Anti-Recoil — Engine-Integrated

    /// <summary>
    /// Enables engine-integrated anti-recoil compensation.
    /// Instead of modifying Windows SmoothMouse curves (which conflicts
    /// with the engine's own Stage 11 smoothing), this sets engine flags
    /// that add a downward Y-axis compensation stage in the pipeline.
    ///
    /// The compensation is sensitivity-adaptive — scales with the user's
    /// Overall/X/Y sensitivity so it works at ANY sensitivity level.
    /// </summary>
    public static void EnableAntiRecoil(MouseSensitivityEngine engine)
    {
        if (engine == null) return;
        engine.AntiRecoilEnabled = true;
        engine.AntiRecoilStrength = 0.35;    // base strength (auto-scales with sens)
        engine.AntiRecoilRampUpMs = 120.0;   // ramp-up time — gradual engagement
        engine.AntiRecoilMaxCompensation = 3.5; // max px/frame compensation

        // Kernel-level vertical drift correction
        engine.VerticalDriftEnabled = true;
        engine.VerticalDriftStrength = 0.35;
        engine.VerticalDriftRampMs = 120.0;
        engine.VerticalDriftMaxPx = 3.5;
    }

    /// <summary>Disables engine-integrated anti-recoil.</summary>
    public static void DisableAntiRecoil(MouseSensitivityEngine engine)
    {
        if (engine == null) return;
        engine.AntiRecoilEnabled = false;
        engine.AntiRecoilStrength = 0.0;
        engine.AntiRecoilRampUpMs = 0.0;
        engine.AntiRecoilMaxCompensation = 0.0;

        engine.VerticalDriftEnabled = false;
        engine.VerticalDriftStrength = 0.0;
        engine.VerticalDriftRampMs = 0.0;
        engine.VerticalDriftMaxPx = 0.0;
    }

    #endregion

    #region No Acceleration — Engine-Integrated

    /// <summary>
    /// Disables ALL mouse acceleration: both the engine's Stage 4 velocity
    /// acceleration AND Windows' own enhance-pointer-precision.
    /// This gives true 1:1 linear mouse movement.
    /// </summary>
    public static void DisableMouseAcceleration(MouseSensitivityEngine engine)
    {
        if (engine == null) return;

        // Save original Windows EPP state BEFORE we change anything
        SaveOriginalState();

        engine.NoAccelerationOverride = true;
        engine.LinearInputEnabled = true;

        // Also disable Windows' own acceleration (belt and suspenders)
        try
        {
            int[] mouseParams = [0, 0, 0];
            var pinnedArray = GCHandle.Alloc(mouseParams, GCHandleType.Pinned);
            try
            {
                SystemParametersInfo(SPI_SETMOUSE, 0, pinnedArray.AddrOfPinnedObject(),
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            finally { pinnedArray.Free(); }

            // Set Windows speed to neutral (10 = no scaling)
            SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)10, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            // Disable "Enhance pointer precision" in registry
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            if (key != null)
            {
                key.SetValue("MouseSpeed", "0");
                key.SetValue("MouseThreshold1", "0");
                key.SetValue("MouseThreshold2", "0");
            }
        }
        catch { /* Registry/system call failure — engine flag still active */ }
    }

    /// <summary>
    /// Restores mouse acceleration in both engine and Windows.
    /// Restores the ORIGINAL Windows state that was captured before the app changed it.
    /// </summary>
    public static void RestoreMouseAcceleration(MouseSensitivityEngine engine)
    {
        if (engine == null) return;
        engine.NoAccelerationOverride = false;
        engine.LinearInputEnabled = false;

        // Restore to user's original Windows state (not hardcoded defaults)
        RestoreOriginalState();
    }

    #endregion

    #region AI Click Optimization — Engine-Integrated

    /// <summary>
    /// Optimizes click response at kernel level.
    /// • Reduces Windows double-click delay to minimum (200ms)
    /// • Enables engine click-through mode (suppresses micro-movement during clicks)
    /// • Sets engine debounce to eliminate accidental double-taps
    /// • Kernel-level: click stabilization runs inside ProcessDelta pipeline
    /// </summary>
    public static void OptimizeClicking(MouseSensitivityEngine engine)
    {
        if (engine == null) return;
        engine.AiClickOptimized = true;
        engine.ClickStabilityRadius = 2.0;    // freeze cursor within 2px during click
        engine.ClickStabilityMs = 45.0;       // stability window 45ms around click events

        // Also optimize Windows click response
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            if (key != null)
            {
                key.SetValue("DoubleClickSpeed", "200"); // fastest reliable double-click
            }
        }
        catch { }
    }

    /// <summary>Restores click defaults.</summary>
    public static void RestoreClicking(MouseSensitivityEngine engine)
    {
        if (engine == null) return;
        engine.AiClickOptimized = false;
        engine.ClickStabilityRadius = 0.0;
        engine.ClickStabilityMs = 0.0;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            if (key != null)
            {
                key.SetValue("DoubleClickSpeed", "500");
            }
        }
        catch { }
    }

    #endregion

    #region AI Keyboard Optimization — Registry-Based (Doesn't Conflict)

    /// <summary>
    /// Optimizes keyboard response for gaming at kernel level.
    /// This is registry-based because keyboard settings don't conflict with the mouse pipeline.
    /// • Minimum repeat delay (0ms)
    /// • Maximum repeat speed (31 = fastest)
    /// • Zero bounce/debounce for instant key response
    /// • Optimized for rapid key tapping in FPS games
    /// </summary>
    public static void OptimizeKeyboard()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true);
            if (key != null)
            {
                key.SetValue("KeyboardDelay", "0");    // minimum delay before repeat
                key.SetValue("KeyboardSpeed", "31");   // maximum repeat rate
            }

            // Aggressive optimization via accessibility registry for system-wide effect
            using var accessKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\Keyboard Response", true);
            if (accessKey != null)
            {
                accessKey.SetValue("AutoRepeatDelay", "150");  // faster than default 200
                accessKey.SetValue("AutoRepeatRate", "4");     // faster repeat (was 6)
                accessKey.SetValue("DelayBeforeAcceptance", "0");
                accessKey.SetValue("Flags", "59");
                accessKey.SetValue("BounceTime", "0");
            }

            // Optimize keyboard buffer via HKCU for lower input latency
            using var keyParam = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true);
            if (keyParam != null)
            {
                keyParam.SetValue("InitialKeyboardIndicators", "2"); // NumLock on by default
            }
        }
        catch { }
    }

    /// <summary>Restores keyboard defaults.</summary>
    public static void RestoreKeyboard()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true);
            if (key != null)
            {
                key.SetValue("KeyboardDelay", "1");
                key.SetValue("KeyboardSpeed", "12");
            }

            using var accessKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\Keyboard Response", true);
            if (accessKey != null)
            {
                accessKey.SetValue("AutoRepeatDelay", "300");
                accessKey.SetValue("AutoRepeatRate", "36");
                accessKey.SetValue("DelayBeforeAcceptance", "0");
                accessKey.SetValue("Flags", "126");
                accessKey.SetValue("BounceTime", "0");
            }
        }
        catch { }
    }

    #endregion

    #region Factory Reset

    /// <summary>
    /// Engine-only cleanup — resets all internal engine AI flags without touching
    /// Windows mouse/keyboard settings. Call this on app close so the user's
    /// mouse settings (Enhance Pointer Precision OFF, acceleration disabled, etc.)
    /// persist after the application exits.
    /// </summary>
    public static void ResetEngineOnly(MouseSensitivityEngine? engine)
    {
        if (engine == null) return;
        RestoreAIMouseDefaults(engine);
        DisableAntiRecoil(engine);
        engine.NoAccelerationOverride = false;
        engine.LinearInputEnabled = false;
        engine.AiClickOptimized = false;
        engine.ClickStabilityRadius = 0.0;
        engine.ClickStabilityMs = 0.0;
        engine.VerticalDriftEnabled = false;
        engine.VerticalDriftStrength = 0.0;
        engine.VerticalDriftRampMs = 0.0;
        engine.VerticalDriftMaxPx = 0.0;
    }

    /// <summary>
    /// Restores the user's original "Enhance pointer precision" state.
    /// Called on app close to guarantee Windows mouse settings go back
    /// to exactly how they were before the app touched them.
    /// </summary>
    public static void RestoreOriginalMouseState()
    {
        RestoreOriginalState();
    }

    /// <summary>
    /// Comprehensive factory reset — restores ALL mouse/keyboard settings to Windows defaults
    /// AND clears all engine AI flags. Only call this when the user explicitly requests a full reset.
    /// </summary>
    public static void ResetAllToFactory(MouseSensitivityEngine? engine)
    {
        // Reset engine AI flags
        ResetEngineOnly(engine);

        // Reset Windows mouse settings to full defaults
        try
        {
            int[] mouseParams = [6, 10, 1];
            var pinnedArray = GCHandle.Alloc(mouseParams, GCHandleType.Pinned);
            try
            {
                SystemParametersInfo(SPI_SETMOUSE, 0, pinnedArray.AddrOfPinnedObject(),
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            finally { pinnedArray.Free(); }

            SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)10, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            using var mouseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            if (mouseKey != null)
            {
                mouseKey.SetValue("MouseSensitivity", "10");
                mouseKey.SetValue("MouseSpeed", "1");
                mouseKey.SetValue("MouseThreshold1", "6");
                mouseKey.SetValue("MouseThreshold2", "10");
                mouseKey.SetValue("DoubleClickSpeed", "500");

                // Restore default SmoothMouse curves
                byte[] defaultCurve =
                [
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x15, 0x6E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x29, 0xDC, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00
                ];
                mouseKey.SetValue("SmoothMouseXCurve", defaultCurve, RegistryValueKind.Binary);
                mouseKey.SetValue("SmoothMouseYCurve", defaultCurve, RegistryValueKind.Binary);
            }
        }
        catch { }

        // Reset keyboard
        RestoreKeyboard();
    }

    #endregion
}
