using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LegitX.WPF.Services;
using LegitX.WPF.Views;
using static LegitX.WPF.Services.LicenseService;

namespace LegitX.WPF;

public partial class App : Application
{
    /// <summary>Named mutex that prevents more than one instance of LegitX V2 from running.</summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>Layer 1 — Assembly integrity verification.</summary>
    private SecurityService? _security;

    /// <summary>Layer 2 — Method integrity verification (anti-patch).</summary>
    private MethodIntegrityService? _methodIntegrity;

    /// <summary>Layer 3 — Anti-RE tool detection.</summary>
    private AntiReverseEngineerService? _antiRE;

    /// <summary>Layer 4 — Timing-based debugger detection.</summary>
    private TimingDebuggerDetector? _timingDetector;

    /// <summary>Layer 5 — Native anti-debug, anti-dump, environment checks.</summary>
    private AntiDebugService? _antiDebug;

    /// <summary>Layer 6 — Managed DLL on-disk hash when hosted by <c>dotnet.exe</c> (FDD portable).</summary>
    private ManagedImageGuard? _managedImageGuard;

    /// <summary>
    /// Cross-layer runtime guard (additional hardening):
    /// runs independent checks on a short jittered loop.
    /// </summary>
    private System.Threading.Timer? _runtimeGuardTimer;
    private static readonly Random _runtimeGuardJitter = new();

    /// <summary>Exposes the security service for QuickCheck calls from other windows.</summary>
    internal SecurityService? Security => _security;

    /// <summary>Exposes the method integrity service for Scan calls.</summary>
    internal MethodIntegrityService? MethodIntegrity => _methodIntegrity;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Single-instance enforcement ──
        const string mutexName = "Global\\LegitX_V2_SingleInstance_8F3A";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LegitX V2 is already running.\n\nOnly one instance can run at a time.",
                "LegitX V2", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        // ── Animation performance fix for Win10/11 with AllowsTransparency ──
        RenderOptions.ProcessRenderMode = RenderMode.Default;

