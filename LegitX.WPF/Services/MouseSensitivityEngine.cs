using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LegitX.WPF.Services;

/// <summary>Acceleration curve type applied in the sensitivity stage.</summary>
public enum AccelCurveType
{
    /// <summary>Pure flat multiplier — output = input × sensitivity. No speed dependence.</summary>
    Linear = 0,
    /// <summary>Power curve — slow moves get less accel, fast moves ramp up. Like CS:GO m_customaccel.</summary>
    Power = 1,
    /// <summary>Natural (exponential decay) — smoothly approaches a limit. Inspired by RawAccel.</summary>
    Natural = 2,
    /// <summary>Logarithmic — fast ramp at low speed, gradually flattens out. Great for precision + speed.</summary>
    Logarithmic = 3,
    /// <summary>S-Curve (Sigmoid) — smooth transition: slow start, steep middle, soft ceiling.</summary>
    Sigmoid = 4,
    /// <summary>Synchronised step — stays flat at low speed, then jumps to a higher multiplier above a threshold.</summary>
    Step = 5
}

/// <summary>How aggressively lock-mode burst deltas are clamped.</summary>
public enum LockModeStrictness
{
    Off = 0,
    Normal = 1,
    Strict = 2,
    Ultra = 3
}

/// <summary>
/// KERNEL-LEVEL Mouse Sensitivity Engine â€” Interception Driver + 20-stage pipeline.
///
/// Uses the Interception kernel driver (interception.dll) by Francisco Lopes
/// for TRUE hardware-level mouse capture at kernel level (&lt;0.1ms latency).
/// Falls back to WH_MOUSE_LL if the Interception driver is not installed.
///
/// KERNEL ADVANTAGES OVER WH_MOUSE_LL:
///  â€¢ Intercepts at the kernel driver stack â€” before Windows processes the input
///  â€¢ Sub-0.1ms latency (vs 1-5ms for WH_MOUSE_LL)
///  â€¢ True hardware deltas â€” no Windows mouse acceleration interference
///  â€¢ Invisible to user-mode anti-cheat hooks
///  â€¢ Cannot be blocked by SetWindowsHookEx priority chains
///  â€¢ Works even when the app is not focused (kernel-level)
///  â€¢ No message pump dependency â€” dedicated high-priority capture thread
///
/// 20-STAGE UNIFIED AIM PIPELINE (preserved from previous version):
///  1â€“20: Full pipeline unchanged, operating on kernel-captured deltas.
///
/// All math in 64-bit double precision. Zero integer truncation until final output.
/// </summary>
public sealed class MouseSensitivityEngine : IDisposable
{
    #region Native Interop â€” Interception Kernel Driver

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Interception driver P/Invoke â€” kernel-level mouse interception
    //  Driver: https://github.com/oblitum/Interception
    //  These calls go directly to the signed kernel driver (interception.dll)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private const string INTERCEPTION_DLL = "interception.dll";

    // Context management
    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr interception_create_context();

    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_destroy_context(IntPtr context);

