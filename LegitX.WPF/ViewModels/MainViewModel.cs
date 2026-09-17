using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LegitX.WPF.Services;
using Microsoft.Win32;

namespace LegitX.WPF.ViewModels;

public class MainViewModel : ViewModelBase
{
    #region Navigation

    private int _selectedTab;
    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            // Optimization tab hosts Interception install UI — re-check registry/files when user opens it.
            if (value == 2)
                RefreshInterceptionStatus();
        }
    }

    // Tab indices: 0=Sensitivity, 1=Advanced, 2=Optimization, 3=Extras

    #endregion

    #region Main Sensitivity Panel (Tab 0)

    private readonly MouseSensitivityEngine _sensitivityEngine = new();

    private string _mouseSensitivityText = "25.00";
    /// <summary>Base speed — accepts decimals; comma or dot (e.g. 1,50).</summary>
    public string MouseSensitivityText
    {
        get => _mouseSensitivityText;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 100.0, 25.0);
            if (!SetProperty(ref _mouseSensitivityText, validated)) return;
            PushMainSpeedToEngine();
            RefreshCurvePoints();
            AutoSaveNow();
        }
    }

    private string _xAxisSensitivityText = "15.00";
    public string XAxisSensitivityText
    {
        get => _xAxisSensitivityText;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 100.0, 15.0);
            if (!SetProperty(ref _xAxisSensitivityText, validated)) return;
            PushMainSpeedToEngine();
            RefreshCurvePoints();
            AutoSaveNow();
        }
    }

    private string _yAxisSensitivityText = "15.00";
    public string YAxisSensitivityText
    {
        get => _yAxisSensitivityText;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 100.0, 15.0);
            if (!SetProperty(ref _yAxisSensitivityText, validated)) return;
            PushMainSpeedToEngine();
            RefreshCurvePoints();
            AutoSaveNow();
        }
    }

    private void PushMainSpeedToEngine()
    {
        _sensitivityEngine.OverallSensitivity = TryParseUserDouble(MouseSensitivityText, out double o) ? Math.Clamp(o, 0.01, 100.0) : 25.0;
        _sensitivityEngine.XAxisSensitivity = TryParseUserDouble(XAxisSensitivityText, out double x) ? Math.Clamp(x, 0.01, 100.0) : 15.0;
        _sensitivityEngine.YAxisSensitivity = TryParseUserDouble(YAxisSensitivityText, out double y) ? Math.Clamp(y, 0.01, 100.0) : 15.0;
    }

    private static bool TryParseUserDouble(string? s, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string n = s.Trim().Replace(',', '.');
        return double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ── Curve Type (0=Linear, 1=Power, 2=Natural) ──
    private int _curveTypeIndex;
    public int CurveTypeIndex
    {
        get => _curveTypeIndex;
        set
        {
            if (SetProperty(ref _curveTypeIndex, value))
            {
                _sensitivityEngine.CurveType = (AccelCurveType)value;
                OnPropertyChanged(nameof(CurveTypeName));
                RefreshCurvePoints();
                AutoSaveNow();
            }
        }
    }
    public string CurveTypeName => _curveTypeIndex switch
    {
        0 => "Flat",
        1 => "Exponential",
        2 => "Adaptive",
        3 => "Logarithmic",
        4 => "S-Curve",
        5 => "Threshold",
        _ => "Flat"
    };

    // ── Live curve graph points (PointCollection for WPF Polyline) ──
    private PointCollection _curvePoints = new();
    public PointCollection CurvePoints
    {
        get => _curvePoints;
        set => SetProperty(ref _curvePoints, value);
    }

    // ── Filled area under the curve (Polygon) ──
    private PointCollection _curveAreaPoints = new();
    public PointCollection CurveAreaPoints
    {
        get => _curveAreaPoints;
        set => SetProperty(ref _curveAreaPoints, value);
    }

    /// <summary>Recalculates the velocity curve polyline points for the live graph.</summary>
    private void RefreshCurvePoints()
    {
        double sensLimit = 10.0;
        double accel = 1.0;
        if (double.TryParse(SensLimit, out double sl)) sensLimit = sl;
        if (double.TryParse(Acceleration, out double ac)) accel = ac;

        double ms = TryParseUserDouble(MouseSensitivityText, out double msv) ? msv : 25.0;
        double xa = TryParseUserDouble(XAxisSensitivityText, out double xav) ? xav : 15.0;
        var (inputs, outputs) = MouseSensitivityEngine.ComputeCurvePoints(
            ms, xa,
            (AccelCurveType)_curveTypeIndex, accel, sensLimit);

        // Viewbox canvas is fixed 400×200, Viewbox stretches it to actual size
        const double graphW = 400.0;
        const double graphH = 200.0;
        const double padL = 0.0;
        const double padR = 0.0;
        const double plotW = graphW - padL - padR;

        double maxInput = 49.0;
        double maxOutput = 0;
        for (int i = 0; i < outputs.Length; i++)
            if (outputs[i] > maxOutput) maxOutput = outputs[i];
        if (maxOutput < 1) maxOutput = 1;
        // Add 10% headroom so the line doesn't clip the top
        maxOutput *= 1.1;

        var linePts = new PointCollection(inputs.Length);
        var areaPts = new PointCollection(inputs.Length + 2);

        // Bottom-left corner of fill polygon
        areaPts.Add(new Point(padL, graphH));

        for (int i = 0; i < inputs.Length; i++)
        {
            double x = padL + (inputs[i] / maxInput) * plotW;
            double y = graphH - (outputs[i] / maxOutput) * graphH;
            y = Math.Clamp(y, 0, graphH);
            linePts.Add(new Point(x, y));
            areaPts.Add(new Point(x, y));
        }

        // Bottom-right corner of fill polygon
        areaPts.Add(new Point(padL + plotW, graphH));

        CurvePoints = linePts;
        CurveAreaPoints = areaPts;
    }

    /// <summary>
    /// Reads the Windows mouse speed from registry (1-20).
    /// Used only as a reference — our engine uses its own 1-100 scale.
    /// </summary>
    private static int GetSystemMouseSpeed()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            var val = key?.GetValue("MouseSensitivity")?.ToString();
            if (int.TryParse(val, out int speed))
                return Math.Clamp(speed, 1, 20);
        }
        catch { }
        return 10;
    }

    private int _displayWidth = 1920;
    public int DisplayWidth
    {
        get => _displayWidth;
        set
        {
            if (SetProperty(ref _displayWidth, value))
            {
                OnPropertyChanged(nameof(DisplayWidthDisplay));
                _sensitivityEngine.DisplayWidth = value;
                AutoSaveNow();
            }
        }
    }
    public string DisplayWidthDisplay => _displayWidth.ToString();

    private int _displayHeight = 1080;
    public int DisplayHeight
    {
        get => _displayHeight;
        set
        {
            if (SetProperty(ref _displayHeight, value))
            {
                OnPropertyChanged(nameof(DisplayHeightDisplay));
                _sensitivityEngine.DisplayHeight = value;
                AutoSaveNow();
            }
        }
    }
    public string DisplayHeightDisplay => _displayHeight.ToString();

    private int _emulatorWidth = 1280;
    public int EmulatorWidth
    {
        get => _emulatorWidth;
        set
        {
            if (SetProperty(ref _emulatorWidth, value))
            {
                OnPropertyChanged(nameof(EmulatorWidthDisplay));
                _sensitivityEngine.EmulatorWidth = value;
                AutoSaveNow();
            }
        }
    }
    public string EmulatorWidthDisplay => _emulatorWidth.ToString();

    private int _emulatorHeight = 720;
    public int EmulatorHeight
    {
        get => _emulatorHeight;
        set
        {
            if (SetProperty(ref _emulatorHeight, value))
            {
                OnPropertyChanged(nameof(EmulatorHeightDisplay));
                _sensitivityEngine.EmulatorHeight = value;
                AutoSaveNow();
            }
        }
    }
    public string EmulatorHeightDisplay => _emulatorHeight.ToString();

    // ── Emulator resolution status / fullscreen warning ──
    private string _emulatorResolutionStatus = "";
    public string EmulatorResolutionStatus { get => _emulatorResolutionStatus; set => SetProperty(ref _emulatorResolutionStatus, value); }

    private bool _isEmulatorWindowed;
    public bool IsEmulatorWindowed { get => _isEmulatorWindowed; set => SetProperty(ref _isEmulatorWindowed, value); }

    private string _emulatorResolutionSource = "";
    public string EmulatorResolutionSource { get => _emulatorResolutionSource; set => SetProperty(ref _emulatorResolutionSource, value); }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(HookModeText));
        }
    }

    private bool _isPaused = true;
    public bool IsPaused
    {
        get => _isPaused;
        set => SetProperty(ref _isPaused, value);
    }

    /// <summary>
    /// Shows "KERNEL MODE" or "USER MODE" in the bottom bar when active, empty when inactive.
    /// </summary>
    public string HookModeText => _isActive
        ? (_sensitivityEngine.IsKernelMode ? "KERNEL MODE" : "USER MODE")
        : "";

    #endregion

    #region Advanced Sensitivity Panel (Tab 1)

    private string _generalSens = "1.00";
    public string GeneralSens
    {
        get => _generalSens;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 10.00, 1.00);
            if (SetProperty(ref _generalSens, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _sensLimit = "10.00";
    public string SensLimit
    {
        get => _sensLimit;
        set
        {
            string validated = ValidateTextBox(value, 0.10, 10.00, 10.00);
            if (SetProperty(ref _sensLimit, validated)) { RefreshCurvePoints(); SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _preScaleX = "1.00";
    public string PreScaleX
    {
        get => _preScaleX;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 5.00, 1.00);
            if (SetProperty(ref _preScaleX, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _preScaleY = "1.00";
    public string PreScaleY
    {
        get => _preScaleY;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 5.00, 1.00);
            if (SetProperty(ref _preScaleY, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _postScaleX = "1.00";
    public string PostScaleX
    {
        get => _postScaleX;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 5.00, 1.00);
            if (SetProperty(ref _postScaleX, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _postScaleY = "1.00";
    public string PostScaleY
    {
        get => _postScaleY;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 5.00, 1.00);
            if (SetProperty(ref _postScaleY, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _acceleration = "1.00";
    public string Acceleration
    {
        get => _acceleration;
        set
        {
            string validated = ValidateTextBox(value, 0.01, 5.00, 1.00);
            if (SetProperty(ref _acceleration, validated)) { RefreshCurvePoints(); SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    // ── PRO features ──
    private string _advRecoilControl = "0.00";
    public string AdvRecoilControl
    {
        get => _advRecoilControl;
        set
        {
            string validated = ValidateTextBox(value, 0.00, 1.00, 0.00);
            if (SetProperty(ref _advRecoilControl, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _advSmoothStrength = "0.35";
    public string AdvSmoothStrength
    {
        get => _advSmoothStrength;
        set
        {
            string validated = ValidateTextBox(value, 0.00, 1.00, 0.35);
            if (SetProperty(ref _advSmoothStrength, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _advSteadyAim = "0.00";
    public string AdvSteadyAim
    {
        get => _advSteadyAim;
        set
        {
            string validated = ValidateTextBox(value, 0.00, 1.00, 0.00);
            if (SetProperty(ref _advSteadyAim, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    // ── Per-mode lock tuning (User vs Kernel) ──
    private int _userLockStrictness = 1;   // Normal
    public int UserLockStrictness
    {
        get => _userLockStrictness;
        set
        {
            int v = Math.Clamp(value, 0, 3);
            if (SetProperty(ref _userLockStrictness, v))
            {
                SyncEngineSettings();
                SettingsService.SavePreference("UserLockStrictness", v);
                AutoSaveNow();
            }
        }
    }

    private int _kernelLockStrictness = 2; // Strict
    public int KernelLockStrictness
    {
        get => _kernelLockStrictness;
        set
        {
            int v = Math.Clamp(value, 0, 3);
            if (SetProperty(ref _kernelLockStrictness, v))
            {
                SyncEngineSettings();
                SettingsService.SavePreference("KernelLockStrictness", v);
                AutoSaveNow();
            }
        }
    }

    /// <summary>
    /// Advanced → lock tuning: show kernel sliders only when the Interception driver is installed
    /// and the license allows kernel access. Otherwise show user-mode tuners only.
    /// </summary>
    public bool ShowKernelModeLockTuning => InterceptionInstalled && KernelAccessAllowed;

    public bool ShowUserModeLockTuning => !ShowKernelModeLockTuning;

    private void NotifyLockTuningPanelVisibility()
    {
        OnPropertyChanged(nameof(ShowKernelModeLockTuning));
        OnPropertyChanged(nameof(ShowUserModeLockTuning));
    }

    private string _userAccelX = "1.00";
    public string UserAccelX
    {
        get => _userAccelX;
        set
        {
            string validated = ValidateTextBox(value, 0.50, 1.80, 1.00);
            if (SetProperty(ref _userAccelX, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _userAccelY = "0.92";
    public string UserAccelY
    {
        get => _userAccelY;
        set
        {
            string validated = ValidateTextBox(value, 0.50, 1.80, 0.92);
            if (SetProperty(ref _userAccelY, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _kernelAccelX = "1.00";
    public string KernelAccelX
    {
        get => _kernelAccelX;
        set
        {
            string validated = ValidateTextBox(value, 0.50, 1.80, 1.00);
            if (SetProperty(ref _kernelAccelX, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private string _kernelAccelY = "0.85";
    public string KernelAccelY
    {
        get => _kernelAccelY;
        set
        {
            string validated = ValidateTextBox(value, 0.50, 1.80, 0.85);
            if (SetProperty(ref _kernelAccelY, validated)) { SyncEngineSettings(); AutoSaveNow(); }
        }
    }

    private bool _advancedActive;
    public bool AdvancedActive
    {
        get => _advancedActive;
        set => SetProperty(ref _advancedActive, value);
    }

    private bool _persistAcrossApps;
    public bool PersistAcrossApps
    {
        get => _persistAcrossApps;
        set
        {
            if (SetProperty(ref _persistAcrossApps, value))
            {
                _sensitivityEngine.PersistAcrossApps = value;
                AutoSaveNow();
            }
        }
    }

    private bool _autoSaveEnabled;
    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set
        {
            if (SetProperty(ref _autoSaveEnabled, value))
            {
                SettingsService.SavePreference("AutoSave", value ? 1 : 0);
                if (value) AutoSaveNow(); // save immediately when turned on
            }
        }
    }

    private bool _alwaysOnTop = true;
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (SetProperty(ref _alwaysOnTop, value))
            {
                SettingsService.SavePreference("AlwaysOnTop", value ? 1 : 0);
                AlwaysOnTopChanged?.Invoke(value);
            }
        }
    }
    /// <summary>Raised so the Window can set Topmost.</summary>
    public event Action<bool>? AlwaysOnTopChanged;

    private bool _autoDetectEmulatorRes = true;
    public bool AutoDetectEmulatorRes
    {
        get => _autoDetectEmulatorRes;
        set
        {
            if (SetProperty(ref _autoDetectEmulatorRes, value))
                SettingsService.SavePreference("AutoDetectEmuRes", value ? 1 : 0);
        }
    }

    private bool _liveResolutionSync = true;
    public bool LiveResolutionSync
    {
        get => _liveResolutionSync;
        set
        {
            if (SetProperty(ref _liveResolutionSync, value))
                SettingsService.SavePreference("LiveResSync", value ? 1 : 0);
        }
    }

    /// <summary>
    /// Silently persists all current settings if AutoSave is enabled.
    /// Called by every property setter that should trigger a save.
    /// </summary>
    private void AutoSaveNow()
    {
        if (!_autoSaveEnabled) return;
        if (!TryParseAdvanced(out var gs, out var sl, out var px, out var py, out var sx, out var sy, out var ac,
                out var rc, out var smooth, out var steady))
            return; // don't save if advanced values are invalid
        SettingsService.SaveMainSettings(
            TryParseUserDouble(MouseSensitivityText, out double ms) ? ms : 25.0,
            TryParseUserDouble(XAxisSensitivityText, out double xa) ? xa : 15.0,
            TryParseUserDouble(YAxisSensitivityText, out double ya) ? ya : 15.0,
            DisplayWidth, DisplayHeight, EmulatorWidth, EmulatorHeight, CurveTypeIndex);
        SettingsService.SaveAdvancedSettings(gs, sl, px, py, sx, sy, ac, rc, smooth, steady);
        SettingsService.SavePreference("UserLockStrictness", Math.Clamp(UserLockStrictness, 0, 3));
        SettingsService.SavePreference("KernelLockStrictness", Math.Clamp(KernelLockStrictness, 0, 3));
        SettingsService.SavePreference("UserAccelXx100",
            (int)Math.Round((TryParseUserDouble(UserAccelX, out double uax) ? Math.Clamp(uax, 0.50, 1.80) : 1.00) * 100.0));
        SettingsService.SavePreference("UserAccelYx100",
            (int)Math.Round((TryParseUserDouble(UserAccelY, out double uay) ? Math.Clamp(uay, 0.50, 1.80) : 0.92) * 100.0));
        SettingsService.SavePreference("KernelAccelXx100",
            (int)Math.Round((TryParseUserDouble(KernelAccelX, out double kax) ? Math.Clamp(kax, 0.50, 1.80) : 1.00) * 100.0));
        SettingsService.SavePreference("KernelAccelYx100",
            (int)Math.Round((TryParseUserDouble(KernelAccelY, out double kay) ? Math.Clamp(kay, 0.50, 1.80) : 0.85) * 100.0));
        SaveDeviceSettings();
        SyncEngineSettings();
        SaveSettingsRequested?.Invoke();
    }

    #endregion

    #region Optimization Panel (Tab 2)

    // ── Game Mode Enhancer ──
    private bool _optGameMode; public bool OptGameMode { get => _optGameMode; set { if (SetProperty(ref _optGameMode, value) && value) _ = ApplyGameModeAsync(); } }

    // ── NEW: Power Plan Optimizer (Ultimate Performance) ──
    private bool _optPowerPlan; public bool OptPowerPlan { get => _optPowerPlan; set { if (SetProperty(ref _optPowerPlan, value) && value) _ = ApplyPowerPlanAsync(); } }

    // ── NEW: Audio Latency Optimizer ──
    private bool _optAudioLatency; public bool OptAudioLatency { get => _optAudioLatency; set { if (SetProperty(ref _optAudioLatency, value) && value) _ = ApplyAudioLatencyAsync(); } }

    // ── NEW: DPI Override ──
    private bool _optDpiOverride; public bool OptDpiOverride { get => _optDpiOverride; set { if (SetProperty(ref _optDpiOverride, value) && value) _ = ApplyDpiOverrideAsync(); } }

    // ── NEW: System Cleaner (command, not toggle) ──
    public ICommand RunSystemCleanerCommand { get; private set; } = null!;

    // ── NEW: Privacy Mode ──
    public ICommand RunPrivacyCleanCommand { get; private set; } = null!;

    // ── NEW: Controller Input Optimizer ──
    private bool _optController; public bool OptController { get => _optController; set { if (SetProperty(ref _optController, value) && value) _ = ApplyControllerOptAsync(); } }

    // ── NEW: Mouse Registry Optimizer (gaming-grade registry tweaks for mouse/cursor) ──
    private bool _optMouseRegistry; public bool OptMouseRegistry { get => _optMouseRegistry; set { if (SetProperty(ref _optMouseRegistry, value)) _ = value ? ApplyMouseRegistryOptAsync() : RestoreMouseRegistryAsync(); } }

    // ── NEW: System Info ──
    private string _sysInfoCpu = "Detecting...";
    public string SysInfoCpu { get => _sysInfoCpu; set => SetProperty(ref _sysInfoCpu, value); }

    private string _sysInfoRam = "Detecting...";
    public string SysInfoRam { get => _sysInfoRam; set => SetProperty(ref _sysInfoRam, value); }

    private string _sysInfoGpu = "Detecting...";
    public string SysInfoGpu { get => _sysInfoGpu; set => SetProperty(ref _sysInfoGpu, value); }

    private string _sysInfoOs = "Detecting...";
    public string SysInfoOs { get => _sysInfoOs; set => SetProperty(ref _sysInfoOs, value); }

    // Drivers — replaced with real driver status
    private string _driverStatus = "Checking...";
    public string DriverStatus { get => _driverStatus; set => SetProperty(ref _driverStatus, value); }

    private bool _driversHealthy;
    public bool DriversHealthy { get => _driversHealthy; set => SetProperty(ref _driversHealthy, value); }

    private bool _isDriverLoading;
    public bool IsDriverLoading { get => _isDriverLoading; set => SetProperty(ref _isDriverLoading, value); }

    // Interception Kernel Driver Install/Uninstall
    private bool _interceptionInstalled;
    public bool InterceptionInstalled
    {
        get => _interceptionInstalled;
        set
        {
            if (SetProperty(ref _interceptionInstalled, value))
            {
                OnPropertyChanged(nameof(CanInstallDriver));
                OnPropertyChanged(nameof(CanUninstallDriver));
                NotifyLockTuningPanelVisibility();
            }
        }
    }

    private string _interceptionStatusText = "Checking...";
    public string InterceptionStatusText { get => _interceptionStatusText; set => SetProperty(ref _interceptionStatusText, value); }

    private bool _isInterceptionBusy;
    public bool IsInterceptionBusy
    {
        get => _isInterceptionBusy;
        set
        {
            if (SetProperty(ref _isInterceptionBusy, value))
            {
                OnPropertyChanged(nameof(CanInstallDriver));
                OnPropertyChanged(nameof(CanUninstallDriver));
            }
        }
    }

    /// <summary>
    /// Whether the user's license key allows kernel-level access.
    /// Fetched from Firestore licenses/{code}.kernelAccess field.
    /// When false, the engine stays in User Mode even if Interception is installed; install driver is disabled.
    /// </summary>
    private bool _kernelAccessAllowed = true;
    public bool KernelAccessAllowed
    {
        get => _kernelAccessAllowed;
        set
        {
            bool previous = _kernelAccessAllowed;
            if (!SetProperty(ref _kernelAccessAllowed, value))
                return;

            _sensitivityEngine.ForceUserMode = !value;
            OnPropertyChanged(nameof(CanInstallDriver));
            OnPropertyChanged(nameof(CanUninstallDriver));
            OnPropertyChanged(nameof(KernelAccessDeniedText));
            NotifyLockTuningPanelVisibility();
            UpdateInterceptionStatusText();

            if (previous != value && IsActive)
                Application.Current?.Dispatcher.Invoke(RebindActiveEngineForKernelLicense);
        }
    }

    /// <summary>Shows a warning message when kernel access is denied.</summary>
    public string KernelAccessDeniedText => _kernelAccessAllowed
        ? ""
        : "Kernel Mode is disabled for your license. Contact your reseller or admin to upgrade.";

    /// <summary>Install button enabled only when driver is NOT installed, not busy, and kernel access is allowed.</summary>
    public bool CanInstallDriver => !InterceptionInstalled && !IsInterceptionBusy && KernelAccessAllowed;

    /// <summary>Uninstall enabled when the OS driver is present — license must not block removing hardware state.</summary>
    public bool CanUninstallDriver => InterceptionInstalled && !IsInterceptionBusy;

    // Reset Mouse to Factory Defaults
    private bool _resetMouseDefaults;
    public bool ResetMouseDefaults
    {
        get => _resetMouseDefaults;
        set
        {
            if (SetProperty(ref _resetMouseDefaults, value) && value)
                ApplyResetMouseDefaults();
        }
    }

    // Bypass Type
    private int _selectedBypassIndex = -1;
    public int SelectedBypassIndex
    {
        get => _selectedBypassIndex;
        set { if (SetProperty(ref _selectedBypassIndex, value)) ExecuteBypass(value); }
    }

    private bool _bypassToggle;
    public bool BypassToggle { get => _bypassToggle; set => SetProperty(ref _bypassToggle, value); }

    #endregion

    #region Extras Panel (Tab 3)

    private bool _streamerMode;
    public bool StreamerMode { get => _streamerMode; set { if (SetProperty(ref _streamerMode, value)) StreamerModeChanged?.Invoke(value); } }

    private bool _formBypass;
    public bool FormBypass { get => _formBypass; set { if (SetProperty(ref _formBypass, value)) FormBypassChanged?.Invoke(value); } }

    private bool _pinOnTop = true;
    public bool PinOnTop { get => _pinOnTop; set { if (SetProperty(ref _pinOnTop, value)) PinOnTopChanged?.Invoke(value); } }

    private bool _stopNetwork;
    public bool StopNetwork { get => _stopNetwork; set { if (SetProperty(ref _stopNetwork, value)) StopNetworkChanged?.Invoke(value); } }

    #endregion

    #region Firebase Feature Flags (remote visibility control)

    private bool _featureBypassVisible = true;
    public bool FeatureBypassVisible { get => _featureBypassVisible; set => SetProperty(ref _featureBypassVisible, value); }

    private bool _featureStreamerModeVisible = true;
    public bool FeatureStreamerModeVisible { get => _featureStreamerModeVisible; set => SetProperty(ref _featureStreamerModeVisible, value); }

    private bool _featureFormBypassVisible = true;
    public bool FeatureFormBypassVisible { get => _featureFormBypassVisible; set => SetProperty(ref _featureFormBypassVisible, value); }

    private bool _featureStopNetworkVisible = true;
    public bool FeatureStopNetworkVisible { get => _featureStopNetworkVisible; set => SetProperty(ref _featureStopNetworkVisible, value); }

    /// <summary>Timer that periodically re-fetches feature flags from Firebase.</summary>
    private DispatcherTimer? _featureFlagTimer;

    #endregion

    // AI Mouse
    private bool _aiMouseEnabled;
    public bool AiMouseEnabled
    {
        get => _aiMouseEnabled;
        set
        {
            if (SetProperty(ref _aiMouseEnabled, value))
            {
                if (!value) { InputOptimizer.RestoreAIMouseDefaults(_sensitivityEngine); AiMouseIndex = -1; }
                AutoSaveNow();
            }
        }
    }

    private int _aiMouseIndex = -1;
    public int AiMouseIndex
    {
        get => _aiMouseIndex;
        set
        {
            if (SetProperty(ref _aiMouseIndex, value) && _aiMouseEnabled && value >= 0)
                ApplyAiMouse(value);
            AutoSaveNow();
        }
    }

    private bool _aiMouseClicking;
    public bool AiMouseClicking
    {
        get => _aiMouseClicking;
        set
        {
            if (SetProperty(ref _aiMouseClicking, value))
            {
                if (value) InputOptimizer.OptimizeClicking(_sensitivityEngine);
                else InputOptimizer.RestoreClicking(_sensitivityEngine);
                AutoSaveNow();
            }
        }
    }

    private bool _aiKeyboard;
    public bool AiKeyboard
    {
        get => _aiKeyboard;
        set
        {
            if (SetProperty(ref _aiKeyboard, value))
            {
                if (value) InputOptimizer.OptimizeKeyboard();
                else InputOptimizer.RestoreKeyboard();
                AutoSaveNow();
            }
        }
    }

    private bool _antiRecoil;
    public bool AntiRecoil
    {
        get => _antiRecoil;
        set
        {
            if (SetProperty(ref _antiRecoil, value))
            {
                if (value) InputOptimizer.EnableAntiRecoil(_sensitivityEngine);
                else InputOptimizer.DisableAntiRecoil(_sensitivityEngine);
                AutoSaveNow();
            }
        }
    }

    private bool _noAcceleration;
    public bool NoAcceleration
    {
        get => _noAcceleration;
        set
        {
            if (SetProperty(ref _noAcceleration, value))
            {
                if (value) InputOptimizer.DisableMouseAcceleration(_sensitivityEngine);
                else InputOptimizer.RestoreMouseAcceleration(_sensitivityEngine);
                AutoSaveNow();
            }
        }
    }

    #region Device & Emulator Settings

    // ── Mouse device selection ──
    private ObservableCollection<string> _mouseDevices = ["Default Mouse"];
    public ObservableCollection<string> MouseDevices { get => _mouseDevices; set => SetProperty(ref _mouseDevices, value); }

    private int _selectedMouseIndex;
    public int SelectedMouseIndex { get => _selectedMouseIndex; set => SetProperty(ref _selectedMouseIndex, value); }

    // ── Keyboard device selection ──
    private ObservableCollection<string> _keyboardDevices = ["Default Keyboard"];
    public ObservableCollection<string> KeyboardDevices { get => _keyboardDevices; set => SetProperty(ref _keyboardDevices, value); }

    private int _selectedKeyboardIndex;
    public int SelectedKeyboardIndex { get => _selectedKeyboardIndex; set => SetProperty(ref _selectedKeyboardIndex, value); }

    // ── Emulator selection ──
    public string[] SupportedEmulators => EmulatorService.GetSupportedEmulatorNames();

    private int _selectedEmulatorIndex;
    public int SelectedEmulatorIndex
    {
        get => _selectedEmulatorIndex;
        set { if (SetProperty(ref _selectedEmulatorIndex, value)) OnPropertyChanged(nameof(SelectedEmulatorName)); }
    }
    public string SelectedEmulatorName =>
        _selectedEmulatorIndex >= 0 && _selectedEmulatorIndex < SupportedEmulators.Length
            ? SupportedEmulators[_selectedEmulatorIndex]
            : "None";

    // ── Emulator monitoring status ──
    private string _emulatorStatus = "Not checked";
    public string EmulatorStatus { get => _emulatorStatus; set => SetProperty(ref _emulatorStatus, value); }

    private bool _isEmulatorBlocked;
    public bool IsEmulatorBlocked { get => _isEmulatorBlocked; set => SetProperty(ref _isEmulatorBlocked, value); }

    private bool _isEmulatorConnected;
    public bool IsEmulatorConnected { get => _isEmulatorConnected; set => SetProperty(ref _isEmulatorConnected, value); }

    // ── Emulator Info Display ──
    private string _emuInfoProcess = "—";
    public string EmuInfoProcess { get => _emuInfoProcess; set => SetProperty(ref _emuInfoProcess, value); }

    private string _emuInfoPID = "—";
    public string EmuInfoPID { get => _emuInfoPID; set => SetProperty(ref _emuInfoPID, value); }

    private string _emuInfoMemory = "—";
    public string EmuInfoMemory { get => _emuInfoMemory; set => SetProperty(ref _emuInfoMemory, value); }

    private string _emuInfoVersion = "—";
    public string EmuInfoVersion { get => _emuInfoVersion; set => SetProperty(ref _emuInfoVersion, value); }

    private string _emuInfoEngine = "—";
    public string EmuInfoEngine { get => _emuInfoEngine; set => SetProperty(ref _emuInfoEngine, value); }

    private string _emuInfoCPU = "—";
    public string EmuInfoCPU { get => _emuInfoCPU; set => SetProperty(ref _emuInfoCPU, value); }

    private string _emuInfoRAM = "—";
    public string EmuInfoRAM { get => _emuInfoRAM; set => SetProperty(ref _emuInfoRAM, value); }

    private string _emuInfoFPS = "—";
    public string EmuInfoFPS { get => _emuInfoFPS; set => SetProperty(ref _emuInfoFPS, value); }

    private string _emuInfoUptime = "—";
    public string EmuInfoUptime { get => _emuInfoUptime; set => SetProperty(ref _emuInfoUptime, value); }

    public ICommand OptimizeEmulatorConnectionCommand { get; private set; } = null!;

    // Timer for polling emulator processes
    private DispatcherTimer? _emulatorTimer;
    /// <summary>Tracks whether the emulator was connected in the previous tick so we can run a full detect on first connection.</summary>
    private bool _emulatorWasConnected;

    #endregion

    #region Events (for Window code-behind to handle)

    public event Action<bool>? StreamerModeChanged;
    public event Action<bool>? FormBypassChanged;
    public event Action<bool>? PinOnTopChanged;
    public event Action<bool>? StopNetworkChanged;
    /// <summary>Event raised when settings are saved (for window size persistence).</summary>
    public event Action? SaveSettingsRequested;
    /// <summary>Event raised when a loading overlay should be shown/hidden.</summary>
    public event Action<bool, string>? LoadingOverlayRequested;

    #endregion

    #region Commands

    public ICommand ActivateCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand AdvancedPlayCommand { get; }
    public ICommand AdvancedPauseCommand { get; }
    public ICommand AdvancedSaveCommand { get; }
    public ICommand AdvancedResetCommand { get; }
    public ICommand GoToAdvancedCommand { get; }
    public ICommand BackFromAdvancedCommand { get; }
    public ICommand ShowOptInfoCommand { get; }
    public ICommand CheckDriversCommand { get; }
    public ICommand RestartMouseDriverCommand { get; }
    public ICommand ShowDriverReportCommand { get; }
    public ICommand DetectDisplayCommand { get; }
    public ICommand DetectEmulatorResolutionCommand { get; }
    public ICommand OpenShortcutSettingsCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand InstallInterceptionCommand { get; }
    public ICommand UninstallInterceptionCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }

    /// <summary>Event raised when driver report popup should be shown (handled by Window code-behind).</summary>
    public event Action? ShowDriverReportPopupRequested;
    /// <summary>Event raised when shortcut settings dialog should be shown.</summary>
    public event Action? ShowShortcutSettingsRequested;

    public MainViewModel()
    {
        ActivateCommand = new RelayCommand(Activate);
        PauseCommand = new RelayCommand(Pause);
        SaveCommand = new RelayCommand(SaveSettings);
        ResetCommand = new RelayCommand(ResetDefaults);
        AdvancedPlayCommand = new RelayCommand(AdvancedPlay);
        AdvancedPauseCommand = new RelayCommand(AdvancedPause);
        AdvancedSaveCommand = new RelayCommand(SaveSettings);
        AdvancedResetCommand = new RelayCommand(ResetDefaults);
        GoToAdvancedCommand = new RelayCommand(() => SelectedTab = 1);
        BackFromAdvancedCommand = new RelayCommand(() => SelectedTab = 0);
        ShowOptInfoCommand = new RelayCommand(p => ShowOptInfo(p?.ToString() ?? ""));
        CheckDriversCommand = new RelayCommand(async () => await CheckDriverStatusAsync());
        RestartMouseDriverCommand = new RelayCommand(RestartMouseDriver);
        ShowDriverReportCommand = new RelayCommand(ShowDriverReport);
        DetectDisplayCommand = new RelayCommand(DetectDisplay);
        DetectEmulatorResolutionCommand = new RelayCommand(DetectEmulatorResolution);
        OpenShortcutSettingsCommand = new RelayCommand(() => ShowShortcutSettingsRequested?.Invoke());
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        InstallInterceptionCommand = new RelayCommand(async () => await InstallInterceptionDriverAsync());
        UninstallInterceptionCommand = new RelayCommand(async () => await UninstallInterceptionDriverAsync());
        ExportSettingsCommand = new RelayCommand(ExportSettings);
        ImportSettingsCommand = new RelayCommand(ImportSettings);
        RunSystemCleanerCommand = new RelayCommand(async () => await RunSystemCleanerAsync());
        RunPrivacyCleanCommand = new RelayCommand(async () => await RunPrivacyCleanAsync());
        OptimizeEmulatorConnectionCommand = new RelayCommand(async () => await OptimizeEmulatorAsync());

        LoadAllSettings();
        AutoDetectDisplayOnStartup();
        _ = CheckDriverStatusAsync();
        RefreshDevices();
        LoadDeviceSettings();
        StartEmulatorMonitor();
        _ = Task.Run(DetectSystemInfo);
        RefreshInterceptionStatus();

        // Firebase remote feature flags — fetch on startup & poll every 2 minutes
        _ = LoadFeatureFlagsAsync();
        StartFeatureFlagPolling();
    }

    #endregion

    #region Actions

    private async void Activate()
    {
        if (IsEmulatorBlocked)
        {
            Msg("Multiple emulators detected!\n\nLegitX V2 cannot activate while more than one emulator is running. Please close one emulator first.",
                "Emulator Conflict", MessageBoxImage.Warning);
            return;
        }

        if (!PersistAcrossApps)
        {
            Msg("Please enable 'Persist Across Apps' first!\n\nWithout it the sensitivity engine cannot stay active when you switch to your game. Go to Settings → Persist Across Apps and turn it ON.",
                "Persist Across Apps Required", MessageBoxImage.Warning);
            return;
        }

        await RunWithLoadingAsync("Installing mouse hook & activating engine…", () =>
        {
            SyncEngineSettings();
            _sensitivityEngine.Install();
            _sensitivityEngine.IsActive = true;
            _sensitivityEngine.AdvancedActive = true;
            Thread.Sleep(500); // let pipeline settle
        });

        // Sync both panels to active state
        IsActive = true;
        IsPaused = false;
        AdvancedActive = true;
        var mode = _sensitivityEngine.IsKernelMode ? "Kernel Mode" : "User Mode";
        Views.ToastNotification.Show("Engine Activated", $"Sensitivity pipeline running • {mode}", true);
    }

    private async void Pause()
    {
        await RunWithLoadingAsync("Uninstalling mouse hook…", () =>
        {
            _sensitivityEngine.IsActive = false;
            _sensitivityEngine.AdvancedActive = false;
            _sensitivityEngine.Uninstall();
            Thread.Sleep(300); // let hook release cleanly
        });

        // Sync both panels to paused state
        IsActive = false;
        IsPaused = true;
        AdvancedActive = false;
        Views.ToastNotification.Show("Engine Paused", "Sensitivity pipeline stopped", false);
    }

    /// <summary>Syncs all current ViewModel settings into the sensitivity engine.</summary>
    private void SyncEngineSettings()
    {
        PushMainSpeedToEngine();
        _sensitivityEngine.CurveType = (AccelCurveType)CurveTypeIndex;
        _sensitivityEngine.DisplayWidth = DisplayWidth;
        _sensitivityEngine.DisplayHeight = DisplayHeight;
        _sensitivityEngine.EmulatorWidth = EmulatorWidth;
        _sensitivityEngine.EmulatorHeight = EmulatorHeight;

        if (TryParseAdvanced(out var gs, out var sl, out var px, out var py, out var sx, out var sy, out var ac,
                out var rc, out var smooth, out var steady))
        {
            _sensitivityEngine.AdvGeneralSens = gs;
            _sensitivityEngine.AdvSensLimit = sl;
            _sensitivityEngine.AdvPreScaleX = px;
            _sensitivityEngine.AdvPreScaleY = py;
            _sensitivityEngine.AdvPostScaleX = sx;
            _sensitivityEngine.AdvPostScaleY = sy;
            _sensitivityEngine.AdvAcceleration = ac;
            _sensitivityEngine.AdvRecoilControl = rc;
            _sensitivityEngine.AdvSmoothStrength = smooth;
            _sensitivityEngine.AdvSteadyAim = steady;
        }
        _sensitivityEngine.AdvancedActive = AdvancedActive;

        // Sync AI / Extras engine flags
        if (_aiMouseEnabled && _aiMouseIndex >= 0)
            InputOptimizer.ApplyAIMouseProfile(_sensitivityEngine, _aiMouseIndex);
        else
            InputOptimizer.RestoreAIMouseDefaults(_sensitivityEngine);

        if (_antiRecoil)
            InputOptimizer.EnableAntiRecoil(_sensitivityEngine);
        else
            InputOptimizer.DisableAntiRecoil(_sensitivityEngine);

        _sensitivityEngine.NoAccelerationOverride = _noAcceleration;
        _sensitivityEngine.PersistAcrossApps = _persistAcrossApps;
        _sensitivityEngine.UserLockStrictness = (LockModeStrictness)Math.Clamp(UserLockStrictness, 0, 3);
        _sensitivityEngine.KernelLockStrictness = (LockModeStrictness)Math.Clamp(KernelLockStrictness, 0, 3);
        _sensitivityEngine.UserAccelX = TryParseUserDouble(UserAccelX, out double uax) ? Math.Clamp(uax, 0.50, 1.80) : 1.00;
        _sensitivityEngine.UserAccelY = TryParseUserDouble(UserAccelY, out double uay) ? Math.Clamp(uay, 0.50, 1.80) : 0.92;
        _sensitivityEngine.KernelAccelX = TryParseUserDouble(KernelAccelX, out double kax) ? Math.Clamp(kax, 0.50, 1.80) : 1.00;
        _sensitivityEngine.KernelAccelY = TryParseUserDouble(KernelAccelY, out double kay) ? Math.Clamp(kay, 0.50, 1.80) : 0.85;

        // Enforce kernel access restriction from license
        _sensitivityEngine.ForceUserMode = !_kernelAccessAllowed;

        if (_aiMouseClicking)
            InputOptimizer.OptimizeClicking(_sensitivityEngine);
        else
            InputOptimizer.RestoreClicking(_sensitivityEngine);
    }

    private void SaveSettings()
    {
        if (!TryParseAdvanced(out var gs, out var sl, out var px, out var py, out var sx, out var sy, out var ac,
                out var rc, out var smooth, out var steady))
        {
            Msg("Invalid advanced values. Please check your inputs.", "Error", MessageBoxImage.Error);
            return;
        }
        SettingsService.SaveMainSettings(
            TryParseUserDouble(MouseSensitivityText, out double ms) ? ms : 25.0,
            TryParseUserDouble(XAxisSensitivityText, out double xa) ? xa : 15.0,
            TryParseUserDouble(YAxisSensitivityText, out double ya) ? ya : 15.0,
            DisplayWidth, DisplayHeight, EmulatorWidth, EmulatorHeight, CurveTypeIndex);
        SettingsService.SaveAdvancedSettings(gs, sl, px, py, sx, sy, ac, rc, smooth, steady);
        SettingsService.SavePreference("UserLockStrictness", Math.Clamp(UserLockStrictness, 0, 3));
        SettingsService.SavePreference("KernelLockStrictness", Math.Clamp(KernelLockStrictness, 0, 3));
        SettingsService.SavePreference("UserAccelXx100",
            (int)Math.Round((TryParseUserDouble(UserAccelX, out double uax) ? Math.Clamp(uax, 0.50, 1.80) : 1.00) * 100.0));
        SettingsService.SavePreference("UserAccelYx100",
            (int)Math.Round((TryParseUserDouble(UserAccelY, out double uay) ? Math.Clamp(uay, 0.50, 1.80) : 0.92) * 100.0));
        SettingsService.SavePreference("KernelAccelXx100",
            (int)Math.Round((TryParseUserDouble(KernelAccelX, out double kax) ? Math.Clamp(kax, 0.50, 1.80) : 1.00) * 100.0));
        SettingsService.SavePreference("KernelAccelYx100",
            (int)Math.Round((TryParseUserDouble(KernelAccelY, out double kay) ? Math.Clamp(kay, 0.50, 1.80) : 0.85) * 100.0));
        SaveDeviceSettings();
        SyncEngineSettings();
        SaveSettingsRequested?.Invoke();
        Msg("All settings saved!\n\n• Main sensitivity settings\n• Advanced tuning parameters\n• PRO aim features\n• Device preferences", "Saved");
    }

    private void ResetDefaults()
    {
        if (IsActive)
        {
            Msg("Please pause the engine first before resetting.\n\nThe mouse hook must be stopped before changing values to prevent erratic behavior.",
                "Pause Required", MessageBoxImage.Warning);
            return;
        }

        // Reset main sensitivity
        MouseSensitivityText = "25.00";
        XAxisSensitivityText = "15.00";
        YAxisSensitivityText = "15.00";
        DisplayWidth = 1920; DisplayHeight = 1080;
        EmulatorWidth = 1280; EmulatorHeight = 720;
        // Reset advanced tuning
        GeneralSens = "1.00"; SensLimit = "10.00";
        PreScaleX = "1.00"; PreScaleY = "1.00";
        PostScaleX = "1.00"; PostScaleY = "1.00";
        Acceleration = "1.00";
        // Reset PRO aim features
        AdvRecoilControl = "0.00"; AdvSmoothStrength = "0.35";
        AdvSteadyAim = "0.00";
        UserLockStrictness = 1;
        KernelLockStrictness = 2;
        UserAccelX = "1.00"; UserAccelY = "0.92";
        KernelAccelX = "1.00"; KernelAccelY = "0.85";
        SyncEngineSettings();
        Msg("All settings reset to defaults.\n\n• Overall: 25  •  X/Y Axis: 15\n• Resolution: 1920×1080 → 1280×720\n• Advanced tuning: Factory\n• PRO aim features: Factory", "Reset");
    }

    private void ExportSettings()
    {
        // Make sure current values are saved to registry first
        if (TryParseAdvanced(out var gs, out var sl, out var px, out var py, out var sx, out var sy, out var ac,
                out var rc, out var smooth, out var steady))
        {
            SettingsService.SaveMainSettings(
            TryParseUserDouble(MouseSensitivityText, out double ms) ? ms : 25.0,
            TryParseUserDouble(XAxisSensitivityText, out double xa) ? xa : 15.0,
            TryParseUserDouble(YAxisSensitivityText, out double ya) ? ya : 15.0,
                DisplayWidth, DisplayHeight, EmulatorWidth, EmulatorHeight, CurveTypeIndex);
            SettingsService.SaveAdvancedSettings(gs, sl, px, py, sx, sy, ac, rc, smooth, steady);
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export LegitX V2 Settings",
            Filter = "LegitX Settings (*.legitx)|*.legitx|JSON Files (*.json)|*.json",
            DefaultExt = ".legitx",
            FileName = $"LegitX_Settings_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var json = SettingsService.ExportSettingsToJson();
                File.WriteAllText(dlg.FileName, json);
                Views.ToastNotification.Show("Settings Exported", $"Saved to {Path.GetFileName(dlg.FileName)}", true);
            }
            catch (Exception ex)
            {
                Msg($"Export failed: {ex.Message}", "Error", MessageBoxImage.Error);
            }
        }
    }

    private void ImportSettings()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import LegitX V2 Settings",
            Filter = "LegitX Settings (*.legitx)|*.legitx|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".legitx"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var error = SettingsService.ImportSettingsFromJson(json);
                if (error != null)
                {
                    Msg(error, "Import Error", MessageBoxImage.Warning);
                    return;
                }

                // Reload all values into the ViewModel from registry
                LoadAllSettings();
                // Notify all UI bindings
                OnPropertyChanged(nameof(MouseSensitivityText));
                OnPropertyChanged(nameof(XAxisSensitivityText));
                OnPropertyChanged(nameof(YAxisSensitivityText));
                OnPropertyChanged(nameof(DisplayWidth));
                OnPropertyChanged(nameof(DisplayWidthDisplay));
                OnPropertyChanged(nameof(DisplayHeight));
                OnPropertyChanged(nameof(DisplayHeightDisplay));
                OnPropertyChanged(nameof(EmulatorWidth));
                OnPropertyChanged(nameof(EmulatorWidthDisplay));
                OnPropertyChanged(nameof(EmulatorHeight));
                OnPropertyChanged(nameof(EmulatorHeightDisplay));
                OnPropertyChanged(nameof(GeneralSens));
                OnPropertyChanged(nameof(SensLimit));
                OnPropertyChanged(nameof(PreScaleX));
                OnPropertyChanged(nameof(PreScaleY));
                OnPropertyChanged(nameof(PostScaleX));
                OnPropertyChanged(nameof(PostScaleY));
                OnPropertyChanged(nameof(Acceleration));
                OnPropertyChanged(nameof(AdvRecoilControl));
                OnPropertyChanged(nameof(AdvSmoothStrength));
                OnPropertyChanged(nameof(AdvSteadyAim));
                OnPropertyChanged(nameof(UserLockStrictness));
                OnPropertyChanged(nameof(KernelLockStrictness));
                OnPropertyChanged(nameof(UserAccelX));
                OnPropertyChanged(nameof(UserAccelY));
                OnPropertyChanged(nameof(KernelAccelX));
                OnPropertyChanged(nameof(KernelAccelY));

                Views.ToastNotification.Show("Settings Imported", $"Loaded from {Path.GetFileName(dlg.FileName)}", true);
            }
            catch (Exception ex)
            {
                Msg($"Import failed: {ex.Message}", "Error", MessageBoxImage.Error);
            }
        }
    }

    private async void AdvancedPlay()
    {
        if (IsEmulatorBlocked)
        {
            Msg("Multiple emulators detected!\n\nLegitX V2 cannot activate while more than one emulator is running. Please close one emulator first.",
                "Emulator Conflict", MessageBoxImage.Warning);
            return;
        }

        if (!PersistAcrossApps)
        {
            Msg("Please enable 'Persist Across Apps' first!\n\nWithout it the sensitivity engine cannot stay active when you switch to your game. Go to Settings → Persist Across Apps and turn it ON.",
                "Persist Across Apps Required", MessageBoxImage.Warning);
            return;
        }

        await RunWithLoadingAsync("Installing mouse hook & activating engine…", () =>
        {
            SyncEngineSettings();
            if (!_sensitivityEngine.IsActive)
            {
                _sensitivityEngine.Install();
                _sensitivityEngine.IsActive = true;
            }
            _sensitivityEngine.AdvancedActive = true;
            Thread.Sleep(500); // let pipeline settle
        });

        // Sync both panels to active state
        IsActive = true;
        IsPaused = false;
        AdvancedActive = true;
        var mode = _sensitivityEngine.IsKernelMode ? "Kernel Mode" : "User Mode";
        Views.ToastNotification.Show("Engine Activated", $"Sensitivity pipeline running • {mode}", true);
    }

    private async void AdvancedPause()
    {
        await RunWithLoadingAsync("Uninstalling mouse hook…", () =>
        {
            _sensitivityEngine.IsActive = false;
            _sensitivityEngine.AdvancedActive = false;
            _sensitivityEngine.Uninstall();
            Thread.Sleep(300); // let hook release cleanly
        });

        // Sync both panels to paused state
        IsActive = false;
        IsPaused = true;
        AdvancedActive = false;
        Views.ToastNotification.Show("Engine Paused", "Sensitivity pipeline stopped", false);
    }

    private void LoadAllSettings()
    {
        try
        {
            var (ms, xa, ya, dw, dh, ew, eh, ct) = SettingsService.LoadMainSettings();
            if (ms >= 0) _mouseSensitivityText = Math.Clamp(ms, 0.01, 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            if (xa >= 0) _xAxisSensitivityText = Math.Clamp(xa, 0.01, 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            if (ya >= 0) _yAxisSensitivityText = Math.Clamp(ya, 0.01, 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            _displayWidth = dw; _displayHeight = dh; _emulatorWidth = ew; _emulatorHeight = eh;
            _curveTypeIndex = Math.Clamp(ct, 0, 5);

            var (gs, sl, px, py, sx, sy, ac, rc, smooth, steady) = SettingsService.LoadAdvancedSettings();
            _generalSens = gs.ToString("0.00"); _sensLimit = sl.ToString("0.00");
            _preScaleX = px.ToString("0.00"); _preScaleY = py.ToString("0.00");
            _postScaleX = sx.ToString("0.00"); _postScaleY = sy.ToString("0.00");
            _acceleration = ac.ToString("0.00");
            _advRecoilControl = rc.ToString("0.00");
            _advSmoothStrength = smooth.ToString("0.00"); _advSteadyAim = steady.ToString("0.00");
            _userLockStrictness = Math.Clamp(SettingsService.LoadPreference("UserLockStrictness", 1), 0, 3);
            _kernelLockStrictness = Math.Clamp(SettingsService.LoadPreference("KernelLockStrictness", 2), 0, 3);
            _userAccelX = (SettingsService.LoadPreference("UserAccelXx100", 100) / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            _userAccelY = (SettingsService.LoadPreference("UserAccelYx100", 92) / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            _kernelAccelX = (SettingsService.LoadPreference("KernelAccelXx100", 100) / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            _kernelAccelY = (SettingsService.LoadPreference("KernelAccelYx100", 85) / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

            // Load preferences (set backing field directly to avoid triggering auto-save during load)
            _autoSaveEnabled = SettingsService.LoadPreference("AutoSave", 0) == 1;
            _alwaysOnTop = SettingsService.LoadPreference("AlwaysOnTop", 1) == 1; // default ON
            _autoDetectEmulatorRes = SettingsService.LoadPreference("AutoDetectEmuRes", 1) == 1; // default ON
            _liveResolutionSync = SettingsService.LoadPreference("LiveResSync", 1) == 1; // default ON

            // Sync loaded settings to engine
            SyncEngineSettings();
            RefreshCurvePoints();
            OnPropertyChanged(nameof(MouseSensitivityText));
            OnPropertyChanged(nameof(XAxisSensitivityText));
            OnPropertyChanged(nameof(YAxisSensitivityText));
        }
        catch
        {
            // Use defaults if registry read fails
        }
    }

    #endregion

    #region Optimization Handlers

    /// <summary>Helper: run heavy work on background thread with loading overlay.</summary>
    private async Task RunWithLoadingAsync(string loadingText, Action work)
    {
        LoadingOverlayRequested?.Invoke(true, loadingText);
        try
        {
            await Task.Run(work);
        }
        finally
        {
            LoadingOverlayRequested?.Invoke(false, "");
        }
    }

    private async Task ApplyGameModeAsync()
    {
        try
        {
            await RunWithLoadingAsync("Enabling Game Mode…", () =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
                key?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                key?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);

                using var gpuKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                gpuKey?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);

                using var dvrKey = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                dvrKey?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);

                using var captureKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
                captureKey?.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
            });
            Msg("Game Mode enabled!\n\n• Windows Game Mode: ON\n• GPU Hardware Scheduling: ON\n• Game DVR/Recording: OFF (reduces overhead)\n\nRestart recommended for GPU scheduling.", "Game Mode");
        }
        catch { Msg("Could not apply Game Mode. Try running as Administrator.", "Error", MessageBoxImage.Warning); OptGameMode = false; }
    }

    private async Task ApplyPowerPlanAsync()
    {
        try
        {
            await RunWithLoadingAsync("Optimizing Power Plan…", () =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powercfg", "/duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(5000);

                var psi2 = new System.Diagnostics.ProcessStartInfo("powercfg", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
                { CreateNoWindow = true, UseShellExecute = false };
                System.Diagnostics.Process.Start(psi2)?.WaitForExit(3000);

                using var usbKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\USB");
                usbKey?.SetValue("DisableSelectiveSuspend", 1, RegistryValueKind.DWord);
            });
            Msg("Power Plan optimized!\n\n• High Performance plan activated\n• USB Selective Suspend: OFF\n• CPU throttling minimized", "Power Plan");
        }
        catch { Msg("Could not change power plan. Try running as Administrator.", "Error", MessageBoxImage.Warning); OptPowerPlan = false; }
    }

    private async Task ApplyAudioLatencyAsync()
    {
        try
        {
            await RunWithLoadingAsync("Optimizing Audio Latency…", () =>
            {
                using var audioKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio");
                audioKey?.SetValue("LowLatencyMode", 1, RegistryValueKind.DWord);

                using var mmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio");
                mmKey?.SetValue("DisableProtectedAudioDG", 1, RegistryValueKind.DWord);

                using var mmcssKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                mmcssKey?.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                mmcssKey?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);

                using var gameKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
                gameKey?.SetValue("Affinity", 0, RegistryValueKind.DWord);
                gameKey?.SetValue("Background Only", "False", RegistryValueKind.String);
                gameKey?.SetValue("Clock Rate", 10000, RegistryValueKind.DWord);
                gameKey?.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                gameKey?.SetValue("Priority", 6, RegistryValueKind.DWord);
                gameKey?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                gameKey?.SetValue("SFIO Priority", "High", RegistryValueKind.String);
            });
            Msg("Audio Latency optimized!\n\n• Low latency mode: ON\n• System responsiveness: Maximum\n• Game audio priority: High\n• Network throttling: Disabled", "Audio Latency");
        }
        catch { Msg("Could not optimize audio latency. Try running as Administrator.", "Error", MessageBoxImage.Warning); OptAudioLatency = false; }
    }

    private async Task ApplyDpiOverrideAsync()
    {
        try
        {
            await RunWithLoadingAsync("Applying DPI Override…", () =>
            {
                using var dpiKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                var emulators = new[] { "HD-Player.exe", "NoxPlayer.exe", "MEmu.exe", "LDPlayer.exe", "Bluestacks.exe" };
                foreach (var emu in emulators)
                    dpiKey?.SetValue(emu, "~ HIGHDPIAWARE", RegistryValueKind.String);

                using var dwmKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                dwmKey?.SetValue("DpiScalingVer", 0x00001018, RegistryValueKind.DWord);
                dwmKey?.SetValue("Win8DpiScaling", 1, RegistryValueKind.DWord);
            });
            Msg("DPI Override applied!\n\n• Emulators set to High DPI Aware\n• Per-monitor DPI scaling enabled\n• Reduces blurriness in emulators", "DPI Override");
        }
        catch { Msg("Could not apply DPI override.", "Error", MessageBoxImage.Warning); OptDpiOverride = false; }
    }

    private async Task ApplyControllerOptAsync()
    {
        try
        {
            await RunWithLoadingAsync("Optimizing Controller Input…", () =>
            {
                using var hidKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters");
                hidKey?.SetValue("EnhancedPowerManagementEnabled", 0, RegistryValueKind.DWord);

                using var usbKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\usbhub\Parameters");
                usbKey?.SetValue("DisableSelectiveSuspend", 1, RegistryValueKind.DWord);

                using var xinputKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\xboxgip\Parameters");
                xinputKey?.SetValue("ThreadPriority", 31, RegistryValueKind.DWord);

                using var btKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\BTHUSB\Parameters");
                btKey?.SetValue("DisableSelectiveSuspend", 1, RegistryValueKind.DWord);
            });
            Msg("Controller Input optimized!\n\n• USB power saving: OFF\n• HID polling: Enhanced\n• XInput priority: Maximum\n• Bluetooth suspend: OFF", "Controller");
        }
        catch { Msg("Could not optimize controller input. Try running as Administrator.", "Error", MessageBoxImage.Warning); OptController = false; }
    }

    /// <summary>
    /// Applies every known Windows mouse/cursor registry tweak for optimal gaming aim.
    /// Disables acceleration, EPP, smoothing curves, sets neutral speed, optimizes
    /// pointer precision, fixes high-DPI scaling, lowers USB mouse polling latency,
    /// and tunes the mouse class driver for raw 1:1 input.
    /// </summary>
    private async Task ApplyMouseRegistryOptAsync()
    {
        try
        {
            await RunWithLoadingAsync("Optimizing Mouse Registry…", () =>
            {
                // ── 1. Disable "Enhance Pointer Precision" (EPP / mouse acceleration) ──
                var mouseParams = new int[] { 0, 0, 0 }; // threshold1=0, threshold2=0, acceleration=0
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(mouseParams, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    SystemParametersInfo(SPI_SETMOUSE, 0, pin.AddrOfPinnedObject(), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }
                finally { pin.Free(); }

                // ── 2. Set Windows mouse speed to neutral (10 = no scaling) ──
                SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)10, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                // ── 3. Registry: Control Panel\Mouse — full optimization ──
                using (var mouseKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
                {
                    if (mouseKey != null)
                    {
                        // Disable acceleration
                        mouseKey.SetValue("MouseSpeed", "0");
                        mouseKey.SetValue("MouseThreshold1", "0");
                        mouseKey.SetValue("MouseThreshold2", "0");

                        // Neutral sensitivity (10 = 1:1 mapping)
                        mouseKey.SetValue("MouseSensitivity", "10");

                        // Fastest double-click for gaming
                        mouseKey.SetValue("DoubleClickSpeed", "200");

                        // Disable mouse trails (causes visual lag)
                        mouseKey.SetValue("MouseTrails", "0");

                        // Disable snap-to-default-button (jumps cursor to dialog buttons)
                        mouseKey.SetValue("SnapToDefaultButton", "0");

                        // Disable hover time delay
                        mouseKey.SetValue("MouseHoverTime", "0");

                        // Flat SmoothMouse curves — perfect 1:1 linear response
                        // These replace the Windows default curved acceleration tables
                        byte[] flatXCurve = new byte[] {
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x80, 0x99, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x40, 0x66, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x33, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00
                        };
                        byte[] flatYCurve = new byte[] {
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00
                        };
                        mouseKey.SetValue("SmoothMouseXCurve", flatXCurve, RegistryValueKind.Binary);
                        mouseKey.SetValue("SmoothMouseYCurve", flatYCurve, RegistryValueKind.Binary);
                    }
                }

                // ── 4. Disable pointer shadow & visual effects that add input lag ──
                using (var desktopKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                {
                    if (desktopKey != null)
                    {
                        desktopKey.SetValue("CursorShadow", "0");
                        // Disable menu show delay (faster UI response)
                        desktopKey.SetValue("MenuShowDelay", "0");
                    }
                }

                // ── 5. Mouse class driver: buffer & sample rate optimization ──
                using (var classKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\mouclass\Parameters"))
                {
                    if (classKey != null)
                    {
                        // Maximum sample rate the class driver will handle
                        classKey.SetValue("SampleRate", 200, RegistryValueKind.DWord);
                        // Mouse data queue size (larger = fewer dropped packets at high poll rates)
                        classKey.SetValue("MouseDataQueueSize", 100, RegistryValueKind.DWord);
                    }
                }

                // ── 6. USB mouse: disable selective suspend & optimize polling ──
                using (var usbMouseKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\mouhid\Parameters"))
                {
                    if (usbMouseKey != null)
                    {
                        usbMouseKey.SetValue("TreatAbsoluteAsRelative", 0, RegistryValueKind.DWord);
                    }
                }

                // ── 7. Disable Aero Shake & Snap Assist (prevent accidental window flings) ──
                using (var explorerKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (explorerKey != null)
                    {
                        explorerKey.SetValue("DisallowShaking", 1, RegistryValueKind.DWord);
                    }
                }

                // ── 8. MMCSS gaming task priority (cursor responsiveness in games) ──
                using (var mmcssKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    if (mmcssKey != null)
                    {
                        mmcssKey.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                        mmcssKey.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    }
                }
                using (var gameTaskKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                {
                    if (gameTaskKey != null)
                    {
                        gameTaskKey.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                        gameTaskKey.SetValue("Priority", 6, RegistryValueKind.DWord);
                        gameTaskKey.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                        gameTaskKey.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                    }
                }

                // ── 9. Disable touch feedback visuals (adds latency on touch/pen screens) ──
                using (var touchKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors"))
                {
                    if (touchKey != null)
                    {
                        touchKey.SetValue("ContactVisualization", 0, RegistryValueKind.DWord);
                        touchKey.SetValue("GestureVisualization", 0, RegistryValueKind.DWord);
                    }
                }
            });
            Msg("Mouse Registry Optimized for Gaming!\n\n" +
                "✓ Enhance Pointer Precision: OFF\n" +
                "✓ Mouse acceleration: Disabled\n" +
                "✓ SmoothMouse curves: Flat 1:1 linear\n" +
                "✓ Mouse speed: Neutral (10)\n" +
                "✓ Mouse trails/shadow: OFF\n" +
                "✓ Snap-to-button: OFF\n" +
                "✓ Mouse class driver: Optimized\n" +
                "✓ MMCSS gaming priority: Maximum\n" +
                "✓ Touch feedback visuals: OFF\n\n" +
                "⚠ Restart recommended for full effect.", "Mouse Optimizer");
        }
        catch { Msg("Could not optimize mouse registry. Try running as Administrator.", "Error", MessageBoxImage.Warning); OptMouseRegistry = false; }
    }

    /// <summary>Restores mouse registry to Windows defaults when toggle is turned OFF.</summary>
    private async Task RestoreMouseRegistryAsync()
    {
        try
        {
            await RunWithLoadingAsync("Restoring Mouse Defaults…", () =>
            {
                // Restore EPP (Enhance Pointer Precision ON = Windows default)
                var mouseParams = new int[] { 6, 10, 1 };
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(mouseParams, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    SystemParametersInfo(SPI_SETMOUSE, 0, pin.AddrOfPinnedObject(), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }
                finally { pin.Free(); }

                SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)10, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                using (var mouseKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
                {
                    if (mouseKey != null)
                    {
                        mouseKey.SetValue("MouseSpeed", "1");
                        mouseKey.SetValue("MouseThreshold1", "6");
                        mouseKey.SetValue("MouseThreshold2", "10");
                        mouseKey.SetValue("MouseSensitivity", "10");
                        mouseKey.SetValue("DoubleClickSpeed", "500");
                        mouseKey.SetValue("MouseTrails", "0");
                        mouseKey.SetValue("SnapToDefaultButton", "0");
                        mouseKey.SetValue("MouseHoverTime", "400");

                        // Default Windows acceleration curves
                        byte[] defaultXCurve = new byte[] {
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x15, 0x6E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x29, 0xDC, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00
                        };
                        byte[] defaultYCurve = new byte[] {
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0xFD, 0x11, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0x24, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0xFC, 0x12, 0x00, 0x00, 0x00, 0x00, 0x00,
                            0x00, 0xC0, 0xBB, 0x01, 0x00, 0x00, 0x00, 0x00
                        };
                        mouseKey.SetValue("SmoothMouseXCurve", defaultXCurve, RegistryValueKind.Binary);
                        mouseKey.SetValue("SmoothMouseYCurve", defaultYCurve, RegistryValueKind.Binary);
                    }
                }

                using (var desktopKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                {
                    if (desktopKey != null)
                    {
                        desktopKey.SetValue("MenuShowDelay", "400");
                    }
                }
            });
            Msg("Mouse settings restored to Windows defaults.\n\nRestart recommended.", "Mouse Optimizer");
        }
        catch { Msg("Could not restore mouse defaults.", "Error", MessageBoxImage.Warning); }
    }

    private async Task RunSystemCleanerAsync()
    {
        if (!Confirm("This will clean:\n\n• Temp files\n• DNS cache\n• Prefetch cache\n• Thumbnail cache\n• Windows temp\n\nContinue?"))
            return;

        Msg("Cleaning system... Please wait.", "System Cleaner");

        await Task.Run(() =>
        {
            try
            {
                // Clear temp folders
                ClearFolder(Path.GetTempPath());
                ClearFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));

                // Clear prefetch
                ClearFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"));

                // Clear thumbnail cache
                ClearFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Explorer"));

                // Flush DNS
                var psi = new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
                { CreateNoWindow = true, UseShellExecute = false };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);

                // Clear Windows Store cache
                var psi2 = new System.Diagnostics.ProcessStartInfo("wsreset.exe")
                { CreateNoWindow = true, UseShellExecute = false };
                try { System.Diagnostics.Process.Start(psi2)?.WaitForExit(5000); } catch { }
            }
            catch { }
        });

        Msg("System cleaned!\n\n✓ Temp files cleared\n✓ DNS cache flushed\n✓ Prefetch cleaned\n✓ Thumbnail cache cleared", "System Cleaner");
    }

    private async Task RunPrivacyCleanAsync()
    {
        if (!Confirm("Privacy Mode will:\n\n• Clear Windows event logs\n• Clear recent files history\n• Disable some telemetry\n• Clear clipboard\n\nContinue?"))
            return;

        await Task.Run(() =>
        {
            try
            {
                // Clear event logs
                var psi = new System.Diagnostics.ProcessStartInfo("wevtutil", "cl Application")
                { CreateNoWindow = true, UseShellExecute = false };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);

                var psi2 = new System.Diagnostics.ProcessStartInfo("wevtutil", "cl System")
                { CreateNoWindow = true, UseShellExecute = false };
                System.Diagnostics.Process.Start(psi2)?.WaitForExit(3000);

                var psi3 = new System.Diagnostics.ProcessStartInfo("wevtutil", "cl Security")
                { CreateNoWindow = true, UseShellExecute = false };
                System.Diagnostics.Process.Start(psi3)?.WaitForExit(3000);

                // Disable telemetry
                using var telKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                telKey?.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);

                // Clear recent files
                ClearFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Recent)));

                // Disable Activity History
                using var actKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
                actKey?.SetValue("EnableActivityFeed", 0, RegistryValueKind.DWord);
                actKey?.SetValue("PublishUserActivities", 0, RegistryValueKind.DWord);
                actKey?.SetValue("UploadUserActivities", 0, RegistryValueKind.DWord);
            }
            catch { }
        });

        // Clear clipboard on UI thread
        try { System.Windows.Clipboard.Clear(); } catch { }

        Msg("Privacy cleaned!\n\n✓ Event logs cleared\n✓ Recent files removed\n✓ Telemetry reduced\n✓ Activity history disabled\n✓ Clipboard cleared", "Privacy Mode");
    }

    private static void ClearFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.GetFiles(path))
                try { File.Delete(f); } catch { }
            foreach (var d in Directory.GetDirectories(path))
                try { Directory.Delete(d, true); } catch { }
        }
        catch { }
    }

    private void DetectSystemInfo()
    {
        try
        {
            // CPU
            using var cpuSearch = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in cpuSearch.Get())
            {
                SysInfoCpu = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                break;
            }

            // RAM
            using var ramSearch = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in ramSearch.Get())
            {
                if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out var bytes))
                    SysInfoRam = $"{bytes / (1024 * 1024 * 1024.0):F1} GB";
                break;
            }

            // GPU
            using var gpuSearch = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in gpuSearch.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(name) && !name.Contains("Microsoft Basic"))
                { SysInfoGpu = name.Trim(); break; }
            }

            // OS
            SysInfoOs = $"{Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
        }
        catch
        {
            SysInfoCpu = "Unknown"; SysInfoRam = "Unknown"; SysInfoGpu = "Unknown"; SysInfoOs = "Unknown";
        }
    }

    private async Task CheckDriverStatusAsync()
    {
        IsDriverLoading = true;
        DriverStatus = "Scanning drivers...";

        try
        {
            bool healthy = false;
            await Task.Run(() =>
            {
                healthy = DriverService.AreEssentialDriversHealthy();
            });

            DriversHealthy = healthy;
            DriverStatus = healthy
                ? "All essential drivers are healthy"
                : "Some drivers may need attention";
        }
        catch
        {
            DriverStatus = "Unable to query driver status";
            DriversHealthy = false;
        }
        finally
        {
            IsDriverLoading = false;
        }
    }

    private void RestartMouseDriver()
    {
        if (!Confirm("This will briefly disable and re-enable your mouse driver. Continue?"))
            return;

        bool success = DriverService.RestartMouseDriver();
        if (success)
        {
            Msg("Mouse driver restarted successfully!", "Success");
            _ = CheckDriverStatusAsync();
        }
        else
            Msg("Could not restart mouse driver. Try running as Administrator.", "Error", MessageBoxImage.Warning);
    }

    private void ShowDriverReport()
    {
        ShowDriverReportPopupRequested?.Invoke();
    }

    private void ApplyResetMouseDefaults()
    {
        if (!Confirm("This will reset ALL mouse settings to Windows factory defaults.\n\n" +
                      "• Mouse sensitivity, speed, thresholds\n" +
                      "• SmoothMouse curves\n" +
                      "• Double-click speed\n" +
                      "• mouclass/kbdclass parameters\n" +
                      "• Any extra non-default registry values will be removed\n\n" +
                      "Continue?"))
        {
            _resetMouseDefaults = false;
            OnPropertyChanged(nameof(ResetMouseDefaults));
            return;
        }

        try
        {
            InputOptimizer.ResetAllToFactory(_sensitivityEngine);

            // Reset all related UI toggles
            _noAcceleration = false; OnPropertyChanged(nameof(NoAcceleration));
            _antiRecoil = false; OnPropertyChanged(nameof(AntiRecoil));
            _aiMouseEnabled = false; OnPropertyChanged(nameof(AiMouseEnabled));
            _aiMouseClicking = false; OnPropertyChanged(nameof(AiMouseClicking));
            _aiMouseIndex = -1; OnPropertyChanged(nameof(AiMouseIndex));

            Msg("All mouse settings have been reset to Windows factory defaults!\n\nA restart is recommended for all changes to take full effect.",
                "Mouse Reset Complete");
        }
        catch (Exception ex)
        {
            Msg($"Error resetting mouse: {ex.Message}\n\nTry running as Administrator.", "Error", MessageBoxImage.Error);
        }

        // Auto-uncheck after applying
        _resetMouseDefaults = false;
        OnPropertyChanged(nameof(ResetMouseDefaults));
    }

    private void AutoDetectDisplayOnStartup()
    {
        try
        {
            var (w, h) = DisplayService.GetPrimaryResolution();
            // Only auto-set if user hasn't manually saved custom values
            // (i.e., if still at the old defaults of 1920x1080)
            if (_displayWidth == 1920 && _displayHeight == 1080)
            {
                DisplayWidth = w;
                DisplayHeight = h;
            }
        }
        catch { /* Silently use defaults */ }
    }

    private void DetectDisplay()
    {
        try
        {
            var (w, h) = DisplayService.AutoDetectResolution();
            DisplayWidth = w;
            DisplayHeight = h;
            Msg($"Display resolution set to {w} × {h}", "Display Detected");
        }
        catch (Exception ex)
        {
            Msg($"Could not detect display: {ex.Message}", "Error", MessageBoxImage.Warning);
        }
    }

    private void DetectEmulatorResolution()
    {
        try
        {
            var info = EmulatorService.DetectEmulatorResolution();
            EmulatorWidth = info.Width;
            EmulatorHeight = info.Height;
            EmulatorResolutionSource = info.Source;

            // Build a detailed status message
            string details = $"Emulator resolution set to {info.Width} × {info.Height}\n" +
                             $"Source: {info.Source}";

            if (info.LiveClientWidth > 0 && info.LiveClientHeight > 0)
                details += $"\nLive window: {info.LiveClientWidth} × {info.LiveClientHeight}";

            if (info.MonitorWidth > 0 && info.MonitorHeight > 0)
                details += $"\nMonitor: {info.MonitorWidth} × {info.MonitorHeight}";

            if (info.ScreenCoverage > 0)
                details += $"\nScreen coverage: {info.ScreenCoverage:P0}";

            if (info.IsFullscreen || info.IsBorderless)
            {
                details += info.IsBorderless ? "\n\n✓ Borderless fullscreen detected" : "\n\n✓ Fullscreen / maximized detected";
                IsEmulatorWindowed = false;
                EmulatorResolutionStatus = "";
            }
            else if (info.ScreenCoverage > 0 && info.ScreenCoverage < 0.85)
            {
                details += $"\n\n⚠ Emulator is WINDOWED (only {info.ScreenCoverage:P0} of screen).\n" +
                           "For best accuracy, run your emulator in fullscreen.";
                IsEmulatorWindowed = true;
                EmulatorResolutionStatus = $"⚠ Windowed mode ({info.ScreenCoverage:P0} coverage) — fullscreen recommended";
            }
            else
            {
                IsEmulatorWindowed = false;
                EmulatorResolutionStatus = "";
            }

            Msg(details, "Emulator Resolution Detected");
        }
        catch (Exception ex)
        {
            Msg($"Could not detect emulator resolution: {ex.Message}", "Error", MessageBoxImage.Warning);
        }
    }

    private void ExecuteBypass(int index)
    {
        if (!_bypassToggle || index < 0) return;
        try
        {
            switch (index)
            {
                case 0: // Complete Bypass
                    RegistryOptimizer.RunCompleteBypass();
                    Msg("Complete Bypass applied!", "Success");
                    break;
                case 1: // Journal Reset
                    RegistryOptimizer.RunSilentCommand("fsutil usn deletejournal /d C:");
                    Msg("Journal Reset completed!", "Success");
                    break;
                case 2: // Destruct
                    RegistryOptimizer.SelfDestruct();
                    break;
                case 3: // Adv String Bypass
                    RegistryOptimizer.RunAdvStringBypass();
                    Msg("Advanced String Bypass completed!", "Success");
                    break;
                case 4: // String Clearance
                    RegistryOptimizer.ClearWindowsCache();
                    RegistryOptimizer.RunLogKiller();
                    RegistryOptimizer.RunStringsCleaner();
                    Msg("String Clearance completed!", "Success");
                    break;
                case 5: // Standard
                    RegistryOptimizer.RunCleanPCAndEmulator();
                    Msg("Standard cleanup completed!", "Success");
                    break;
            }
        }
        catch (Exception ex) { Msg($"Error: {ex.Message}", "Error", MessageBoxImage.Error); }
    }

    private void ApplyAiMouse(int index)
    {
        InputOptimizer.ApplyAIMouseProfile(_sensitivityEngine, index);
    }

    private void ShowOptInfo(string type)
    {
        string msg = type switch
        {
            _ => "Before applying optimizers, consult @subhoxx for your PC compatibility."
        };
        Msg(msg, "Compatibility Info");
    }

    #endregion

    #region Device & Emulator Management

    private void RefreshDevices()
    {
        // Populate mouse devices
        var mice = EmulatorService.GetMouseDevices();
        MouseDevices.Clear();
        foreach (var m in mice)
            MouseDevices.Add(m.Name);

        if (MouseDevices.Count > 0 && SelectedMouseIndex < 0)
            SelectedMouseIndex = 0;

        // Populate keyboard devices
        var kbs = EmulatorService.GetKeyboardDevices();
        KeyboardDevices.Clear();
        foreach (var k in kbs)
            KeyboardDevices.Add(k.Name);

        if (KeyboardDevices.Count > 0 && SelectedKeyboardIndex < 0)
            SelectedKeyboardIndex = 0;
    }

    private void LoadDeviceSettings()
    {
        var (mouseIdx, kbIdx, emuIdx) = SettingsService.LoadDeviceSettings();
        if (mouseIdx >= 0 && mouseIdx < MouseDevices.Count) _selectedMouseIndex = mouseIdx;
        if (kbIdx >= 0 && kbIdx < KeyboardDevices.Count) _selectedKeyboardIndex = kbIdx;
        if (emuIdx >= 0 && emuIdx < SupportedEmulators.Length) _selectedEmulatorIndex = emuIdx;
        OnPropertyChanged(nameof(SelectedMouseIndex));
        OnPropertyChanged(nameof(SelectedKeyboardIndex));
        OnPropertyChanged(nameof(SelectedEmulatorIndex));
        OnPropertyChanged(nameof(SelectedEmulatorName));
    }

    private void SaveDeviceSettings()
    {
        SettingsService.SaveDeviceSettings(SelectedMouseIndex, SelectedKeyboardIndex, SelectedEmulatorIndex);
    }

    private void StartEmulatorMonitor()
    {
        _emulatorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _emulatorTimer.Tick += (_, _) => CheckEmulatorStatus();
        _emulatorTimer.Start();
        CheckEmulatorStatus();
    }

    private void CheckEmulatorStatus()
    {
        try
        {
            var running = EmulatorService.GetRunningEmulators();
            int instanceCount = running.Count;
            var distinctNames = running.Select(r => r.DisplayName).Distinct().ToList();

            if (instanceCount == 0)
            {
                EmulatorStatus = "No emulator detected — waiting for HD-Player.exe";
                IsEmulatorBlocked = false;
                IsEmulatorConnected = false;
                IsEmulatorWindowed = false;
                EmulatorResolutionStatus = "";
                EmulatorResolutionSource = "";
                _emulatorWasConnected = false;
                ClearEmulatorInfo();
            }
            else if (instanceCount == 1)
            {
                EmulatorStatus = $"✓ Connected to {distinctNames[0]}";
                IsEmulatorBlocked = false;
                IsEmulatorConnected = true;
                UpdateEmulatorInfo();

                // ── First connection: run full auto-detect (if enabled) ──
                if (!_emulatorWasConnected && _autoDetectEmulatorRes)
                {
                    _emulatorWasConnected = true;
                    try
                    {
                        var info = EmulatorService.DetectEmulatorResolution();
                        if (info.Source != "Default")
                        {
                            EmulatorWidth = info.Width;
                            EmulatorHeight = info.Height;
                            EmulatorResolutionSource = $"{info.Source} (auto-detected)";

                            if (info.IsFullscreen || info.IsBorderless)
                            {
                                IsEmulatorWindowed = false;
                                EmulatorResolutionStatus = info.IsBorderless
                                    ? "✓ Borderless fullscreen detected"
                                    : "✓ Fullscreen / maximized detected";
                            }
                            else if (info.ScreenCoverage > 0 && info.ScreenCoverage < 0.85)
                            {
                                IsEmulatorWindowed = true;
                                EmulatorResolutionStatus = $"⚠ Windowed ({info.ScreenCoverage:P0} coverage) — fullscreen recommended";
                            }
                            else
                            {
                                IsEmulatorWindowed = false;
                                EmulatorResolutionStatus = $"✓ {info.Width} × {info.Height}";
                            }
                        }
                    }
                    catch { /* full detect failed, fall through to live monitoring */ }
                }
                else if (!_emulatorWasConnected)
                {
                    _emulatorWasConnected = true;
                }

                // ── Live fullscreen + resolution monitoring (every tick, if enabled) ──
                try
                {
                    var (isFs, coverage, clientW, clientH) = EmulatorService.CheckEmulatorFullscreen();
                    if (!isFs && coverage > 0 && coverage < 0.85)
                    {
                        IsEmulatorWindowed = true;
                        EmulatorResolutionStatus = $"⚠ Windowed ({coverage:P0} coverage) — fullscreen recommended";
                    }
                    else if (isFs || coverage >= 0.85)
                    {
                        IsEmulatorWindowed = false;
                        EmulatorResolutionStatus = "✓ Fullscreen";
                    }

                    // Auto-update emulator resolution from live window (if live sync enabled)
                    if (_liveResolutionSync && clientW > 100 && clientH > 100)
                    {
                        bool widthDrift = Math.Abs(clientW - EmulatorWidth) > 20;
                        bool heightDrift = Math.Abs(clientH - EmulatorHeight) > 20;
                        if (widthDrift || heightDrift)
                        {
                            EmulatorWidth = clientW;
                            EmulatorHeight = clientH;
                            EmulatorResolutionSource = "Live Window (auto-synced)";
                        }
                    }
                }
                catch { /* fullscreen check non-critical */ }
            }
            else
            {
                var names = string.Join(", ", distinctNames);
                EmulatorStatus = $"⚠ {instanceCount} instances detected ({names}) — close one to continue";
                IsEmulatorBlocked = true;
                IsEmulatorConnected = false;
                _emulatorWasConnected = false;
                ClearEmulatorInfo();

                if (IsActive)
                {
                    _sensitivityEngine.IsActive = false;
                    _sensitivityEngine.Uninstall();
                    IsActive = false;
                    IsPaused = true;
                }
            }
        }
        catch
        {
            EmulatorStatus = "Unable to check emulators";
            IsEmulatorBlocked = false;
            IsEmulatorConnected = false;
        }
    }

    private void UpdateEmulatorInfo()
    {
        try
        {
            var info = EmulatorService.GetRunningEmulatorInfo();
            if (info == null) { ClearEmulatorInfo(); return; }

            EmuInfoProcess = info.ProcessName;
            EmuInfoPID = info.PID.ToString();
            EmuInfoMemory = info.MemoryMB > 0 ? $"{info.MemoryMB} MB" : "—";
            EmuInfoVersion = !string.IsNullOrEmpty(info.Version) ? info.Version : "—";
            EmuInfoEngine = !string.IsNullOrEmpty(info.Engine) ? info.Engine : "—";
            EmuInfoCPU = info.AllocatedCPU != "—" ? $"{info.AllocatedCPU} Cores" : "—";
            EmuInfoRAM = info.AllocatedRAM != "—" ? $"{int.Parse(info.AllocatedRAM) / 1024} MB" : "—";
            EmuInfoFPS = info.FPS != "—" ? $"{info.FPS} FPS" : "—";

            if (info.StartTime.HasValue)
            {
                var uptime = DateTime.Now - info.StartTime.Value;
                EmuInfoUptime = uptime.TotalHours >= 1
                    ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                    : $"{uptime.Minutes}m {uptime.Seconds}s";
            }
            else EmuInfoUptime = "—";
        }
        catch { ClearEmulatorInfo(); }
    }

    private void ClearEmulatorInfo()
    {
        EmuInfoProcess = "—"; EmuInfoPID = "—"; EmuInfoMemory = "—";
        EmuInfoVersion = "—"; EmuInfoEngine = "—"; EmuInfoCPU = "—";
        EmuInfoRAM = "—"; EmuInfoFPS = "—"; EmuInfoUptime = "—";
    }

    private async Task OptimizeEmulatorAsync()
    {
        if (!IsEmulatorConnected)
        {
            Msg("No emulator connected.\n\nPlease start BlueStacks or MSI App Player first.", "Not Connected", MessageBoxImage.Warning);
            return;
        }

        await RunWithLoadingAsync("Optimizing emulator for maximum performance…", () =>
        {
            var (success, message) = EmulatorService.OptimizeConnectedEmulator();
            Application.Current.Dispatcher.Invoke(() => Msg(message, success ? "Emulator Optimized" : "Optimization", success ? MessageBoxImage.Information : MessageBoxImage.Warning));
        });
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Validates a text box value: parses it (accepts both dot and comma as decimal separator),
    /// clamps to [min..max], formats to 2 decimal places.
    /// If the input can't be parsed, returns the default value formatted.
    /// </summary>
    private static string ValidateTextBox(string input, double min, double max, double defaultValue)
    {
        // Accept both comma and dot as decimal separator
        string normalized = input?.Replace(',', '.') ?? "";
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val))
            val = defaultValue;
        val = Math.Clamp(val, min, max);
        return val.ToString("0.00");
    }

    private bool TryParseAdvanced(out double gs, out double sl,
        out double px, out double py, out double sx, out double sy, out double ac,
        out double rc, out double smooth, out double steady)
    {
        gs = sl = px = py = sx = sy = ac = rc = smooth = steady = 0;
        return TryParseUserDouble(GeneralSens, out gs) && TryParseUserDouble(SensLimit, out sl) &&
               TryParseUserDouble(PreScaleX, out px) &&
               TryParseUserDouble(PreScaleY, out py) && TryParseUserDouble(PostScaleX, out sx) &&
               TryParseUserDouble(PostScaleY, out sy) && TryParseUserDouble(Acceleration, out ac) &&
               TryParseUserDouble(AdvRecoilControl, out rc) &&
               TryParseUserDouble(AdvSmoothStrength, out smooth) &&
               TryParseUserDouble(AdvSteadyAim, out steady);
    }

    private static void Msg(string text, string title, MessageBoxImage icon = MessageBoxImage.Information)
        => MessageBox.Show(text, title, MessageBoxButton.OK, icon);

    #region Interception Kernel Driver Install/Uninstall

    private void RefreshInterceptionStatus()
    {
        InterceptionInstalled = Services.InterceptionDriverService.IsDriverInstalled();
        UpdateInterceptionStatusText();
    }

    /// <summary>Separates OS driver presence from subscription kernel entitlement.</summary>
    private void UpdateInterceptionStatusText()
    {
        if (!InterceptionInstalled)
        {
            InterceptionStatusText = "Driver: not detected (registry / services / driver files)";
            return;
        }

        if (!KernelAccessAllowed)
        {
            InterceptionStatusText = "Driver: installed — subscription uses User mode only (kernel disabled)";
            return;
        }

        InterceptionStatusText = "Driver: installed — kernel allowed (restart after install if needed)";
    }

    /// <summary>
    /// Re-hooks the mouse engine after <see cref="KernelAccessAllowed"/> changes while activated,
    /// so kernel capture is never left running when the subscription disables kernel mode.
    /// </summary>
    private void RebindActiveEngineForKernelLicense()
    {
        try
        {
            SyncEngineSettings();
            _sensitivityEngine.Uninstall();
            SyncEngineSettings();
            _sensitivityEngine.Install();
            _sensitivityEngine.IsActive = true;
            _sensitivityEngine.AdvancedActive = AdvancedActive;
        }
        catch
        {
            // ignore — engine will recover on next Activate
        }
    }

    private async Task InstallInterceptionDriverAsync()
    {
        if (IsInterceptionBusy) return;

        // Block if kernel access is denied by license
        if (!KernelAccessAllowed)
        {
            Msg("Kernel Mode is not available for your license.\n\n" +
                "Your license key does not include Kernel Mode access.\n" +
                "Contact your reseller or admin to upgrade your license.",
                "Kernel Access Denied", MessageBoxImage.Warning);
            return;
        }

        // Block if already installed
        if (InterceptionInstalled)
        {
            Msg("Interception kernel driver is already installed!\n\nNo action needed. The driver is active and ready.",
                "Already Installed", MessageBoxImage.Information);
            return;
        }

        IsInterceptionBusy = true;
        try
        {
            await RunWithLoadingAsync("Installing Interception kernel driver…", () =>
            {
                var (success, message) = Services.InterceptionDriverService.InstallDriver();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshInterceptionStatus();
                    if (success)
                    {
                        // Ask user to restart PC
                        var result = MessageBox.Show(
                            "Interception kernel driver installed successfully!\n\n" +
                            "You must restart your computer for the driver to become active.\n\n" +
                            "Do you want to restart your PC now?",
                            "Driver Installed — Restart Required",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                            RequestSystemRestart("LegitX V2 — Restarting to activate Interception kernel driver");
                    }
                    else
                    {
                        Msg(message, "Installation Failed", MessageBoxImage.Warning);
                    }
                });
            });
        }
        finally { IsInterceptionBusy = false; }
    }

    private async Task UninstallInterceptionDriverAsync()
    {
        if (IsInterceptionBusy) return;

        // Block if not installed
        if (!InterceptionInstalled)
        {
            Msg("Interception kernel driver is not installed!\n\nNothing to uninstall.",
                "Not Installed", MessageBoxImage.Information);
            return;
        }

        if (!Confirm("Are you sure you want to uninstall the Interception kernel driver?\n\nLegitX V2 will switch to User Mode after reboot."))
            return;

        IsInterceptionBusy = true;
        try
        {
            await RunWithLoadingAsync("Removing Interception kernel driver…", () =>
            {
                var (success, message) = Services.InterceptionDriverService.UninstallDriver();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshInterceptionStatus();
                    if (success)
                    {
                        var result = MessageBox.Show(
                            "Interception kernel driver removed successfully!\n\n" +
                            "You must restart your computer for the removal to take effect.\n" +
                            "LegitX V2 will automatically use User Mode after restart.\n\n" +
                            "Do you want to restart your PC now?",
                            "Driver Removed — Restart Required",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                            RequestSystemRestart("LegitX V2 — Restarting after Interception driver removal");
                    }
                    else
                    {
                        Msg(message, "Removal Failed", MessageBoxImage.Warning);
                    }
                });
            });
        }
        finally { IsInterceptionBusy = false; }
    }

    #endregion

    private static bool Confirm(string text)
        => MessageBox.Show(text, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>
    /// Initiates a system restart in 5 seconds with a reason message.
    /// Uses ProcessStartInfo with UseShellExecute=true so "shutdown.exe" resolves correctly on .NET 8.
    /// </summary>
    private void RequestSystemRestart(string reason)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = $"/r /t 5 /c \"{reason}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            Msg("Could not initiate restart.\n\nPlease restart your computer manually.", "Restart", MessageBoxImage.Warning);
        }
    }

    #region Firebase Feature Flags (Remote Control)

    /// <summary>
    /// Fetches per-user feature flags from Firestore Cloud Database and updates visibility properties.
    /// Called on startup and every 2 minutes via polling timer.
    /// </summary>
    private async Task LoadFeatureFlagsAsync()
    {
        try
        {
            var uid = AuthService.GetUserId();
            if (string.IsNullOrEmpty(uid)) return; // Not logged in — keep all visible

            var idToken = AuthService.GetIdToken();

            // On first call, initialize default flags in DB so admin can see & control this user
            await FirebaseFeatureService.InitializeUserFeaturesAsync(uid, idToken).ConfigureAwait(false);

            // Also store email for admin reference
            var email = "";
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\LegitX V2\Auth", false);
                email = key?.GetValue("Email")?.ToString() ?? "";
            }
            catch { /* ignore */ }
            if (!string.IsNullOrEmpty(email))
                await FirebaseFeatureService.StoreUserEmailAsync(uid, email, idToken).ConfigureAwait(false);

            // Fetch the feature flags
            var flags = await FirebaseFeatureService.FetchFeatureFlagsAsync(uid, idToken).ConfigureAwait(false);

            // Fetch kernel access from the license document
            var licenseCheck = await LicenseService.VerifyLicenseAsync(uid, idToken).ConfigureAwait(false);

            // Update UI on the dispatcher thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                FeatureBypassVisible = flags.GetValueOrDefault(FirebaseFeatureService.KeyBypass, true);
                FeatureStreamerModeVisible = flags.GetValueOrDefault(FirebaseFeatureService.KeyStreamerMode, true);
                FeatureFormBypassVisible = flags.GetValueOrDefault(FirebaseFeatureService.KeyFormBypass, true);
                FeatureStopNetworkVisible = flags.GetValueOrDefault(FirebaseFeatureService.KeyStopNetwork, true);

                // If a feature is hidden, also turn off its active state to avoid ghost behavior
                if (!FeatureBypassVisible && BypassToggle) BypassToggle = false;
                if (!FeatureStreamerModeVisible && StreamerMode) StreamerMode = false;
                if (!FeatureFormBypassVisible && FormBypass) FormBypass = false;
                if (!FeatureStopNetworkVisible && StopNetwork) StopNetwork = false;

                // Update kernel access from the license document
                KernelAccessAllowed = licenseCheck.KernelAccessAllowed;
            });
        }
        catch
        {
            // Network failure — all features stay visible (defaults)
        }
    }

    /// <summary>
    /// Starts a polling timer that re-fetches feature flags every 2 minutes for live updates.
    /// </summary>
    private void StartFeatureFlagPolling()
    {
        _featureFlagTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _featureFlagTimer.Tick += async (_, _) => await LoadFeatureFlagsAsync();
        _featureFlagTimer.Start();
    }

    /// <summary>
    /// Call this after successful login to immediately fetch feature flags for the new user.
    /// </summary>
    public async Task RefreshFeatureFlagsAsync()
    {
        await LoadFeatureFlagsAsync();
    }

    #endregion

    /// <summary>Cleans up resources — must be called on app close.</summary>
    public void Cleanup()
    {
        // Stop feature-flag polling
        _featureFlagTimer?.Stop();
        // Reset only engine-internal AI flags (don't touch Windows mouse settings)
        // so the user's mouse options (acceleration OFF, etc.) persist after exit
        InputOptimizer.ResetEngineOnly(_sensitivityEngine);
        // Restore the user's original Windows mouse / EPP state
        InputOptimizer.RestoreOriginalMouseState();
        _sensitivityEngine.Dispose();
        _emulatorTimer?.Stop();
    }

    #endregion

    #region Native Interop

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    private const uint SPI_SETMOUSE      = 0x0004;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE   = 0x02;

    #endregion
}