        try
        {
            var tier = RenderCapability.Tier >> 16;
            if (tier >= 2)
            {
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 60 });
            }
        }
        catch { }

        // Catch unhandled exceptions
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "LegitX V2 - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                MessageBox.Show($"Fatal Error: {ex.Message}\n\n{ex.StackTrace}",
                    "LegitX V2 - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);

        // ── Require Administrator privileges ──
        if (!InterceptionDriverService.IsAdmin())
        {
            MessageBox.Show(
                "LegitX V2 must be run as Administrator!\n\n" +
                "Without Administrator privileges, the kernel driver and sensitivity engine cannot function.\n\n" +
                "Right-click on LegitX V2.exe → Run as administrator",
                "Run as Admin Required", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // ── Extract Interception DLL next to exe if missing ──
        InterceptionDriverService.EnsureDllExtracted();

        // ═══════════════════════════════════════════════════════════════
        //  LAYER 1 — Assembly Integrity Verification (Anti-Tamper)
        //  Captures golden hashes of the EXE at startup. Background timer
        //  re-checks every 60 seconds. If the binary is patched on disk
        //  or in memory → force exit.
        // ═══════════════════════════════════════════════════════════════
        _security = new SecurityService();
        if (!_security.Initialize())
        {
            MessageBox.Show(
                "Application integrity check failed.\n\n" +
                "The application binary appears to be corrupted or modified.\n" +
                "Please re-download LegitX V2 from the official website.",
                "LegitX V2 — Integrity Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _security.IntegrityViolated += OnIntegrityViolation;

        _managedImageGuard = new ManagedImageGuard();
        if (!_managedImageGuard.Initialize())
        {
            MessageBox.Show(
                "Managed assembly integrity initialization failed.\n\n" +
                "If you are running from a network drive or an unpacked portable folder, copy LegitX V2 to a local disk and try again.",
                "LegitX V2 — Security", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYER 2 — Method Integrity Verification (Anti-Patch)
        // ═══════════════════════════════════════════════════════════════
        _methodIntegrity = new MethodIntegrityService();
        _methodIntegrity.Initialize();
        _methodIntegrity.MethodTampered += OnIntegrityViolation;

        // ═══════════════════════════════════════════════════════════════
        //  LAYER 3 — Anti-dnSpy / Anti-Decompiler
        // ═══════════════════════════════════════════════════════════════
        _antiRE = new AntiReverseEngineerService();
        if (!_antiRE.Initialize())
        {
            MessageBox.Show(
                "A reverse-engineering tool was detected.\n\n" +
                "Please close all debugging / decompilation tools and try again.",
                "LegitX V2 — Security", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        _antiRE.ToolDetected += OnToolDetected;

        // ═══════════════════════════════════════════════════════════════
        //  LAYER 4 — Timing-Based Debugger Detection
        // ═══════════════════════════════════════════════════════════════
        _timingDetector = new TimingDebuggerDetector();
        _timingDetector.Initialize();
        _timingDetector.DebuggerDetected += OnIntegrityViolation;

        // ═══════════════════════════════════════════════════════════════
        //  LAYER 5 — Native Anti-Debug, Anti-Dump, Environment Checks
        //  Uses Windows kernel APIs: IsDebuggerPresent, NtQueryInformationProcess,
        //  CheckRemoteDebuggerPresent, parent process validation, PE header erasure.
        // ═══════════════════════════════════════════════════════════════
        _antiDebug = new AntiDebugService();
        if (!_antiDebug.Initialize())
        {
            MessageBox.Show(
                "A debugger or analysis tool was detected.\n\n" +
                "Please close all debugging tools and restart LegitX V2.",
                "LegitX V2 — Security", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        _antiDebug.DebuggerDetected += OnIntegrityViolation;

        // ── Cross-layer guard loop (extra anti-crack hardening) ──
        StartRuntimeGuard();

        // ═══════════════════════════════════════════════════════════════
        //  MAINTENANCE CHECK — before any UI or login
        // ═══════════════════════════════════════════════════════════════
        {
            var (maint, maintMsg) = Task.Run(() => FirebaseSessionMonitor.CheckMaintenanceOnceAsync()).GetAwaiter().GetResult();
            if (maint)
            {
                MaintenanceConsole.ShowAndExit(maintMsg);
                return; // never reached
            }
        }

        // ── Authentication gate ──
        if (AuthService.IsLoggedIn())
        {
            // Already logged in — verify hardware before granting access
            _ = VerifyHwidAndLaunchAsync();
        }
        else
        {
            // Show login screen
            var login = new LoginWindow();
            login.ShowDialog();

            if (login.LoginSuccessful)
                ShowMainWindow();
            else
                Shutdown();
        }
    }

    /// <summary>
    /// Checks HWID for auto-login sessions. If the PC doesn't match, forces re-login.
    /// </summary>
    private async Task VerifyHwidAndLaunchAsync()
    {
        try
        {
            var uid = AuthService.GetUserId();
            var idToken = AuthService.GetIdToken();

            if (!string.IsNullOrEmpty(uid))
            {
                // ── Ensure user email is stored in Firestore (may be missing for legacy accounts) ──
                try
                {
                    using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\LegitX V2\Auth", false);
                    var email = regKey?.GetValue("Email")?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(email))
                        await FirebaseFeatureService.StoreUserEmailAsync(uid, email, idToken);
                }
                catch { /* silent */ }

                // ── HWID check ──
                var hwResult = await HardwareIdService.VerifyAndRegisterAsync(uid, idToken);

                if (!hwResult.Allowed)
                {
                    // Hardware mismatch — clear session and show login
                    AuthService.Logout();
                    MessageBox.Show(hwResult.Reason,
                        "LegitX V2 — Hardware Lock", MessageBoxButton.OK, MessageBoxImage.Warning);

                    var login = new LoginWindow();
                    login.ShowDialog();

                    if (login.LoginSuccessful)
                        ShowMainWindow();
                    else
                        Shutdown();
                    return;
                }

                // ── HWID Ban check — catches banned hardware even with new accounts ──
                var hwidBan = await LicenseService.CheckHwidBanAsync(idToken);
                if (hwidBan.Banned)
                {
                    AuthService.Logout();
                    AccountStatusDialog.Show(AccountStatusType.HwidBanned, hwidBan.Reason);
                    Shutdown();
                    return;
                }

                // ── Layer 5: Bind server-side session token ──
                await SessionTokenService.BindSessionAsync(uid, idToken);

                // ── License + Account Status check on auto-login ──
                var licenseResult = await LicenseService.VerifyLicenseAsync(uid, idToken);

                // Account banned/suspended/deactivated/trial-expired
                if (!licenseResult.Licensed && !licenseResult.NeedsCode
                    && !licenseResult.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
                {
                    AuthService.Logout();
                    ShowAccountStatusPopup(licenseResult);
                    Shutdown();
                    return;
                }

                if (!licenseResult.Licensed && licenseResult.NeedsCode)
                {
                    // License revoked or missing — show redeem dialog
                    var redeemDialog = new Views.RedeemCodeDialog();
                    redeemDialog.ShowDialog();

                    if (!redeemDialog.CodeActivated)
                    {
                        AuthService.Logout();
                        MessageBox.Show(
                            "License activation required to use LegitX V2.",
                            "LegitX V2 — No License", MessageBoxButton.OK, MessageBoxImage.Warning);

                        var login = new LoginWindow();
                        login.ShowDialog();

                        if (login.LoginSuccessful)
                            ShowMainWindow();
                        else
                            Shutdown();
                        return;
                    }
                }
            }
        }
        catch { /* If checks fail, allow auto-login */ }

        ShowMainWindow();
    }

    /// <summary>
    /// Maps a LicenseCheckResult to the beautiful AccountStatusDialog popup.
    /// </summary>
    private static void ShowAccountStatusPopup(LicenseCheckResult result)
    {
        var statusType = result.Issue switch
        {
            AccountIssue.Banned => AccountStatusType.Banned,
            AccountIssue.Suspended => AccountStatusType.Suspended,
            AccountIssue.Deactivated => AccountStatusType.Deactivated,
            AccountIssue.TrialExpired => AccountStatusType.TrialExpired,
            AccountIssue.LicenseRevoked => AccountStatusType.LicenseRevoked,
            AccountIssue.HwidBanned => AccountStatusType.HwidBanned,
            AccountIssue.NoLicense => AccountStatusType.NoLicense,
            AccountIssue.SessionInvalid => AccountStatusType.SessionInvalid,
            AccountIssue.ServerDown => AccountStatusType.ServerDown,
            _ => AccountStatusType.NoLicense
        };

        (string, string)? d1 = null, d2 = null, d3 = null;
        if (result.Issue == AccountIssue.TrialExpired)
        {
            d1 = ("License Type", "Trial");
            d2 = ("Trial Duration", $"{result.TrialDaysTotal} days");
            if (result.TrialExpiresAt.HasValue)
                d3 = ("Expired On", result.TrialExpiresAt.Value.ToString("MMM dd, yyyy"));
        }

        AccountStatusDialog.Show(statusType, result.Reason, d1, d2, d3);
    }

    private void ShowMainWindow()
    {
        // Switch to normal shutdown mode now that main window is opening
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cleanup security services
        try { _security?.Dispose(); } catch { }
        try { _methodIntegrity?.Dispose(); } catch { }
        try { _antiRE?.Dispose(); } catch { }
        try { _timingDetector?.Dispose(); } catch { }
        try { _antiDebug?.Dispose(); } catch { }
        try { _managedImageGuard?.Dispose(); } catch { }
        try { _runtimeGuardTimer?.Dispose(); } catch { }
        try { SessionTokenService.Clear(); } catch { }
        try { EncryptedResultStore.Clear(); } catch { }

        // Cleanup main window view-model (unhook mouse, stop timers, etc.)
        try
        {
            if (MainWindow is MainWindow mw && mw.DataContext is ViewModels.MainViewModel vm)
                vm.Cleanup();
        }
        catch { }

        // Purge secrets
        try { SecureStrings.PurgeAll(); } catch { }

        // Release the single-instance mutex
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        // Suppress any native DLL teardown errors (Interception C++ runtime)
        // that can occur during process exit when the DLL's static destructors run
        try { base.OnExit(e); } catch { }

        // Force-kill the entire process tree so absolutely nothing lingers
        // in background (hooks, timers, threads, HTTP clients, etc.)
        // Using Process.Kill bypasses DLL_PROCESS_DETACH which avoids the
        // Interception C++ runtime destructor crash.
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            self.Kill();
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Called by SecurityService when binary tampering is detected at runtime.
    /// Forces immediate shutdown with no possibility of recovery.
    /// </summary>
    private void OnIntegrityViolation(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            try { _security?.Dispose(); } catch { }
            try { _methodIntegrity?.Dispose(); } catch { }
            try { _antiRE?.Dispose(); } catch { }
            try { _timingDetector?.Dispose(); } catch { }
            try { _antiDebug?.Dispose(); } catch { }
            try { _managedImageGuard?.Dispose(); } catch { }

            SessionTokenService.Clear();
            EncryptedResultStore.Clear();
            AuthService.Logout();

            MessageBox.Show(
                "LegitX V2 has detected that the application has been modified.\n\n" +
                "This can happen if:\n" +
                "  • The file was corrupted during download\n" +
                "  • Third-party software modified the binary\n" +
                "  • The file was tampered with\n\n" +
                "Please re-download LegitX V2 from the official website.\n\n" +
                "The application will now close.",
                "LegitX V2 — Integrity Violation",
                MessageBoxButton.OK, MessageBoxImage.Error);

            Environment.Exit(1);
        });
    }

    private void OnToolDetected(string detail)
    {
        Dispatcher.Invoke(() =>
        {
            try { _security?.Dispose(); } catch { }
            try { _methodIntegrity?.Dispose(); } catch { }
            try { _antiRE?.Dispose(); } catch { }
            try { _timingDetector?.Dispose(); } catch { }
            try { _antiDebug?.Dispose(); } catch { }
            try { _managedImageGuard?.Dispose(); } catch { }

            SessionTokenService.Clear();
            EncryptedResultStore.Clear();
            AuthService.Logout();

            MessageBox.Show(
                "A reverse-engineering or debugging tool was detected running on your system.\n\n" +
                "Please close all such tools and restart LegitX V2.\n\n" +
                "The application will now close.",
                "LegitX V2 — Security",
                MessageBoxButton.OK, MessageBoxImage.Error);

            Environment.Exit(1);
        });
    }

    /// <summary>
    /// Additional anti-tamper guard that cross-checks multiple layers
    /// at a faster cadence than the individual background scanners.
    /// </summary>
    private void StartRuntimeGuard()
    {
        _runtimeGuardTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                // Layer 1 quick in-memory integrity challenge
                if (_security is not null && !_security.QuickCheck())
                {
                    OnIntegrityViolation("Security quick-check failed.");
                    return;
                }

                // Layer 6 — LegitX V2.dll on disk (when not the same file as the native host)
                if (_managedImageGuard is not null && !_managedImageGuard.TryVerify(out var managedReason))
                {
                    OnIntegrityViolation(managedReason ?? "Managed assembly integrity failed.");
                    return;
                }

                var poison = ModulePoisonGuard.ScanLoadedAssemblies();
                if (!string.IsNullOrEmpty(poison))
                {
                    OnIntegrityViolation(poison);
                    return;
                }

                // Layer 2 immediate method patch scan
                if (_methodIntegrity is not null && !_methodIntegrity.Scan())
                {
                    OnIntegrityViolation("Method integrity scan failed.");
                    return;
                }

                // Layer 3 RE tool scan (renamed-window catches included)
                if (_antiRE is not null)
                {
                    var tool = _antiRE.ScanNow();
                    if (!string.IsNullOrEmpty(tool))
                    {
                        OnToolDetected(tool);
                        return;
                    }
                }

                // Layer 5 anti-debug environment check
                if (_antiDebug is not null)
                {
                    var reason = _antiDebug.RunAllChecks();
                    if (!string.IsNullOrEmpty(reason))
                    {
                        OnIntegrityViolation(reason);
                        return;
                    }
                }
            }
            catch
            {
                // Keep security timer alive even if one cycle throws.
            }
            finally
            {
                try { _runtimeGuardTimer?.Change(NextRuntimeGuardDelay(), Timeout.Infinite); }
                catch { }
            }
        }, null, NextRuntimeGuardDelay(), Timeout.Infinite);
    }

    private static int NextRuntimeGuardDelay()
    {
        lock (_runtimeGuardJitter)
        {
            // Faster than existing guards, but jittered to resist timing bypasses.
            return _runtimeGuardJitter.Next(7_000, 16_001);
        }
    }
}