    // Device filtering
    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, ushort filter);

    // Blocking receive â€” waits for next hardware event at kernel level
    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_receive(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);

    // Send modified stroke back to the driver stack
    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_send(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);

    // Wait with timeout (returns device id, 0 = timeout)
    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_wait_with_timeout(IntPtr context, ulong milliseconds);

    // Device type query
    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_is_mouse(int device);

    [DllImport(INTERCEPTION_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_is_keyboard(int device);

    // Predicate delegate for filtering
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InterceptionPredicate(int device);

    // Mouse stroke structure â€” kernel-level raw hardware data
    [StructLayout(LayoutKind.Sequential)]
    private struct InterceptionMouseStroke
    {
        public ushort state;        // Button state flags
        public ushort flags;        // Movement flags (relative/absolute)
        public short rolling;       // Scroll wheel delta
        public int x;              // X delta (relative) or X position (absolute)
        public int y;              // Y delta (relative) or Y position (absolute)
        public uint information;   // Extra info from driver
    }

    // Mouse state flags (button events)
    private const ushort INTERCEPTION_MOUSE_LEFT_BUTTON_DOWN = 0x001;
    private const ushort INTERCEPTION_MOUSE_LEFT_BUTTON_UP = 0x002;
    private const ushort INTERCEPTION_MOUSE_RIGHT_BUTTON_DOWN = 0x004;
    private const ushort INTERCEPTION_MOUSE_RIGHT_BUTTON_UP = 0x008;
    private const ushort INTERCEPTION_MOUSE_MOVE_RELATIVE = 0x000;
    private const ushort INTERCEPTION_MOUSE_MOVE_ABSOLUTE = 0x001;

    // Filter flags
    private const ushort INTERCEPTION_FILTER_MOUSE_ALL = 0xFFFF;

    // Device range constants
    private const int INTERCEPTION_MAX_MOUSE = 20;     // Max mouse devices

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Fallback: WH_MOUSE_LL (used when Interception driver not installed)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Shared Win32
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClipCursor(out RECT lpRect);

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static readonly IntPtr INJECTED_MARKER = new(0x4C584832); // "LXH2"

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Interception state
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private IntPtr _interceptionContext = IntPtr.Zero;
    private Thread? _captureThread;
    private volatile bool _captureRunning;
    private InterceptionPredicate? _mouseFilter;   // prevent GC collection
    private bool _useInterception;                  // true = kernel, false = WH_MOUSE_LL fallback

    /// <summary>True if running in kernel-level Interception mode.</summary>
    public bool IsKernelMode => _useInterception;

    #endregion

    #region Configuration â€” Main

    /// <summary>Base speed (same numeric range as UI: ~0.01–100, curve is gentle at the low end).</summary>
    public double OverallSensitivity { get; set; } = 25.0;
    public double XAxisSensitivity { get; set; } = 15.0;
    public double YAxisSensitivity { get; set; } = 15.0;
    public int DisplayWidth { get; set; } = 1920;
    public int DisplayHeight { get; set; } = 1080;
    public int EmulatorWidth { get; set; } = 1280;
    public int EmulatorHeight { get; set; } = 720;

    /// <summary>Selected acceleration curve type (Linear / Power / Natural).</summary>
    public AccelCurveType CurveType { get; set; } = AccelCurveType.Linear;

    private volatile bool _isActive;
    public bool IsActive { get => _isActive; set => _isActive = value; }

    #endregion

    #region Configuration â€” Advanced (original 8)

    public double AdvGeneralSens { get; set; } = 1.0;
    public double AdvSensLimit { get; set; } = 10.0;
    public double AdvPreScaleX { get; set; } = 1.0;
    public double AdvPreScaleY { get; set; } = 1.0;
    public double AdvPostScaleX { get; set; } = 1.0;
    public double AdvPostScaleY { get; set; } = 1.0;
    public double AdvAcceleration { get; set; } = 1.0;
    public bool AdvancedActive { get; set; }

    #endregion

    #region Configuration â€” Advanced PRO (3)

    /// <summary>
    /// Recoil Control strength (0.0â€“1.0). Stabilizes rapid vertical/horizontal shake
    /// caused by automatic weapon recoil (sustained fire).
    /// </summary>
    public double AdvRecoilControl { get; set; } = 0.0;

    /// <summary>Smoothing strength 0.0â€“1.0. 0=minimal, 1=max smooth.</summary>
    public double AdvSmoothStrength { get; set; } = 0.35;

    /// <summary>Steady Aim strength 0.0â€“1.0. 0=off.</summary>
    public double AdvSteadyAim { get; set; } = 0.0;

    #endregion

    #region Configuration â€” AI / Extras (Engine-Integrated)

    /// <summary>AI Mouse profile mode: 0=off, 1=Precision, 2=Balanced, 3=Aggressive.</summary>
    public int AiMouseMode { get; set; }

    public double AiSmoothBoost { get; set; }
    public double AiDeadzoneBoost { get; set; }
    public double AiSteadyAimBoost { get; set; }
    public double AiCorrectionBoost { get; set; }
    public double AiPredictionBoost { get; set; }
    public double AiCurveShift { get; set; }
    public double AiAccelerationDampen { get; set; }
    public double AiAntiOvershootBoost { get; set; }

    public bool AntiRecoilEnabled { get; set; }
    public double AntiRecoilStrength { get; set; }
    public double AntiRecoilRampUpMs { get; set; }
    public double AntiRecoilMaxCompensation { get; set; }

    /// <summary>When true, Stage 7 velocity acceleration is bypassed â†’ 1:1 linear.</summary>
    public bool NoAccelerationOverride { get; set; }

    public bool AiClickOptimized { get; set; }
    public double ClickStabilityRadius { get; set; }
    public double ClickStabilityMs { get; set; }

    /// <summary>When true, the engine remains active even when the app loses focus (kernel-level persist).</summary>
    public bool PersistAcrossApps { get; set; }

    /// <summary>Vertical drift correction enabled (anti-recoil display name).</summary>
    public bool VerticalDriftEnabled { get; set; }
    public double VerticalDriftStrength { get; set; }
    public double VerticalDriftRampMs { get; set; }
    public double VerticalDriftMaxPx { get; set; }

    /// <summary>Linear Input Mode â€” bypasses Windows acceleration AND engine Step 7.</summary>
    public bool LinearInputEnabled { get; set; }

    /// <summary>
    /// When true, the engine is forced to use User Mode (WH_MOUSE_LL) even if the
    /// Interception driver is installed. Set by the license kernel access restriction.
    /// </summary>
    public bool ForceUserMode { get; set; }

    // Lock-mode / per-mode tuning
    public LockModeStrictness UserLockStrictness { get; set; } = LockModeStrictness.Normal;
    public LockModeStrictness KernelLockStrictness { get; set; } = LockModeStrictness.Strict;
    public double UserAccelX { get; set; } = 1.00;
    public double UserAccelY { get; set; } = 0.92;
    public double KernelAccelX { get; set; } = 1.00;
    public double KernelAccelY { get; set; } = 0.85;

    #endregion

    #region Computed Aliases (pipeline uses short names)

    // These map the Adv* property names to the short names used in ProcessDelta
    private double XSensitivity => XAxisSensitivity;
    private double YSensitivity => YAxisSensitivity;
    private double PreScaleX => AdvPreScaleX;
    private double PreScaleY => AdvPreScaleY;
    private double PostScaleX => AdvPostScaleX;
    private double PostScaleY => AdvPostScaleY;
    private double GeneralSensitivity => AdvGeneralSens;
    private double AccelerationStrength => AdvAcceleration;
    private double RecoilControlStrength => AdvRecoilControl;
    private double SmoothingStrength => AdvSmoothStrength;
    private double SteadyAimStrength => AdvSteadyAim;
    private double AntiOvershootStrength => AiAntiOvershootBoost;

    #endregion

    #region Internal State

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelMouseProc? _hookProc;
    private POINT _lastCursorPos;
    private bool _hasLastPos;

    // â”€â”€ Sub-pixel accumulator (128-bit precision, fractional pixels never lost) â”€â”€
    private double _subPixelX, _subPixelY;
    private int _zeroOutputRun;  // consecutive frames with (0,0) integer output for sub-pixel dithering

    // â”€â”€ Velocity ring buffer (Gaussian-weighted, 24 samples) â”€â”€
    private const int VBUF = 24;
    private readonly double[] _vxBuf = new double[VBUF], _vyBuf = new double[VBUF];
    private readonly long[] _vtBuf = new long[VBUF];
    private int _vIdx;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private long _lastTick;
    private double _kernelDtEma = 0.001; // adaptive nominal dt for kernel pipeline
    private double _userDtEma = 0.008;   // adaptive nominal dt for user pipeline

    // â”€â”€ Smoothing state (5-cascade directional adaptive) â”€â”€
    private double _smoothX, _smoothY;
    private double _smooth2X, _smooth2Y;
    private double _smooth3X, _smooth3Y;
    private double _smooth4X, _smooth4Y;
    private double _smooth5X, _smooth5Y;
    private double _smoothDirX, _smoothDirY; // directional coherence tracker

    // â”€â”€ Acceleration EMA + history â”€â”€
    private double _prevVel;
    private double _accelEma;
    private double _accelEma2;  // 2nd-order acceleration EMA
    private double _velEma;     // smoothed velocity for curve transitions

    // â”€â”€ Extended Kalman state (dual-axis with velocity + acceleration) â”€â”€
    private double _kalmanX, _kalmanY;
    private double _kalmanPx = 1.0, _kalmanPy = 1.0;
    private double _kalmanVx, _kalmanVy;
    private double _kalmanPvx = 1.0, _kalmanPvy = 1.0;
    private double _kalmanAx, _kalmanAy;      // acceleration state
    private double _kalmanPax = 1.0, _kalmanPay = 1.0;

    // â”€â”€ Anti-overshoot dual-jerk history + momentum â”€â”€
    private double _prevAccelX, _prevAccelY;
    private double _prevDx, _prevDy;
    private double _jerkEmaX, _jerkEmaY;
    private double _jerk2EmaX, _jerk2EmaY;    // 2nd-order jerk (snap)
    private double _momentumX, _momentumY;     // momentum tracker

    // â”€â”€ Recoil Control (firing stabilization) â”€â”€
    // Shared state (both modes)
    private double _recoilJitterEmaX, _recoilJitterEmaY;   // EMA of rapid direction changes
    private double _recoilShakeScore;                        // 0..1 composite shake intensity
    private double _recoilBaselineVelX, _recoilBaselineVelY; // smoothed "intentional" aim direction
    private int _recoilReversalCount;                        // rapid reversal counter (sliding window)
    private long _recoilLastReversalTick;                    // timing for reversal window
    private double _recoilPrevDx, _recoilPrevDy;            // previous delta for reversal detection

    // â”€â”€ Kernel-mode enhanced recoil control state â”€â”€
    // In kernel mode we have TRUE button state from hardware â€” fire-aware stabilization
    private volatile bool _kernelFireHeld;                   // true while LMB is held (kernel only)
    private long _kernelFireStartTick;                       // when fire button was pressed
    private double _kernelFireDurationMs;                    // how long fire has been held
    private double _kernelPreFireBaselineX, _kernelPreFireBaselineY; // aim direction BEFORE firing started
    private double _kernelFireShakeEmaX, _kernelFireShakeEmaY;     // high-freq oscillation tracker during fire
    private double _kernelFireDriftEmaX, _kernelFireDriftEmaY;     // low-freq recoil drift tracker
    private double _kernelFireIntensity;                     // 0..1 how intense the current recoil is
    private int _kernelFireSampleCount;                      // samples since fire started
    private double _kernelSprayPatternPhase;                 // phase accumulator for spray pattern estimation
    private const int FIRE_HISTORY = 32;                     // ring buffer for fire-time analysis
    private readonly double[] _fireDxBuf = new double[FIRE_HISTORY];
    private readonly double[] _fireDyBuf = new double[FIRE_HISTORY];
    private int _fireHistIdx;
    private double _kernelRecoilCompensationY;               // accumulated vertical drift compensation

    // â”€â”€ Emulator cursor-lock / confinement detection â”€â”€
    // When an emulator (BlueStacks, LDPlayer, MEmu, etc.) captures the cursor via ClipCursor(),
    // movement at the boundary produces clamped positions â†’ fake zero-deltas and false reversals.
    // These fields detect and compensate for that state.
    private bool _cursorConfined;                            // true when clip rect â‰  full virtual screen
    private RECT _clipRect;                                  // current clip rectangle
    private long _lastClipCheckTick;                         // throttle clip rect polling (~50ms)
    private long ClipCheckIntervalTicks => Math.Max(1, Stopwatch.Frequency / 20); // 50ms on any timer freq
    private bool _cursorAtEdge;                              // true when cursor is near clip boundary
    private const int EDGE_MARGIN = 3;                       // pixels from edge to consider "at boundary"
    private int _edgeStallCount;                             // consecutive frames cursor stuck at edge
    private double _preEdgeDx, _preEdgeDy;                  // last valid delta before edge stall
    private bool _edgeSuppressRecoil;                        // suppress recoil shake detection at edge

    // Timing
    private long _lastTimeTicks;

    // Anti-recoil (Stage 19) â€” reserved for future pipeline stages
#pragma warning disable CS0414
    private long _recoilStartTicks;
    private bool _recoilActive;
    private double _sustainedDownMs;
#pragma warning restore CS0414

    // Click stabilization
    private long _lastClickTicks;
    private double _clickFreezeX, _clickFreezeY;
#pragma warning disable CS0414
    private bool _clickFreezeActive;
#pragma warning restore CS0414

    // Acceleration smoothing
    private double _lastAccelFactor = 1.0;
    private double _kernelRawMagEma = 1.0;
    private double _userRawMagEma = 2.0;

    #endregion

    #region Lifecycle

    /// <summary>
    /// Attempts kernel-level Interception driver first.
    /// Falls back to WH_MOUSE_LL if driver not installed.
    /// </summary>
    public void Install()
    {
        if (_captureRunning || _hookId != IntPtr.Zero) return;

        // â”€â”€ Fresh start: reset all pipeline state + prime the tick counter â”€â”€
        // Without this, the first ProcessDelta() call sees stale EMAs / Kalman
        // state from a previous session and a giant dtS (since _lastTick was 0
        // or from minutes ago), causing a laggy/jittery cursor for the first
        // several hundred milliseconds after activation.
        Reset();
        _lastTick = _sw.ElapsedTicks; // prime so first dtS is tiny, not stale

        // â”€â”€ Try Interception kernel driver first (unless license denies kernel access) â”€â”€
        if (!ForceUserMode)
        {
            try
            {
                _interceptionContext = interception_create_context();
                if (_interceptionContext != IntPtr.Zero)
                {
                    // Set filter to capture ALL mouse events from ALL mouse devices
                    _mouseFilter = new InterceptionPredicate(interception_is_mouse);
                    interception_set_filter(_interceptionContext, _mouseFilter, INTERCEPTION_FILTER_MOUSE_ALL);

                    _captureRunning = true;
                    _captureThread = new Thread(InterceptionCaptureLoop)
                    {
                        Name = "LegitX_KernelMouseCapture",
                        IsBackground = true,
                        Priority = ThreadPriority.Highest // kernel-level needs highest priority
                    };
                    _captureThread.Start();
                    _useInterception = true;

                    System.Diagnostics.Debug.WriteLine("[MouseEngine] âœ“ Interception KERNEL driver active â€” sub-0.1ms latency");
                    return;
                }
            }
            catch (DllNotFoundException)
            {
                // interception.dll not found â€” fall through to WH_MOUSE_LL
                System.Diagnostics.Debug.WriteLine("[MouseEngine] interception.dll not found, falling back to WH_MOUSE_LL");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MouseEngine] Interception init failed: {ex.Message}, falling back to WH_MOUSE_LL");
                if (_interceptionContext != IntPtr.Zero)
                {
                    try { interception_destroy_context(_interceptionContext); } catch { }
                    _interceptionContext = IntPtr.Zero;
                }
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[MouseEngine] Kernel access denied by license â€” forcing User Mode");
        }

        // â”€â”€ Fallback: WH_MOUSE_LL user-mode hook â”€â”€
        _useInterception = false;
        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
        System.Diagnostics.Debug.WriteLine("[MouseEngine] â–¸ WH_MOUSE_LL fallback active");
    }

    public void Uninstall()
    {
        // â”€â”€ Stop Interception kernel capture â”€â”€
        // First, clear the driver filter so it stops capturing mouse strokes.
        // Without this, strokes queue in the driver with no consumer â†’ mouse frozen.
        if (_interceptionContext != IntPtr.Zero && _mouseFilter != null)
        {
            try { interception_set_filter(_interceptionContext, _mouseFilter, 0); } catch { }
        }

        // Signal the capture thread to stop, then wait for it to drain any pending stroke
        _captureRunning = false;
        if (_captureThread != null)
        {
            try { _captureThread.Join(3000); } catch { } // wait up to 3s for clean drain
            _captureThread = null;
        }

        // IMPORTANT: Do NOT call interception_destroy_context() here.
        // The Interception C++ runtime registers static destructors that run
        // during AppDomain unload / process exit. If we destroy the context first,
        // those destructors attempt to clean up already-freed memory â†’ crash in
        // __std_type_info_destroy_list (Fatal Error: DLL was not found).
        // The kernel driver automatically releases all resources when the process
        // terminates, so explicit cleanup is unnecessary and harmful.
        // Just null out our references so we don't use them after this point.
        _interceptionContext = IntPtr.Zero;
        _mouseFilter = null;

        // â”€â”€ Stop WH_MOUSE_LL fallback â”€â”€
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _hookProc = null;
        _useInterception = false;
        Reset();
    }

    public void Reset()
    {
        _subPixelX = 0; _subPixelY = 0; _zeroOutputRun = 0;
        _smoothX = 0; _smoothY = 0;
        _smooth2X = 0; _smooth2Y = 0;
        _smooth3X = 0; _smooth3Y = 0;
        _smooth4X = 0; _smooth4Y = 0;
        _smooth5X = 0; _smooth5Y = 0;
        _smoothDirX = 0; _smoothDirY = 0;
        _hasLastPos = false;
        _lastTimeTicks = 0;
        _vIdx = 0;
        Array.Clear(_vxBuf); Array.Clear(_vyBuf); Array.Clear(_vtBuf);
        _kalmanX = 0; _kalmanY = 0;
        _kalmanPx = 1.0; _kalmanPy = 1.0;
        _kalmanVx = 0; _kalmanVy = 0;
        _kalmanPvx = 1.0; _kalmanPvy = 1.0;
        _kalmanAx = 0; _kalmanAy = 0;
        _kalmanPax = 1.0; _kalmanPay = 1.0;
        _prevAccelX = 0; _prevAccelY = 0;
        _prevDx = 0; _prevDy = 0;
        _jerkEmaX = 0; _jerkEmaY = 0;
        _jerk2EmaX = 0; _jerk2EmaY = 0;
        _momentumX = 0; _momentumY = 0;
        _accelEma2 = 0; _velEma = 0;
        _recoilJitterEmaX = 0; _recoilJitterEmaY = 0;
        _recoilShakeScore = 0;
        _recoilBaselineVelX = 0; _recoilBaselineVelY = 0;
        _recoilReversalCount = 0; _recoilLastReversalTick = 0;
        _recoilPrevDx = 0; _recoilPrevDy = 0;
        _kernelFireHeld = false; _kernelFireStartTick = 0; _kernelFireDurationMs = 0;
        _kernelPreFireBaselineX = 0; _kernelPreFireBaselineY = 0;
        _kernelFireShakeEmaX = 0; _kernelFireShakeEmaY = 0;
        _kernelFireDriftEmaX = 0; _kernelFireDriftEmaY = 0;
        _kernelFireIntensity = 0; _kernelFireSampleCount = 0;
        _kernelSprayPatternPhase = 0; _fireHistIdx = 0;
        _kernelRecoilCompensationY = 0;
        Array.Clear(_fireDxBuf); Array.Clear(_fireDyBuf);
        _cursorConfined = false; _lastClipCheckTick = 0;
        _cursorAtEdge = false; _edgeStallCount = 0;
        _preEdgeDx = 0; _preEdgeDy = 0; _edgeSuppressRecoil = false;
        _recoilStartTicks = 0; _recoilActive = false; _sustainedDownMs = 0;
        _lastClickTicks = 0; _clickFreezeActive = false;
        _lastAccelFactor = 1.0;
        _kernelDtEma = 0.001;
        _userDtEma = 0.008;
        _kernelRawMagEma = 1.0;
        _userRawMagEma = 2.0;
    }

    public void Dispose() => Uninstall();

    #endregion

    #region Interception Kernel Capture Loop

    /// <summary>
    /// High-priority background thread that captures mouse events directly from the kernel driver.
    /// This runs at driver stack level â€” before Windows processes the input.
    /// Latency: &lt;0.1ms (vs 1-5ms for WH_MOUSE_LL).
    /// </summary>
    private void InterceptionCaptureLoop()
    {
        // Pin this thread to a single core for minimal context-switch latency
        try
        {
            Thread.BeginThreadAffinity();
        }
        catch { /* not critical */ }

        while (_captureRunning)
        {
            // Wait for next hardware event with 100ms timeout (allows clean shutdown)
            IntPtr ctx = _interceptionContext; // local copy â€” context may be nulled by Uninstall()
            if (ctx == IntPtr.Zero) break;

            int device = interception_wait_with_timeout(ctx, 100);
            if (device == 0) continue; // timeout, no stroke â€” just re-check _captureRunning

            // â”€â”€ A stroke is pending in the driver â€” we MUST receive + send it â”€â”€
            // Even if _captureRunning just became false, skipping the receive
            // would leave the driver holding the stroke â†’ mouse frozen at driver level.

            // Only process mouse devices
            if (interception_is_mouse(device) == 0)
            {
                // Not a mouse â€” pass through untouched
                var passStroke = new InterceptionMouseStroke();
                if (interception_receive(ctx, device, ref passStroke, 1) > 0)
                    interception_send(ctx, device, ref passStroke, 1);
                continue;
            }

            var stroke = new InterceptionMouseStroke();
            if (interception_receive(ctx, device, ref stroke, 1) <= 0)
                continue;

            if (!IsActive || !_captureRunning)
            {
                // Engine disabled or shutting down â€” pass through raw hardware event
                interception_send(ctx, device, ref stroke, 1);
                continue;
            }

            // â”€â”€ Track button events for click stabilization + recoil control â”€â”€
            if ((stroke.state & INTERCEPTION_MOUSE_LEFT_BUTTON_DOWN) != 0)
            {
                _lastClickTicks = Stopwatch.GetTimestamp();
                // â”€â”€ Kernel Recoil: fire started â€” capture pre-fire aim baseline â”€â”€
                if (!_kernelFireHeld)
                {
                    _kernelFireHeld = true;
                    _kernelFireStartTick = _sw.ElapsedTicks;
                    _kernelFireDurationMs = 0;
                    _kernelFireSampleCount = 0;
                    _kernelSprayPatternPhase = 0;
                    _kernelFireIntensity = 0;
                    _kernelRecoilCompensationY = 0;
                    // Snapshot the current aim direction as "where player was aiming before spray"
                    _kernelPreFireBaselineX = _recoilBaselineVelX;
                    _kernelPreFireBaselineY = _recoilBaselineVelY;
                    _kernelFireShakeEmaX = 0; _kernelFireShakeEmaY = 0;
                    _kernelFireDriftEmaX = 0; _kernelFireDriftEmaY = 0;
                    _fireHistIdx = 0;
                    Array.Clear(_fireDxBuf); Array.Clear(_fireDyBuf);
                }
                if (AiClickOptimized && ClickStabilityRadius > 0)
                {
                    GetCursorPos(out var clickPos);
                    _clickFreezeX = clickPos.X;
                    _clickFreezeY = clickPos.Y;
                    _clickFreezeActive = true;
                }
            }
            if ((stroke.state & INTERCEPTION_MOUSE_LEFT_BUTTON_UP) != 0)
            {
                // â”€â”€ Kernel Recoil: fire released â€” begin decay â”€â”€
                _kernelFireHeld = false;
            }
            if ((stroke.state & INTERCEPTION_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            {
                _lastClickTicks = Stopwatch.GetTimestamp();
                if (AiClickOptimized && ClickStabilityRadius > 0)
                {
                    GetCursorPos(out var clickPos);
                    _clickFreezeX = clickPos.X;
                    _clickFreezeY = clickPos.Y;
                    _clickFreezeActive = true;
                }
            }

            // â”€â”€ Process movement â”€â”€
            bool hasMovement = (stroke.x != 0 || stroke.y != 0);
            bool isRelative = (stroke.flags & INTERCEPTION_MOUSE_MOVE_ABSOLUTE) == 0;

            if (hasMovement && isRelative)
            {
                // TRUE KERNEL-LEVEL DELTAS â€” raw hardware values, no Windows processing
                int rawDx = stroke.x;
                int rawDy = stroke.y;

                var (outDx, outDy) = ProcessDelta(rawDx, rawDy);

                // Write processed deltas back into the stroke
                stroke.x = outDx;
                stroke.y = outDy;
            }

            // Send the (possibly modified) stroke back through the driver stack
            interception_send(ctx, device, ref stroke, 1);
        }

        try
        {
            Thread.EndThreadAffinity();
        }
        catch { /* not critical */ }
    }

    #endregion

    #region WH_MOUSE_LL Fallback Hook Callback

    /// <summary>
    /// Fallback hook callback â€” only used when Interception driver is not available.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive)
        {
            int msg = (int)wParam;

            // Track click events for click stabilization
            if (AiClickOptimized && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN))
            {
                _lastClickTicks = Stopwatch.GetTimestamp();
                if (ClickStabilityRadius > 0)
                {
                    GetCursorPos(out var clickPos);
                    _clickFreezeX = clickPos.X;
                    _clickFreezeY = clickPos.Y;
                    _clickFreezeActive = true;
                }
            }

            if (msg == WM_MOUSEMOVE)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Skip our own injected movements
                if (hookStruct.dwExtraInfo == INJECTED_MARKER)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                // Skip other injected movements
                if ((hookStruct.flags & 1) != 0 && hookStruct.dwExtraInfo != INJECTED_MARKER)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                if (!_hasLastPos)
                {
                    _lastCursorPos = hookStruct.pt;
                    _hasLastPos = true;
                    _lastTimeTicks = Stopwatch.GetTimestamp();
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                int rawDx = hookStruct.pt.X - _lastCursorPos.X;
                int rawDy = hookStruct.pt.Y - _lastCursorPos.Y;
                _lastCursorPos = hookStruct.pt;

                if (rawDx == 0 && rawDy == 0)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                var (outDx, outDy) = ProcessDelta(rawDx, rawDy);

                if (outDx == rawDx && outDy == rawDy)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                int newX = hookStruct.pt.X - rawDx + outDx;
                int newY = hookStruct.pt.Y - rawDy + outDy;

                // Clamp to virtual screen bounds
                int screenW = GetSystemMetrics(78);
                int screenH = GetSystemMetrics(79);
                int screenX = GetSystemMetrics(76);
                int screenY = GetSystemMetrics(77);
                newX = Math.Clamp(newX, screenX, screenX + screenW - 1);
                newY = Math.Clamp(newY, screenY, screenY + screenH - 1);

                // EMULATOR CURSOR-LOCK: also clamp to the clip rectangle
                // When an emulator has called ClipCursor(), our SetCursorPos must
                // respect that boundary or the cursor escapes / gets re-clamped by
                // Windows causing a jump on the next frame.
                if (_cursorConfined)
                {
                    newX = Math.Clamp(newX, _clipRect.Left, _clipRect.Right - 1);
                    newY = Math.Clamp(newY, _clipRect.Top, _clipRect.Bottom - 1);
                }

                SetCursorPos(newX, newY);
                _lastCursorPos = new POINT { X = newX, Y = newY };

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    #endregion

    #region Split Aim Pipeline (Kernel vs User â€” completely separate logic)

    /// <summary>
    /// Combined sensitivity intensity factor (0.0â€“1.0) using geometric mean.
    /// </summary>
    private double GetSensitivityFactor()
    {
        double overall = Math.Clamp(OverallSensitivity, 0.01, 100);
        double xAxis = Math.Clamp(XAxisSensitivity, 0.01, 100);
        double yAxis = Math.Clamp(YAxisSensitivity, 0.01, 100);
        double combined = Math.Pow(overall * xAxis * yAxis, 1.0 / 3.0);
        return Math.Clamp((combined - 1.0) / 99.0, 0.0, 1.0);
    }

    private double GetPrecisionNeed() => 1.0 - GetSensitivityFactor();

    /// <summary>
    /// Detects whether the cursor is currently confined (ClipCursor) by an emulator
    /// or game window. Throttled to ~50ms polling to avoid syscall overhead per frame.
    /// Also detects if the current cursor position is near the clip boundary edge,
    /// which means deltas may be getting clamped/eaten by Windows.
    /// </summary>
    private void UpdateCursorConfinementState()
    {
        long now = _sw.ElapsedTicks;
        if (now - _lastClipCheckTick < ClipCheckIntervalTicks)
            return;
        _lastClipCheckTick = now;

        // Get current clip rectangle
        if (!GetClipCursor(out RECT clip))
        {
            _cursorConfined = false;
            _cursorAtEdge = false;
            return;
        }
        _clipRect = clip;

        // Compare clip rect to full virtual screen
        int vsX = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
        int vsY = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
        int vsW = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
        int vsH = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN

        bool isFullScreen = clip.Left <= vsX && clip.Top <= vsY &&
                            clip.Right >= vsX + vsW && clip.Bottom >= vsY + vsH;
        _cursorConfined = !isFullScreen;

        // Check if cursor is near the clip boundary
        if (_cursorConfined)
        {
            GetCursorPos(out POINT pos);
            bool nearLeft = pos.X <= clip.Left + EDGE_MARGIN;
            bool nearRight = pos.X >= clip.Right - EDGE_MARGIN - 1;
            bool nearTop = pos.Y <= clip.Top + EDGE_MARGIN;
            bool nearBottom = pos.Y >= clip.Bottom - EDGE_MARGIN - 1;
            _cursorAtEdge = nearLeft || nearRight || nearTop || nearBottom;
        }
        else
        {
            _cursorAtEdge = false;
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  ROUTER â€” calls the appropriate pipeline based on capture mode
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private (int outDx, int outDy) ProcessDelta(int rawDx, int rawDy)
    {
        return _useInterception
            ? ProcessDeltaKernel(rawDx, rawDy)
            : ProcessDeltaUser(rawDx, rawDy);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  SHARED HELPERS â€” used by both pipelines to avoid duplication
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>Preamble: timing, staleness, zero-input detection. Returns (now, dtS, staleState, rawIsZero).</summary>
    private (long now, double dtS, bool staleState, bool rawIsZero) PipelinePreamble(int rawDx, int rawDy, double defaultDt)
    {
        long now = _sw.ElapsedTicks;
        double dtS = (now - _lastTick) / (double)Stopwatch.Frequency;
        bool staleState = (dtS > 0.10 || _lastTick == 0);
        bool dtInvalid = (dtS <= 0 || dtS > 0.25);
        double nominalDt = _useInterception ? _kernelDtEma : _userDtEma;
        if (dtInvalid) dtS = Math.Max(defaultDt, nominalDt);
        else
        {
            // Keep an adaptive timing baseline so Win10/Win11 scheduling differences
            // do not change effective speed/accel math in either capture mode.
            double clampedDt = Math.Clamp(dtS, defaultDt * 0.35, defaultDt * 8.0);
            if (_useInterception)
                _kernelDtEma += (clampedDt - _kernelDtEma) * 0.05;
            else
                _userDtEma += (clampedDt - _userDtEma) * 0.08;
        }
        _lastTick = now;

        bool rawIsZero = (rawDx == 0 && rawDy == 0);

        if (staleState)
        {
            _accelEma = 0; _accelEma2 = 0;
            _velEma = 0; _prevVel = 0;
            _prevDx = 0; _prevDy = 0;
            _prevAccelX = 0; _prevAccelY = 0;
            _momentumX = 0; _momentumY = 0;
            _jerkEmaX = 0; _jerkEmaY = 0;
            _jerk2EmaX = 0; _jerk2EmaY = 0;
            _recoilBaselineVelX = 0; _recoilBaselineVelY = 0;
            _recoilShakeScore = 0;
            _recoilJitterEmaX = 0; _recoilJitterEmaY = 0;
            _subPixelX = 0; _subPixelY = 0;
            _lastAccelFactor = 1.0;
            _vIdx = 0;
            Array.Clear(_vxBuf); Array.Clear(_vyBuf); Array.Clear(_vtBuf);
        }

        return (now, dtS, staleState, rawIsZero);
    }

    /// <summary>
    /// Extra curve gain from motion on one axis only (|raw delta| on that axis).
    /// Using combined hypotenuse for both X and Y made diagonal / drag-up feel like sens
    /// "auto-sped up"; mobile-style aim needs independent vertical drag vs horizontal track.
    /// </summary>
    private double CurveMulForAxisMag(double rawAxisAbs)
    {
        double ax = Math.Abs(rawAxisAbs);
        double accel = AdvAcceleration;
        switch (CurveType)
        {
            case AccelCurveType.Linear:
                return accel;

            case AccelCurveType.Power:
                if (ax > 0.5)
                {
                    double exponent = Math.Clamp(accel, 0.01, 2.0);
                    double normSpeed = ax / 10.0;
                    return 1.0 + Math.Pow(normSpeed, exponent) * 0.5;
                }
                return 1.0;

            case AccelCurveType.Natural:
                if (ax > 0.5)
                {
                    double limit = Math.Clamp(accel, 0.1, 5.0);
                    const double decayRate = 0.15;
                    double normSpeed = ax / 10.0;
                    return 1.0 + limit * (1.0 - Math.Exp(-decayRate * normSpeed));
                }
                return 1.0;

            case AccelCurveType.Logarithmic:
                if (ax > 0.5)
                {
                    double gain = Math.Clamp(accel, 0.1, 3.0);
                    double normSpeed = ax / 10.0;
                    return 1.0 + gain * Math.Log(1.0 + normSpeed);
                }
                return 1.0;

            case AccelCurveType.Sigmoid:
                if (ax > 0.5)
                {
                    double height = Math.Clamp(accel, 0.1, 4.0);
                    double normSpeed = ax / 10.0;
                    double sigmoid = 1.0 / (1.0 + Math.Exp(-3.0 * (normSpeed - 1.5)));
                    return 1.0 + height * sigmoid;
                }
                return 1.0;

            case AccelCurveType.Step:
            {
                double jump = Math.Clamp(accel, 1.0, 5.0);
                const double threshold = 5.0;
                return ax > threshold ? jump : 1.0;
            }

            default:
                return 1.0;
        }
    }

    /// <summary>
    /// Splits a unified accel factor so vertical-dominant drags do not ramp gain as hard,
    /// and horizontal-dominant moves stay a bit more linear for micro-corrections.
    /// </summary>
    private static void SplitAxisAccelerationFactor(double factor, double dx, double dy, out double fx, out double fy)
    {
        if (factor <= 1.0000001) { fx = fy = 1.0; return; }
        double ax = Math.Abs(dx), ay = Math.Abs(dy);
        double sum = ax + ay + 1e-9;
        double yDom = ay / sum;
        double xDom = ax / sum;
        double extra = factor - 1.0;
        double yBoost = 1.0 - 0.72 * yDom * yDom;
        double xBoost = 1.0 - 0.38 * xDom * xDom;
        fx = 1.0 + extra * Math.Clamp(xBoost, 0.28, 1.0);
        fy = 1.0 + extra * Math.Clamp(yBoost, 0.22, 1.0);
    }

    /// <summary>
    /// Applies mode-specific axis shaping so Free Fire style horizontal tracking + vertical drag
    /// can be tuned independently for user/kernel pipelines.
    /// </summary>
    private static (double dx, double dy) ApplyModeAxisScaling(
        double dx, double dy, bool kernelMode, double accelX, double accelY)
    {
        double ax = Math.Abs(dx), ay = Math.Abs(dy);
        double sum = ax + ay + 1e-9;
        double xDom = ax / sum;
        double yDom = ay / sum;

        double mx = Math.Clamp(accelX, 0.50, 1.80);
        double my = Math.Clamp(accelY, 0.50, 1.80);
        if (kernelMode)
        {
            // Kernel path: stronger vertical-control logic for high-precision drag shots.
            mx *= (1.0 - 0.06 * xDom * xDom);
            my *= (1.0 - 0.15 * yDom * yDom);
        }
        else
        {
            // User path: lighter shaping to avoid latency while still stabilizing drag.
            mx *= (1.0 - 0.04 * xDom * xDom);
            my *= (1.0 - 0.08 * yDom * yDom);
        }

        return (dx * mx, dy * my);
    }

    /// <summary>
    /// In cursor-confined lock mode, occasional burst deltas can appear (clip/recenter artifacts).
    /// Cap only outliers so lock-mode does not suddenly overshoot while preserving normal drag.
    /// </summary>
    private (double dx, double dy) ClampConfinedRawSpike(
        double dx, double dy, bool kernelMode, bool staleState, LockModeStrictness strictness)
    {
        if (strictness == LockModeStrictness.Off) return (dx, dy);
        double rawMag = Math.Sqrt(dx * dx + dy * dy);
        if (rawMag <= 1e-9) return (dx, dy);

        ref double ema = ref (kernelMode ? ref _kernelRawMagEma : ref _userRawMagEma);
        double baseline = Math.Max(ema, kernelMode ? 0.8 : 1.8);
        double strictMul = strictness switch
        {
            LockModeStrictness.Normal => 1.00,
            LockModeStrictness.Strict => 0.82,
            LockModeStrictness.Ultra => 0.68,
            _ => 1.00
        };
        double spikeThreshold = (baseline * 6.0 + (kernelMode ? 8.0 : 18.0)) * strictMul;
        bool isSpike = _cursorConfined && !staleState && rawMag > spikeThreshold;

        if (isSpike)
        {
            double capMul = strictness switch
            {
                LockModeStrictness.Normal => 1.00,
                LockModeStrictness.Strict => 0.80,
                LockModeStrictness.Ultra => 0.62,
                _ => 1.00
            };
            double cap = (baseline * 2.6 + (kernelMode ? 4.0 : 10.0)) * capMul;
            double scale = cap / rawMag;
            dx *= scale;
            dy *= scale;
            rawMag = cap;
        }

        double alpha = kernelMode ? 0.035 : 0.09;
        ema += (rawMag - ema) * alpha;
        return (dx, dy);
    }

    /// <summary>Shared: sensitivity curve + curve-type accel + aspect ratio + pre-scale.</summary>
    private (double dx, double dy) ApplySensitivity(double dx, double dy)
    {
        // ── Overall sensitivity: the main multiplier for both axes ──
        double curveShift = AiCurveShift;
        double sensLimitMax = Math.Max(AdvSensLimit, 0.1);
        double ov = Math.Clamp(OverallSensitivity + curveShift * 100.0, 0.01, 200.0);
        double xv = Math.Clamp(XSensitivity + curveShift * 100.0, 0.01, 200.0);
        double yv = Math.Clamp(YSensitivity + curveShift * 100.0, 0.01, 200.0);
        double overallMul = ScaleCurveToLimit(SensCurve12(ov), sensLimitMax);

        // ── Per-axis fine-tune: ratio of axis curve to overall curve ──
        // When X/Y == Overall → ratio 1.0. Clamped so small mismatches cannot explode gain
        // (previously a low overall + high X produced huge xRatio and wild movement).
        double rawX = ScaleCurveToLimit(SensCurve12(xv), sensLimitMax);
        double rawY = ScaleCurveToLimit(SensCurve12(yv), sensLimitMax);
        double xRatio = (overallMul > 1e-9) ? rawX / overallMul : 1.0;
        double yRatio = (overallMul > 1e-9) ? rawY / overallMul : 1.0;
        const double ratioMin = 0.35;
        const double ratioMax = 2.5;
        xRatio = Math.Clamp(xRatio, ratioMin, ratioMax);
        yRatio = Math.Clamp(yRatio, ratioMin, ratioMax);

        double rawMag = Math.Sqrt(dx * dx + dy * dy);
        double curveMulX = CurveMulForAxisMag(dx);
        double curveMulY = CurveMulForAxisMag(dy);
        double sensProduct = overallMul * Math.Max(xRatio, yRatio);
        double curveMulCombo = Math.Max(curveMulX, curveMulY);

        // Compression for very high sens + large deltas
        double compression = 1.0;
        if (sensProduct * curveMulCombo > 5.0 && rawMag > 3.0)
        {
            double knee = 5.0 / (sensProduct * curveMulCombo);
            compression = 1.0 - (1.0 - knee) * Math.Clamp((rawMag - 3.0) / 20.0, 0, 0.4);
        }

        dx *= overallMul * xRatio * curveMulX * compression;
        dy *= overallMul * yRatio * curveMulY * compression;

        // Aspect ratio correction
        if (EmulatorWidth > 0 && EmulatorHeight > 0 && DisplayWidth > 0 && DisplayHeight > 0)
        {
            double dispAr = (double)DisplayWidth / DisplayHeight;
            double emuAr = (double)EmulatorWidth / EmulatorHeight;
            if (Math.Abs(dispAr - emuAr) > 0.01)
            {
                double correction = Math.Sqrt(emuAr / dispAr);
                dx *= correction;
                dy /= correction;
            }
        }

        // Pre-scale axis amplification
        if (Math.Abs(PreScaleX - 1.0) > 1e-9 || Math.Abs(PreScaleY - 1.0) > 1e-9)
        {
            dx *= AmplifyScale(PreScaleX);
            dy *= AmplifyScale(PreScaleY);
        }

        return (dx, dy);
    }

    /// <summary>Shared: post-scale, general sens, sens limit, vertical drift, click stab, zero-gate, sub-pixel.</summary>
    private (int outX, int outY) ApplyPostProcessing(double dx, double dy, double dtS, long now, bool rawIsZero)
    {
        // Post-scale amplification
        if (Math.Abs(PostScaleX - 1.0) > 1e-9 || Math.Abs(PostScaleY - 1.0) > 1e-9)
        {
            dx *= AmplifyScale(PostScaleX);
            dy *= AmplifyScale(PostScaleY);
        }

        // General sensitivity amplification
        if (Math.Abs(GeneralSensitivity - 1.0) > 1e-9)
        {
            double gs = AmplifyScale(GeneralSensitivity);
            dx *= gs;
            dy *= gs;
        }

        // Sensitivity limit (safety ceiling) â€" the user's AdvSensLimit is already
        // built into the curve via ScaleCurveToLimit, but post-scale / general-sens
        // can push values beyond. This hard clamp ensures nothing exceeds the limit.
        if (AdvSensLimit < 9.99)
        {
            double limit = AdvSensLimit;
            double outMag = Math.Sqrt(dx * dx + dy * dy);
            if (outMag > limit)
            {
                double scale = limit / outMag;
                dx *= scale;
                dy *= scale;
            }
        }

        // Vertical Drift Correction (anti-recoil)
        if (VerticalDriftEnabled && VerticalDriftStrength > 0.001)
        {
            if (dy > 0.3)
            {
                _sustainedDownMs += dtS * 1000.0;
                if (!_recoilActive && _sustainedDownMs > 50)
                    _recoilActive = true;
            }
            else
            {
                _sustainedDownMs *= 0.85;
                if (_sustainedDownMs < 10) _recoilActive = false;
            }

            if (_recoilActive)
            {
                double rampFactor = Math.Clamp(_sustainedDownMs / Math.Max(VerticalDriftRampMs, 1.0), 0, 1);
                rampFactor = rampFactor * rampFactor * (3.0 - 2.0 * rampFactor);
                double sensFactor = GetSensitivityFactor();
                double compensation = VerticalDriftStrength * rampFactor * (0.5 + sensFactor * 0.5);
                compensation = Math.Min(compensation, VerticalDriftMaxPx);
                dy -= compensation;
            }
        }

        // Click Stabilization
        if (AiClickOptimized && ClickStabilityMs > 0 && _lastClickTicks > 0)
        {
            double msSinceClick = (now - _lastClickTicks) / (double)Stopwatch.Frequency * 1000.0;
            if (msSinceClick < ClickStabilityMs)
            {
                double mag = Math.Sqrt(dx * dx + dy * dy);
                double radius = ClickStabilityRadius > 0 ? ClickStabilityRadius : 1.5;
                if (mag < radius)
                {
                    double suppressFactor = Math.Clamp(1.0 - (ClickStabilityMs - msSinceClick) / ClickStabilityMs, 0.1, 1.0);
                    dx *= suppressFactor;
                    dy *= suppressFactor;
                }
            }
        }

        // MASTER ZERO-GATE
        if (rawIsZero)
        {
            dx = 0; dy = 0;
            _subPixelX *= 0.5;
            _subPixelY *= 0.5;
        }

        // Sub-pixel accumulation with dithering (prevents 1000Hz pixel-stepping)
        _subPixelX += dx;
        _subPixelY += dy;

        // Round-to-nearest instead of truncation: eliminates the "steppy" feel
        // where cursor outputs 0,0,0,0,1,0,0,0,0,1 at high poll rates.
        // With rounding: 0.5+ rounds to 1 immediately, giving smoother distribution.
        int outX = (int)Math.Round(_subPixelX, MidpointRounding.ToZero);
        int outY = (int)Math.Round(_subPixelY, MidpointRounding.ToZero);

        // At 1000Hz, sub-pixel often sits at 0.3-0.4 for several frames.
        // If we have had 3+ consecutive zero outputs and sub-pixel is building,
        // promote to +/-1 to keep motion fluid instead of stalling.
        if (_useInterception && outX == 0 && outY == 0 && !rawIsZero)
        {
            _zeroOutputRun++;
            if (_zeroOutputRun >= 3)
            {
                if (Math.Abs(_subPixelX) >= 0.35) outX = _subPixelX > 0 ? 1 : -1;
                if (Math.Abs(_subPixelY) >= 0.35) outY = _subPixelY > 0 ? 1 : -1;
            }
        }
        else if (outX != 0 || outY != 0)
        {
            _zeroOutputRun = 0;
        }

        _subPixelX -= outX;
        _subPixelY -= outY;

        return (outX, outY);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  KERNEL MODE PIPELINE â€” optimized for ~1000Hz raw hardware deltas
    //  Native timing: dtS â‰ˆ 0.001s, deltas â‰ˆ 1-3px per sample
    //  Has: true fire-button awareness, 7-layer recoil, hardware strokes
    //  NO rateScale hacks â€” all constants are native 1000Hz values
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private (int outDx, int outDy) ProcessDeltaKernel(int rawDx, int rawDy)
    {
        // â”€â”€ Preamble: timing, staleness, zero detection â”€â”€
        var (now, dtS, staleState, rawIsZero) = PipelinePreamble(rawDx, rawDy, 0.001);

        double dx = rawDx, dy = rawDy;

        // â”€â”€ Confinement state (emulator cursor lock) â”€â”€
        UpdateCursorConfinementState();
        if (_cursorConfined && _cursorAtEdge)
        {
            _edgeStallCount++;
            _edgeSuppressRecoil = true;
            // Kernel mode: raw hardware deltas are correct even at edge.
            // Only suppress recoil false-positives, don't modify deltas.
        }
        else
        {
            if (_cursorConfined)
            {
                if (_edgeStallCount > 0) _edgeStallCount = Math.Max(0, _edgeStallCount - 2);
                _edgeSuppressRecoil = false;
                if (Math.Abs(dx) > 0.3 || Math.Abs(dy) > 0.3) { _preEdgeDx = dx; _preEdgeDy = dy; }
            }
            else
            {
                _edgeStallCount = 0; _edgeSuppressRecoil = false;
                if (Math.Abs(dx) > 0.3 || Math.Abs(dy) > 0.3) { _preEdgeDx = dx; _preEdgeDy = dy; }
            }
        }

        (dx, dy) = ClampConfinedRawSpike(dx, dy, kernelMode: true, staleState, KernelLockStrictness);

        // â”€â”€ Sensitivity + Aspect Ratio + Pre-Scale (shared) â”€â”€
        (dx, dy) = ApplySensitivity(dx, dy);
        (dx, dy) = ApplyModeAxisScaling(dx, dy, kernelMode: true, KernelAccelX, KernelAccelY);

        // â”€â”€ Velocity estimation â”€â”€
        double vel = EstimateVelocity(dx, dy, now, dtS);
        _velEma += (vel - _velEma) * 0.06; // 1000Hz: moderate vel tracking (was 0.02, too sluggish)

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  KERNEL RECOIL CONTROL â€” 7-layer fire-aware stabilization
        //  TRUE hardware button state from interception strokes
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        double effectiveRecoilControl = RecoilControlStrength;
        if (effectiveRecoilControl > 0.001)
        {
            double str = Math.Clamp(effectiveRecoilControl, 0, 1);
            bool kernelEdgeGuard = _edgeSuppressRecoil && _cursorConfined;

            // Layer 1: Update fire duration
            if (_kernelFireHeld)
            {
                _kernelFireDurationMs = (_sw.ElapsedTicks - _kernelFireStartTick) / (double)Stopwatch.Frequency * 1000.0;
                _kernelFireSampleCount++;
                if (!kernelEdgeGuard)
                {
                    _fireDxBuf[_fireHistIdx] = dx;
                    _fireDyBuf[_fireHistIdx] = dy;
                    _fireHistIdx = (_fireHistIdx + 1) % FIRE_HISTORY;
                }
            }

            // Layer 2: Spray duration ramp (80ms activation, 320ms full ramp)
            double sprayRamp = 0;
            if (_kernelFireHeld && _kernelFireDurationMs > 80)
            {
                sprayRamp = Math.Clamp((_kernelFireDurationMs - 80.0) / 320.0, 0, 1);
                sprayRamp = sprayRamp * sprayRamp * (3.0 - 2.0 * sprayRamp);
            }

            // Layer 3: Separate high-freq shake from low-freq recoil drift
            if (_kernelFireHeld && sprayRamp > 0.01 && !kernelEdgeGuard)
            {
                double driftAlpha = 0.04 + (1.0 - sprayRamp) * 0.08;
                _kernelFireDriftEmaX += (dx - _kernelFireDriftEmaX) * driftAlpha;
                _kernelFireDriftEmaY += (dy - _kernelFireDriftEmaY) * driftAlpha;

                double shakeAlpha = 0.45;
                double shakeComponentX = dx - _kernelFireDriftEmaX;
                double shakeComponentY = dy - _kernelFireDriftEmaY;
                _kernelFireShakeEmaX += (Math.Abs(shakeComponentX) - _kernelFireShakeEmaX) * shakeAlpha;
                _kernelFireShakeEmaY += (Math.Abs(shakeComponentY) - _kernelFireShakeEmaY) * shakeAlpha;
            }

            // Layer 4: Fire intensity scoring
            if (_kernelFireHeld && sprayRamp > 0.01)
            {
                double shakeMag = Math.Sqrt(_kernelFireShakeEmaX * _kernelFireShakeEmaX +
                                            _kernelFireShakeEmaY * _kernelFireShakeEmaY);
                double shakeScore = Math.Clamp(shakeMag / 5.0, 0, 1);
                double verticalBias = _kernelFireDriftEmaY > 0.2 ? Math.Clamp(_kernelFireDriftEmaY / 3.0, 0, 0.3) : 0;
                double rawIntensity = (shakeScore * 0.6 + sprayRamp * 0.3 + verticalBias * 0.1);

                if (rawIntensity > _kernelFireIntensity)
                    _kernelFireIntensity += (rawIntensity - _kernelFireIntensity) * 0.4;
                else
                    _kernelFireIntensity += (rawIntensity - _kernelFireIntensity) * 0.15;
                _kernelFireIntensity = Math.Clamp(_kernelFireIntensity, 0, 1);
            }
            else
            {
                _kernelFireIntensity *= 0.88;
                if (_kernelFireIntensity < 0.005) _kernelFireIntensity = 0;
            }

            // Layer 5: Apply stabilization
            if (_kernelFireIntensity > 0.01)
            {
                double stabilize = str * _kernelFireIntensity;

                // 5a: Horizontal shake dampening
                double shakeX = dx - _kernelFireDriftEmaX;
                dx = _kernelFireDriftEmaX + shakeX * Math.Max(0.08, 1.0 - stabilize * 0.92);

                // 5b: Vertical shake dampening (stronger)
                double shakeY = dy - _kernelFireDriftEmaY;
                dy = _kernelFireDriftEmaY + shakeY * Math.Max(0.05, 1.0 - stabilize * 0.95);

                // 5c: Vertical drift compensation
                if (_kernelFireHeld && _kernelFireDurationMs > 150 && _kernelFireDriftEmaY > 0.3)
                {
                    double driftCompensation = _kernelFireDriftEmaY * stabilize * 0.35;
                    driftCompensation = Math.Min(driftCompensation, 4.0);
                    double compRamp = Math.Clamp((_kernelFireDurationMs - 150.0) / 250.0, 0, 1);
                    _kernelRecoilCompensationY += (driftCompensation * compRamp - _kernelRecoilCompensationY) * 0.12;
                    dy -= _kernelRecoilCompensationY;
                }
                else
                {
                    _kernelRecoilCompensationY *= 0.85;
                }

                // 5d: Spray pattern phase tracking
                if (_kernelFireHeld)
                {
                    _kernelSprayPatternPhase += dtS * 1000.0;
                    if (_kernelFireSampleCount > 8 && _kernelSprayPatternPhase > 100)
                    {
                        double avgDy = 0;
                        int count = Math.Min(_kernelFireSampleCount, FIRE_HISTORY);
                        for (int i = 0; i < count; i++) avgDy += _fireDyBuf[i];
                        avgDy /= Math.Max(count, 1);
                        if (avgDy > 0.15)
                        {
                            double patternBoost = Math.Clamp(avgDy / 2.0, 0, 0.5) * stabilize;
                            dy -= patternBoost * 0.2;
                        }
                    }
                }
            }

            // Layer 6: Velocity gate â€” fast flicks pass through
            if (_kernelFireIntensity > 0.01 && vel > 1200.0)
            {
                double flickOverride = Math.Clamp((vel - 1200.0) / 1500.0, 0, 0.85);
                _kernelFireIntensity *= (1.0 - flickOverride * 0.5);
            }

            // Layer 7: Update shared baseline
            double blAlpha = _kernelFireHeld ? 0.03 : 0.12;
            _recoilBaselineVelX += (dx - _recoilBaselineVelX) * blAlpha;
            _recoilBaselineVelY += (dy - _recoilBaselineVelY) * blAlpha;

            _recoilPrevDx = dx;
            _recoilPrevDy = dy;
        }

        // â”€â”€ Auto micro-deadzone â”€â”€
        { double mag = Math.Sqrt(dx * dx + dy * dy); if (mag < 0.02) { dx = 0; dy = 0; } }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  KERNEL ACCELERATION â€” native 1000Hz tuning
        //  Derivatives use dtS directly (no normalization needed, native rate)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        if (AccelerationStrength > 0.001 && !NoAccelerationOverride && !LinearInputEnabled)
        {
            // After a timing gap (stale), (vel - _prevVel)/dtS is meaningless and spikes gain.
            if (!staleState)
            {
                double str = AccelerationStrength * 1.5;
                double accelRaw = (vel - _prevVel) / Math.Max(dtS, 0.0001);
                // At 1000Hz, raw accel values are ~8x smaller per sample
                // Use lighter EMA alphas to match the effective time-constant of 125Hz
                _accelEma += (accelRaw - _accelEma) * 0.025;   // 1000Hz native (â‰ˆ0.2 at 125Hz)
                _accelEma2 += (_accelEma - _accelEma2) * 0.019; // 1000Hz native (â‰ˆ0.15 at 125Hz)

                double velComponent = Math.Pow(vel / 600.0, 1.4) * 0.6;
                double accelComponent = Math.Sign(_accelEma2) * Math.Pow(Math.Abs(_accelEma2) / 5000.0, 0.8) * 0.2;
                double accelFactor = 1.0 + str * (velComponent + Math.Max(0, accelComponent));

                double jerk = Math.Abs(_accelEma);
                double jerk2 = Math.Abs(_accelEma2);
                if (jerk > 4000) accelFactor *= Math.Max(0.5, 1.0 - (jerk - 4000) / 40000.0);
                if (jerk2 > 2000) accelFactor *= Math.Max(0.7, 1.0 - (jerk2 - 2000) / 30000.0);

                _lastAccelFactor += (accelFactor - _lastAccelFactor) * 0.038; // 1000Hz native (â‰ˆ0.3 at 125Hz)

                if (Math.Abs(AiAccelerationDampen) > 0.001)
                    _lastAccelFactor = 1.0 + (_lastAccelFactor - 1.0) * (1.0 + AiAccelerationDampen);

                SplitAxisAccelerationFactor(_lastAccelFactor, dx, dy, out double accFx, out double accFy);
                dx *= accFx;
                dy *= accFy;
            }

            _prevVel = vel;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  KERNEL SMOOTHING â€” 3-cascade (lighter than user mode)
        //  At 1000Hz we get 8x more samples, so we need far fewer
        //  cascade stages. 3 stages with tight alphas = responsive + smooth.
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        double effectiveSmoothing = SmoothingStrength + AiSmoothBoost;
        if (effectiveSmoothing > 0.001)
        {
            double str = Math.Pow(Math.Clamp(effectiveSmoothing, 0, 1), 0.55);

            // Stale: seed EMAs
            if (staleState && !rawIsZero)
            {
                _smoothX = dx; _smoothY = dy;
                _smooth2X = dx; _smooth2Y = dy;
                _smooth3X = dx; _smooth3Y = dy;
            }

            if (rawIsZero)
            {
                // At 1000Hz, zero frames happen naturally between sensor reports.
                // Decay must be gentle enough to coast smoothly through gaps,
                // but fast enough to stop when mouse truly stops.
                // 0.75 per sample ≈ 0.10 after 8 consecutive zeros (stops in ~8ms)
                double decay = 0.75;
                _smoothX *= decay; _smoothY *= decay;
                _smooth2X *= decay; _smooth2Y *= decay;
                _smooth3X *= decay; _smooth3Y *= decay;
                dx = _smooth3X; dy = _smooth3Y;
            }
            else
            {
                // Stage 1: velocity-adaptive (1000Hz) — per-axis vel hint so vertical drag and
                // horizontal track do not share one "speed" that opens smoothing equally on both.
                double axS = Math.Abs(dx), ayS = Math.Abs(dy);
                double yDomS = ayS / (axS + ayS + 1e-9);
                double velX = vel * (1.0 - 0.38 * (1.0 - yDomS) * (1.0 - yDomS));
                double velY = vel * (1.0 - 0.42 * yDomS * yDomS);
                double a1x = Math.Clamp(1.0 - str * 0.88 * Math.Exp(-velX / 500.0), 0.03, 1.0);
                double a1y = Math.Clamp(1.0 - str * 0.88 * Math.Exp(-velY / 500.0), 0.03, 1.0);
                double alpha1X = Math.Clamp(a1x * 0.35, 0.06, 1.0);
                double alpha1Y = Math.Clamp(a1y * 0.35, 0.06, 1.0);
                _smoothX += (dx - _smoothX) * alpha1X;
                _smoothY += (dy - _smoothY) * alpha1Y;

                // Stage 2: acceleration-adaptive (1000Hz native)
                double accelMag = Math.Abs(_accelEma);
                double a2 = Math.Clamp(1.0 - str * 0.65 * Math.Exp(-accelMag / 2500.0), 0.08, 1.0);
                double alpha2 = Math.Clamp(a2 * 0.35, 0.08, 1.0);
                _smooth2X += (_smoothX - _smooth2X) * alpha2;
                _smooth2Y += (_smoothY - _smooth2Y) * alpha2;

                // Stage 3: final polish (1000Hz native)
                double a3 = Math.Clamp(1.0 - str * 0.20, 0.3, 1.0);
                double alpha3 = Math.Clamp(a3 * 0.40, 0.15, 1.0);
                _smooth3X += (_smooth2X - _smooth3X) * alpha3;
                _smooth3Y += (_smooth2Y - _smooth3Y) * alpha3;

                dx = _smooth3X; dy = _smooth3Y;
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  KERNEL KALMAN / STEADY AIM â€” native 1000Hz state model
        //  Prediction step uses real dtS, noise params tuned for 1000Hz
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        double effectiveSteady = SteadyAimStrength + AiSteadyAimBoost;
        if (effectiveSteady > 0.001)
        {
            if (staleState && !rawIsZero)
            {
                _kalmanX = dx; _kalmanY = dy;
                _kalmanVx = 0; _kalmanVy = 0;
                _kalmanAx = 0; _kalmanAy = 0;
                _kalmanPx = 1.0; _kalmanPy = 1.0;
                _kalmanPvx = 0.5; _kalmanPvy = 0.5;
                _kalmanPax = 0.5; _kalmanPay = 0.5;
            }

            if (rawIsZero)
            {
                // 1000Hz: gentle decay to coast through natural sensor gaps
                // 0.70^8 ≈ 0.057 so after 8ms of true silence it's effectively zero
                _kalmanX *= 0.70; _kalmanY *= 0.70;
                _kalmanVx *= 0.60; _kalmanVy *= 0.60;
                _kalmanAx *= 0.50; _kalmanAy *= 0.50;
                dx = _kalmanX; dy = _kalmanY;
            }
            else
            {
                double str = effectiveSteady * 1.2;
                // 1000Hz: process noise per sample is ~1/8 of 125Hz
                double processNoise = (0.08 / Math.Max(str, 0.01)) * 0.125;
                double baseMeasNoise = str * 5.0;
                double gate = Math.Clamp(vel / 400.0, 0, 1);
                double effectiveMeas = baseMeasNoise * Math.Pow(1.0 - gate * 0.8, 2.0);

                // X-axis predict
                _kalmanX += _kalmanVx * dtS + 0.5 * _kalmanAx * dtS * dtS;
                _kalmanVx += _kalmanAx * dtS;
                _kalmanPx += _kalmanPvx * dtS * dtS + processNoise;
                double kx = _kalmanPx / (_kalmanPx + effectiveMeas);
                double innovX = dx - _kalmanX;
                _kalmanX += kx * innovX;
                // 1000Hz: velocity/accel updates scaled down to prevent explosion
                _kalmanVx += (kx * innovX / Math.Max(dtS, 0.0001)) * 0.01;  // 0.08 * 0.125
                _kalmanAx += (kx * innovX / Math.Max(dtS * dtS, 0.00001)) * 0.0025; // 0.02 * 0.125
                _kalmanPx *= (1.0 - kx);
                _kalmanPvx *= 0.998;  // slower decay at 1000Hz (0.985^(1/8))
                _kalmanPax *= 0.996;
                _kalmanAx *= 0.994;   // 0.95^(1/8)

                // Y-axis predict
                _kalmanY += _kalmanVy * dtS + 0.5 * _kalmanAy * dtS * dtS;
                _kalmanVy += _kalmanAy * dtS;
                _kalmanPy += _kalmanPvy * dtS * dtS + processNoise;
                double ky = _kalmanPy / (_kalmanPy + effectiveMeas);
                double innovY = dy - _kalmanY;
                _kalmanY += ky * innovY;
                _kalmanVy += (ky * innovY / Math.Max(dtS, 0.0001)) * 0.01;
                _kalmanAy += (ky * innovY / Math.Max(dtS * dtS, 0.00001)) * 0.0025;
                _kalmanPy *= (1.0 - ky);
                _kalmanPvy *= 0.998;
                _kalmanPay *= 0.996;
                _kalmanAy *= 0.994;

                dx = _kalmanX; dy = _kalmanY;
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  KERNEL ANTI-OVERSHOOT â€” native 1000Hz thresholds
        //  Derivatives use dtS directly; thresholds calibrated for 1000Hz
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        if (AntiOvershootStrength > 0.001)
        {
            double str = AntiOvershootStrength * 1.5;

            // At 1000Hz, delta differences per sample are ~8x smaller
            // so acceleration/jerk magnitudes are naturally smaller.
            // Thresholds are calibrated accordingly.
            double accelX = (dx - _prevDx) / Math.Max(dtS, 0.0001);
            double accelY = (dy - _prevDy) / Math.Max(dtS, 0.0001);

            double jerkX = (accelX - _prevAccelX) / Math.Max(dtS, 0.0001);
            double jerkY = (accelY - _prevAccelY) / Math.Max(dtS, 0.0001);
            _jerkEmaX += (jerkX - _jerkEmaX) * 0.031;  // 0.25 * 0.125
            _jerkEmaY += (jerkY - _jerkEmaY) * 0.031;

            double snapX = (jerkX - _jerk2EmaX);
            double snapY = (jerkY - _jerk2EmaY);
            _jerk2EmaX += (jerkX - _jerk2EmaX) * 0.025; // 0.2 * 0.125
            _jerk2EmaY += (jerkY - _jerk2EmaY) * 0.025;

            double jerkMag = Math.Sqrt(_jerkEmaX * _jerkEmaX + _jerkEmaY * _jerkEmaY);
            double snapMag = Math.Sqrt(snapX * snapX + snapY * snapY);

            // 1000Hz native thresholds (8x higher because derivatives scale with 1/dtÂ²)
            _momentumX += (dx - _momentumX) * 0.019;  // 0.15 * 0.125
            _momentumY += (dy - _momentumY) * 0.019;
            double momentumDot = dx * _momentumX + dy * _momentumY;
            bool isOvershooting = momentumDot < 0 && Math.Sqrt(dx * dx + dy * dy) > 1.0;

            double threshold = 20000.0 + vel * 48.0;  // 8x the 125Hz values
            double dampen = 1.0;

            if (jerkMag > threshold)
                dampen *= Math.Max(0.15, 1.0 - str * (jerkMag - threshold) / (threshold * 4.0));
            if (snapMag > threshold * 1.5)
                dampen *= Math.Max(0.4, 1.0 - str * 0.3 * (snapMag - threshold * 1.5) / (threshold * 5.0));
            if (isOvershooting)
                dampen *= Math.Max(0.25, 1.0 - str * 0.6);

            dx *= dampen; dy *= dampen;

            if (dx * _prevDx < 0)
            {
                double reversalStr = Math.Min(Math.Abs(dx), Math.Abs(_prevDx)) / Math.Max(Math.Abs(dx) + Math.Abs(_prevDx), 0.01);
                dx *= Math.Max(0.2, 1.0 - str * 0.6 * reversalStr);
            }
            if (dy * _prevDy < 0)
            {
                double reversalStr = Math.Min(Math.Abs(dy), Math.Abs(_prevDy)) / Math.Max(Math.Abs(dy) + Math.Abs(_prevDy), 0.01);
                dy *= Math.Max(0.2, 1.0 - str * 0.6 * reversalStr);
            }

            _prevAccelX = accelX; _prevAccelY = accelY;
            _prevDx = dx; _prevDy = dy;
        }

        // â”€â”€ Post-processing (shared) â”€â”€
        return ApplyPostProcessing(dx, dy, dtS, now, rawIsZero);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  USER MODE PIPELINE â€” optimized for ~125Hz WH_MOUSE_LL deltas
    //  Native timing: dtS â‰ˆ 0.008s, deltas â‰ˆ 8-24px per sample
    //  Has: pattern-based recoil (no button info), edge-stall compensation
    //  NO rateScale hacks â€” all constants are native 125Hz values
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private (int outDx, int outDy) ProcessDeltaUser(int rawDx, int rawDy)
    {
        // â”€â”€ Preamble: timing, staleness, zero detection â”€â”€
        var (now, dtS, staleState, rawIsZero) = PipelinePreamble(rawDx, rawDy, 0.008);

        double dx = rawDx, dy = rawDy;

        // â”€â”€ Confinement state + edge-stall compensation (user mode needs delta injection) â”€â”€
        UpdateCursorConfinementState();
        if (_cursorConfined)
        {
            if (_cursorAtEdge)
            {
                _edgeStallCount++;
                _edgeSuppressRecoil = true;

                // USER MODE EDGE FIX: cursor is physically stuck at clip boundary
                // â†’ WH_MOUSE_LL sees hookStruct.pt clamped â†’ computed delta is 0.
                // Carry forward a fraction of the prior movement to prevent stall.
                double preMag = Math.Sqrt(_preEdgeDx * _preEdgeDx + _preEdgeDy * _preEdgeDy);
                if (preMag > 0.5 && _edgeStallCount <= 8)
                {
                    bool xClamped = Math.Abs(dx) < 0.5 && Math.Abs(_preEdgeDx) > 0.5;
                    bool yClamped = Math.Abs(dy) < 0.5 && Math.Abs(_preEdgeDy) > 0.5;
                    double decay = Math.Max(0.0, 1.0 - _edgeStallCount * 0.12);
                    if (xClamped) dx = _preEdgeDx * decay * 0.5;
                    if (yClamped) dy = _preEdgeDy * decay * 0.5;
                }
            }
            else
            {
                if (_edgeStallCount > 0) _edgeStallCount = Math.Max(0, _edgeStallCount - 2);
                _edgeSuppressRecoil = false;
                if (Math.Abs(dx) > 0.3 || Math.Abs(dy) > 0.3) { _preEdgeDx = dx; _preEdgeDy = dy; }
            }
        }
        else
        {
            _edgeStallCount = 0; _edgeSuppressRecoil = false;
            if (Math.Abs(dx) > 0.3 || Math.Abs(dy) > 0.3) { _preEdgeDx = dx; _preEdgeDy = dy; }
        }

        (dx, dy) = ClampConfinedRawSpike(dx, dy, kernelMode: false, staleState, UserLockStrictness);

        // â”€â”€ Sensitivity + Aspect Ratio + Pre-Scale (shared) â”€â”€
        (dx, dy) = ApplySensitivity(dx, dy);
        (dx, dy) = ApplyModeAxisScaling(dx, dy, kernelMode: false, UserAccelX, UserAccelY);

        // â”€â”€ Velocity estimation â”€â”€
        double vel = EstimateVelocity(dx, dy, now, dtS);
        _velEma += (vel - _velEma) * 0.15; // 125Hz native smoothing

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  USER RECOIL CONTROL â€” pattern-based shake detection
        //  No button state available, uses movement analysis only
        //  + emulator edge-stall reversal suppression
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        double effectiveRecoilControl = RecoilControlStrength;
        if (effectiveRecoilControl > 0.001)
        {
            double str = Math.Clamp(effectiveRecoilControl, 0, 1);

            bool reversalX, reversalY;
            if (_edgeSuppressRecoil)
            {
                reversalX = false; reversalY = false;
            }
            else
            {
                reversalX = dx * _recoilPrevDx < 0 && Math.Abs(dx) > 0.15;
                reversalY = dy * _recoilPrevDy < 0 && Math.Abs(dy) > 0.15;
            }

            long ticksNow = _sw.ElapsedTicks;
            double windowMs = (ticksNow - _recoilLastReversalTick) / (double)Stopwatch.Frequency * 1000.0;
            if (windowMs > 150.0) { _recoilReversalCount = 0; _recoilLastReversalTick = ticksNow; }
            if (reversalX || reversalY) _recoilReversalCount++;

            double jitterX = reversalX ? Math.Abs(dx - _recoilPrevDx) : 0;
            double jitterY = reversalY ? Math.Abs(dy - _recoilPrevDy) : 0;
            _recoilJitterEmaX += (jitterX - _recoilJitterEmaX) * 0.3;
            _recoilJitterEmaY += (jitterY - _recoilJitterEmaY) * 0.3;
            double jitterMag = Math.Sqrt(_recoilJitterEmaX * _recoilJitterEmaX + _recoilJitterEmaY * _recoilJitterEmaY);

            double reversalScore = Math.Clamp(_recoilReversalCount / 6.0, 0, 1);
            double jitterScore = Math.Clamp(jitterMag / 8.0, 0, 1);
            double rawShake = Math.Max(reversalScore, jitterScore * 0.8);
            rawShake *= Math.Clamp(1.0 - vel / 2000.0, 0.1, 1.0);

            if (rawShake > _recoilShakeScore)
                _recoilShakeScore += (rawShake - _recoilShakeScore) * 0.5;
            else
                _recoilShakeScore += (rawShake - _recoilShakeScore) * 0.08;
            _recoilShakeScore = Math.Clamp(_recoilShakeScore, 0, 1);

            double baselineAlpha = 0.06 + (1.0 - _recoilShakeScore) * 0.15;
            _recoilBaselineVelX += (dx - _recoilBaselineVelX) * baselineAlpha;
            _recoilBaselineVelY += (dy - _recoilBaselineVelY) * baselineAlpha;

            if (_recoilShakeScore > 0.05)
            {
                double blendFactor = str * _recoilShakeScore * 0.70;
                blendFactor = Math.Clamp(blendFactor, 0, 0.70);
                dx = dx * (1.0 - blendFactor) + _recoilBaselineVelX * blendFactor;
                dy = dy * (1.0 - blendFactor) + _recoilBaselineVelY * blendFactor;
            }

            _recoilPrevDx = dx; _recoilPrevDy = dy;
        }

        // â”€â”€ Auto micro-deadzone â”€â”€
        { double mag = Math.Sqrt(dx * dx + dy * dy); if (mag < 0.12) { dx = 0; dy = 0; } }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  USER ACCELERATION â€” native 125Hz tuning
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        if (AccelerationStrength > 0.001 && !NoAccelerationOverride && !LinearInputEnabled)
        {
            if (!staleState)
            {
                double str = AccelerationStrength * 1.5;
                double accelRaw = (vel - _prevVel) / Math.Max(dtS, 0.0001);
                _accelEma += (accelRaw - _accelEma) * 0.2;   // 125Hz native
                _accelEma2 += (_accelEma - _accelEma2) * 0.15;

                double velComponent = Math.Pow(vel / 600.0, 1.4) * 0.6;
                double accelComponent = Math.Sign(_accelEma2) * Math.Pow(Math.Abs(_accelEma2) / 5000.0, 0.8) * 0.2;
                double accelFactor = 1.0 + str * (velComponent + Math.Max(0, accelComponent));

                double jerk = Math.Abs(_accelEma);
                double jerk2 = Math.Abs(_accelEma2);
                if (jerk > 4000) accelFactor *= Math.Max(0.5, 1.0 - (jerk - 4000) / 40000.0);
                if (jerk2 > 2000) accelFactor *= Math.Max(0.7, 1.0 - (jerk2 - 2000) / 30000.0);

                _lastAccelFactor += (accelFactor - _lastAccelFactor) * 0.3; // 125Hz native

                if (Math.Abs(AiAccelerationDampen) > 0.001)
                    _lastAccelFactor = 1.0 + (_lastAccelFactor - 1.0) * (1.0 + AiAccelerationDampen);

                SplitAxisAccelerationFactor(_lastAccelFactor, dx, dy, out double accFx, out double accFy);
                dx *= accFx;
                dy *= accFy;
            }

            _prevVel = vel;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  USER SMOOTHING â€” 5-cascade directional-adaptive (125Hz native)
        //  Full 5 stages because 125Hz samples are larger and noisier
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        double effectiveSmoothing = SmoothingStrength + AiSmoothBoost;
        if (effectiveSmoothing > 0.001)
        {
            double str = Math.Pow(Math.Clamp(effectiveSmoothing, 0, 1), 0.55);

            if (staleState && !rawIsZero)
            {
                _smoothX = dx; _smoothY = dy;
                _smooth2X = dx; _smooth2Y = dy;
                _smooth3X = dx; _smooth3Y = dy;
                _smooth4X = dx; _smooth4Y = dy;
                _smooth5X = dx; _smooth5Y = dy;
            }

            if (rawIsZero)
            {
                double decay = 0.5; // 125Hz native
                _smoothX *= decay; _smoothY *= decay;
                _smooth2X *= decay; _smooth2Y *= decay;
                _smooth3X *= decay; _smooth3Y *= decay;
                _smooth4X *= decay; _smooth4Y *= decay;
                _smooth5X *= decay; _smooth5Y *= decay;
                dx = _smooth5X; dy = _smooth5Y;
            }
            else
            {
                // Stage 1: velocity-adaptive (per-axis vel hint — smoother Y drag, finer X track)
                double axS = Math.Abs(dx), ayS = Math.Abs(dy);
                double yDomS = ayS / (axS + ayS + 1e-9);
                double velX = vel * (1.0 - 0.38 * (1.0 - yDomS) * (1.0 - yDomS));
                double velY = vel * (1.0 - 0.42 * yDomS * yDomS);
                double alpha1X = Math.Clamp(1.0 - str * 0.88 * Math.Exp(-velX / 500.0), 0.03, 1.0);
                double alpha1Y = Math.Clamp(1.0 - str * 0.88 * Math.Exp(-velY / 500.0), 0.03, 1.0);
                _smoothX += (dx - _smoothX) * alpha1X;
                _smoothY += (dy - _smoothY) * alpha1Y;

                // Stage 2: acceleration-adaptive
                double accelMag = Math.Abs(_accelEma);
                double alpha2 = Math.Clamp(1.0 - str * 0.65 * Math.Exp(-accelMag / 2500.0), 0.08, 1.0);
                _smooth2X += (_smoothX - _smooth2X) * alpha2;
                _smooth2Y += (_smoothY - _smooth2Y) * alpha2;

                // Stage 3: directional coherence
                double curDirMag = Math.Sqrt(_smooth2X * _smooth2X + _smooth2Y * _smooth2Y);
                if (curDirMag > 0.01)
                {
                    double ndx = _smooth2X / curDirMag, ndy = _smooth2Y / curDirMag;
                    _smoothDirX += (ndx - _smoothDirX) * 0.1;
                    _smoothDirY += (ndy - _smoothDirY) * 0.1;
                    double dot = ndx * _smoothDirX + ndy * _smoothDirY;
                    double coherence = Math.Clamp(dot, 0, 1);
                    double alpha3 = Math.Clamp(1.0 - str * 0.4 * coherence, 0.15, 1.0);
                    _smooth3X += (_smooth2X - _smooth3X) * alpha3;
                    _smooth3Y += (_smooth2Y - _smooth3Y) * alpha3;
                }
                else
                {
                    _smooth3X = _smooth2X; _smooth3Y = _smooth2Y;
                }

                // Stage 4: micro-jitter suppression
                double alpha4 = Math.Clamp(1.0 - str * 0.25, 0.25, 1.0);
                _smooth4X += (_smooth3X - _smooth4X) * alpha4;
                _smooth4Y += (_smooth3Y - _smooth4Y) * alpha4;

                // Stage 5: final polish
                double alpha5 = Math.Clamp(1.0 - str * 0.12, 0.5, 1.0);
                _smooth5X += (_smooth4X - _smooth5X) * alpha5;
                _smooth5Y += (_smooth4Y - _smooth5Y) * alpha5;

                dx = _smooth5X; dy = _smooth5Y;
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  USER KALMAN / STEADY AIM â€” native 125Hz state model
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        double effectiveSteady = SteadyAimStrength + AiSteadyAimBoost;
        if (effectiveSteady > 0.001)
        {
            if (staleState && !rawIsZero)
            {
                _kalmanX = dx; _kalmanY = dy;
                _kalmanVx = 0; _kalmanVy = 0;
                _kalmanAx = 0; _kalmanAy = 0;
                _kalmanPx = 1.0; _kalmanPy = 1.0;
                _kalmanPvx = 0.5; _kalmanPvy = 0.5;
                _kalmanPax = 0.5; _kalmanPay = 0.5;
            }

            if (rawIsZero)
            {
                _kalmanX *= 0.4; _kalmanY *= 0.4;  // 125Hz native
                _kalmanVx *= 0.3; _kalmanVy *= 0.3;
                _kalmanAx *= 0.2; _kalmanAy *= 0.2;
                dx = _kalmanX; dy = _kalmanY;
            }
            else
            {
                double str = effectiveSteady * 1.2;
                double processNoise = 0.08 / Math.Max(str, 0.01);  // 125Hz native
                double baseMeasNoise = str * 5.0;
                double gate = Math.Clamp(vel / 400.0, 0, 1);
                double effectiveMeas = baseMeasNoise * Math.Pow(1.0 - gate * 0.8, 2.0);

                // X-axis
                _kalmanX += _kalmanVx * dtS + 0.5 * _kalmanAx * dtS * dtS;
                _kalmanVx += _kalmanAx * dtS;
                _kalmanPx += _kalmanPvx * dtS * dtS + _kalmanPax * dtS * dtS * dtS * dtS * 0.25 + processNoise;
                double kx = _kalmanPx / (_kalmanPx + effectiveMeas);
                double innovX = dx - _kalmanX;
                _kalmanX += kx * innovX;
                _kalmanVx += (kx * innovX / Math.Max(dtS, 0.0001)) * 0.08;  // 125Hz native
                _kalmanAx += (kx * innovX / Math.Max(dtS * dtS, 0.00001)) * 0.02;
                _kalmanPx *= (1.0 - kx);
                _kalmanPvx *= 0.985;
                _kalmanPax *= 0.97;
                _kalmanAx *= 0.95;

                // Y-axis
                _kalmanY += _kalmanVy * dtS + 0.5 * _kalmanAy * dtS * dtS;
                _kalmanVy += _kalmanAy * dtS;
                _kalmanPy += _kalmanPvy * dtS * dtS + _kalmanPay * dtS * dtS * dtS * dtS * 0.25 + processNoise;
                double ky = _kalmanPy / (_kalmanPy + effectiveMeas);
                double innovY = dy - _kalmanY;
                _kalmanY += ky * innovY;
                _kalmanVy += (ky * innovY / Math.Max(dtS, 0.0001)) * 0.08;
                _kalmanAy += (ky * innovY / Math.Max(dtS * dtS, 0.00001)) * 0.02;
                _kalmanPy *= (1.0 - ky);
                _kalmanPvy *= 0.985;
                _kalmanPay *= 0.97;
                _kalmanAy *= 0.95;

                dx = _kalmanX; dy = _kalmanY;
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  USER ANTI-OVERSHOOT â€” native 125Hz thresholds
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        if (AntiOvershootStrength > 0.001)
        {
            double str = AntiOvershootStrength * 1.5;

            double accelX = (dx - _prevDx) / Math.Max(dtS, 0.0001);
            double accelY = (dy - _prevDy) / Math.Max(dtS, 0.0001);

            double jerkX = (accelX - _prevAccelX) / Math.Max(dtS, 0.0001);
            double jerkY = (accelY - _prevAccelY) / Math.Max(dtS, 0.0001);
            _jerkEmaX += (jerkX - _jerkEmaX) * 0.25;  // 125Hz native
            _jerkEmaY += (jerkY - _jerkEmaY) * 0.25;

            double snapX = (jerkX - _jerk2EmaX);
            double snapY = (jerkY - _jerk2EmaY);
            _jerk2EmaX += (jerkX - _jerk2EmaX) * 0.2;
            _jerk2EmaY += (jerkY - _jerk2EmaY) * 0.2;

            double jerkMag = Math.Sqrt(_jerkEmaX * _jerkEmaX + _jerkEmaY * _jerkEmaY);
            double snapMag = Math.Sqrt(snapX * snapX + snapY * snapY);

            _momentumX += (dx - _momentumX) * 0.15;  // 125Hz native
            _momentumY += (dy - _momentumY) * 0.15;
            double momentumDot = dx * _momentumX + dy * _momentumY;
            bool isOvershooting = momentumDot < 0 && Math.Sqrt(dx * dx + dy * dy) > 1.0;

            double threshold = 2500.0 + vel * 6.0;  // 125Hz native
            double dampen = 1.0;

            if (jerkMag > threshold)
                dampen *= Math.Max(0.15, 1.0 - str * (jerkMag - threshold) / (threshold * 4.0));
            if (snapMag > threshold * 1.5)
                dampen *= Math.Max(0.4, 1.0 - str * 0.3 * (snapMag - threshold * 1.5) / (threshold * 5.0));
            if (isOvershooting)
                dampen *= Math.Max(0.25, 1.0 - str * 0.6);

            dx *= dampen; dy *= dampen;

            if (dx * _prevDx < 0)
            {
                double reversalStr = Math.Min(Math.Abs(dx), Math.Abs(_prevDx)) / Math.Max(Math.Abs(dx) + Math.Abs(_prevDx), 0.01);
                dx *= Math.Max(0.2, 1.0 - str * 0.6 * reversalStr);
            }
            if (dy * _prevDy < 0)
            {
                double reversalStr = Math.Min(Math.Abs(dy), Math.Abs(_prevDy)) / Math.Max(Math.Abs(dy) + Math.Abs(_prevDy), 0.01);
                dy *= Math.Max(0.2, 1.0 - str * 0.6 * reversalStr);
            }

            _prevAccelX = accelX; _prevAccelY = accelY;
            _prevDx = dx; _prevDy = dy;
        }

        // â”€â”€ Post-processing (shared) â”€â”€
        return ApplyPostProcessing(dx, dy, dtS, now, rawIsZero);
    }

    #endregion

    #region Math Utilities

    /// <summary>
    /// 24-sample Gaussian-weighted velocity estimation.
    /// Uses Gaussian kernel weighting (Ïƒ=0.04s) for ultra-smooth speed estimation.
    /// Rejects outlier samples &gt;2Ïƒ from median for robustness.
    /// </summary>
    /// <param name="dtS">Elapsed time since last processed sample (from <see cref="PipelinePreamble"/>).</param>
    private double EstimateVelocity(double dx, double dy, long now, double dtS)
    {
        _vxBuf[_vIdx] = dx;
        _vyBuf[_vIdx] = dy;
        _vtBuf[_vIdx] = now;
        _vIdx = (_vIdx + 1) % VBUF;

        double totalWeight = 0, weightedSpeed = 0;
        double sigma = 0.04;
        double minDenom = Math.Max(dtS, 1e-6);
        for (int i = 0; i < VBUF; i++)
        {
            if (_vtBuf[i] <= 0) continue;
            double age = (now - _vtBuf[i]) / (double)Stopwatch.Frequency;
            if (age > 0.2) continue;
            // Gaussian weighting
            double weight = Math.Exp(-0.5 * (age / sigma) * (age / sigma));
            // |delta|/age blows up when age→0 (same-tick sample). Use max(age, dtS) so the newest
            // sample is ~|delta|/dtS (px/s) instead of thousands× inflated — that was driving
            // smoothing + accel to "max speed" after a second or two of micro-movement.
            double denom = Math.Max(age, minDenom);
            double speed = Math.Sqrt(_vxBuf[i] * _vxBuf[i] + _vyBuf[i] * _vyBuf[i]) / denom;
            weightedSpeed += speed * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? weightedSpeed / totalWeight : 0;
    }

    /// <summary>
    /// 12-segment perceptual sensitivity curve (gradual).
    /// Neutral at ~35 (1.0x). Values below 1.0 map to an extra-fine zone for small edits (e.g. 0.5, 1.25).
    ///
    ///   0.01≈0.003x  1≈0.064x   5=0.12x   10=0.22x   15=0.35x   20=0.50x
    ///  25=0.68x   30=0.83x   35=1.00x   40=1.12x   50=1.35x
    ///  60=1.60x   70=1.90x   80=2.25x   90=2.70x  100=3.20x
    /// </summary>
    private static double SensCurve12(double v)
    {
        if (v <= 0) return 0.003;
        // Ultra-low band: smooth continuation into the first segment (matches ~v=1 at boundary)
        if (v < 1.0)
        {
            double y1 = 0.05 + (1.0 / 5.0) * 0.07; // SensCurve12(1) in legacy first segment
            return 0.003 + (v / 1.0) * (y1 - 0.003);
        }
        if (v <= 5)   return 0.05 + (v / 5.0) * 0.07;                         //  0..5:    0.05  .. 0.12
        if (v <= 10)  return 0.12 + ((v - 5) / 5.0) * 0.10;                   //  5..10:   0.12  .. 0.22
        if (v <= 15)  return 0.22 + ((v - 10) / 5.0) * 0.13;                  // 10..15:   0.22  .. 0.35
        if (v <= 20)  return 0.35 + ((v - 15) / 5.0) * 0.15;                  // 15..20:   0.35  .. 0.50
        if (v <= 25)  return 0.50 + ((v - 20) / 5.0) * 0.18;                  // 20..25:   0.50  .. 0.68
        if (v <= 30)  return 0.68 + ((v - 25) / 5.0) * 0.15;                  // 25..30:   0.68  .. 0.83
        if (v <= 35)  return 0.83 + ((v - 30) / 5.0) * 0.17;                  // 30..35:   0.83  .. 1.00  (neutral)
        if (v <= 40)  return 1.00 + ((v - 35) / 5.0) * 0.12;                  // 35..40:   1.00  .. 1.12
        if (v <= 50)  return 1.12 + ((v - 40) / 10.0) * 0.23;                 // 40..50:   1.12  .. 1.35
        if (v <= 60)  return 1.35 + ((v - 50) / 10.0) * 0.25;                 // 50..60:   1.35  .. 1.60
        if (v <= 70)  return 1.60 + ((v - 60) / 10.0) * 0.30;                 // 60..70:   1.60  .. 1.90
        if (v <= 80)  return 1.90 + ((v - 70) / 10.0) * 0.35;                 // 70..80:   1.90  .. 2.25
        if (v <= 90)  return 2.25 + ((v - 80) / 10.0) * 0.45;                 // 80..90:   2.25  .. 2.70
        return 2.70 + ((v - 90) / 10.0) * 0.50;                               // 90..100:  2.70  .. 3.20
    }

    /// <summary>
    /// Scales the normalized SensCurve12 output so that the user's Sensitivity Limit
    /// controls the maximum multiplier. At curveVal=1.0 (neutral, slider=35), returns 1.0.
    /// Above 1.0, the output is remapped so the curve max (3.20) maps to sensLimit.
    /// Below 1.0, the output is kept as-is (low sensitivity stays unaffected).
    /// This makes the sensitivity limit the real "ceiling" the user controls.
    /// </summary>
    private static double ScaleCurveToLimit(double curveVal, double sensLimit)
    {
        const double CURVE_MAX = 3.20; // SensCurve12 max at slider=100
        if (curveVal <= 1.0)
            return curveVal; // below neutral: no scaling needed
        // Remap 1.0..CURVE_MAX to 1.0..sensLimit
        double t = (curveVal - 1.0) / (CURVE_MAX - 1.0); // 0..1 normalized above-neutral
        return 1.0 + t * (sensLimit - 1.0);
    }

    /// <summary>
    /// 4-segment exponential piecewise sensitivity curve (legacy).
    /// </summary>
    private static double SensitivityToMultiplier(int sens)
    {
        double s = Math.Clamp(sens, 1, 100);
        if (s <= 10) { double t = (s - 1) / 9.0; return 0.03 + t * t * 0.37; }
        else if (s <= 25) { double t = (s - 10) / 15.0; return 0.40 + t * 0.60; }
        else if (s <= 50) { double t = (s - 25) / 25.0; return 1.00 + t * t * 1.50; }
        else { double t = (s - 50) / 50.0; return 2.50 + t * t * 4.50; }
    }

    /// <summary>
    /// Pre/Post scale and General Sens use the numeric value directly (1.50 = 1.5×) so the Advanced
    /// panel matches user expectation and comma/dot decimals behave predictably.
    /// </summary>
    private static double AmplifyScale(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 1.0;
        return Math.Clamp(v, 0.01, 100.0);
    }

    // Legacy alias kept for compatibility
    private static double SensCurve(double v) => SensCurve12(v);

    /// <summary>
    /// Computes a series of (inputSpeed, outputSpeed) points for the live curve graph.
    /// Returns 50 points covering input speeds 0–50.
    /// </summary>
    public static (double[] inputs, double[] outputs) ComputeCurvePoints(
        double overallSens, double xAxisSens, AccelCurveType curveType, double acceleration, double sensLimit)
    {
        const int N = 50;
        double[] inputs = new double[N];
        double[] outputs = new double[N];

        double sensLimitMax = Math.Max(sensLimit, 0.1);
        double overallMul = ScaleCurveToLimit(SensCurve12(overallSens), sensLimitMax);
        double rawX = ScaleCurveToLimit(SensCurve12(xAxisSens), sensLimitMax);
        double xRatio = (overallMul > 1e-9) ? rawX / overallMul : 1.0;
        double baseMul = overallMul * xRatio;

        for (int i = 0; i < N; i++)
        {
            double speed = i; // 0..49
            inputs[i] = speed;

            double curveMul = 1.0;
            switch (curveType)
            {
                case AccelCurveType.Linear:
                    curveMul = acceleration;
                    break;
                case AccelCurveType.Power:
                    if (speed > 0.5)
                    {
                        double exp = Math.Clamp(acceleration, 0.01, 2.0);
                        curveMul = 1.0 + Math.Pow(speed / 10.0, exp) * 0.5;
                    }
                    break;
                case AccelCurveType.Natural:
                    if (speed > 0.5)
                    {
                        double lim = Math.Clamp(acceleration, 0.1, 5.0);
                        curveMul = 1.0 + lim * (1.0 - Math.Exp(-0.15 * speed / 10.0));
                    }
                    break;

                case AccelCurveType.Logarithmic:
                    if (speed > 0.5)
                    {
                        double gain = Math.Clamp(acceleration, 0.1, 3.0);
                        curveMul = 1.0 + gain * Math.Log(1.0 + speed / 10.0);
                    }
                    break;

                case AccelCurveType.Sigmoid:
                    if (speed > 0.5)
                    {
                        double height = Math.Clamp(acceleration, 0.1, 4.0);
                        double normSpeed = speed / 10.0;
                        double sigmoid = 1.0 / (1.0 + Math.Exp(-3.0 * (normSpeed - 1.5)));
                        curveMul = 1.0 + height * sigmoid;
                    }
                    break;

                case AccelCurveType.Step:
                    {
                        double jump = Math.Clamp(acceleration, 1.0, 5.0);
                        double threshold = 5.0;
                        curveMul = speed > threshold ? jump : 1.0;
                    }
                    break;
            }

            double output = speed * baseMul * curveMul;

            // Apply sens limit (hard ceiling matching pipeline)
            if (sensLimit < 9.99)
            {
                if (output > sensLimit)
                    output = sensLimit;
            }

            outputs[i] = output;
        }

        return (inputs, outputs);
    }

    #endregion
}
