using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LegitX.WPF.Services;
using LegitX.WPF.ViewModels;

namespace LegitX.WPF.Views;

public partial class MainWindow : Window
{
    private readonly WindowService _windowService = new();
    private readonly FirebaseSessionMonitor _sessionMonitor = new();
    private bool _isFormHidden;
    private Storyboard? _spinnerStoryboard;

    public MainWindow()
    {
        InitializeComponent();

        // ═══════════════════════════════════════════════════════════════
        //  SECURITY GATE — MainWindow refuses to open without valid session
        //  Even if someone bypasses the login window, this blocks them.
        // ═══════════════════════════════════════════════════════════════
        if (!VerifySessionIntegrity())
        {
            // No valid session — close immediately
            Loaded += (_, _) => Close();
            return;
        }

        // ── Layer 1: Assembly integrity quick-check ──
        if (Application.Current is App app && app.Security != null && !app.Security.QuickCheck())
        {
            Loaded += (_, _) => Close();
            return;
        }

        // ── Layer 2: Method integrity scan ──
        if (Application.Current is App app2 && app2.MethodIntegrity != null && !app2.MethodIntegrity.Scan())
        {
            Loaded += (_, _) => Close();
            return;
        }

        // ── Layer 6: Encrypted result store — license + HWID must decrypt valid ──
        if (!EncryptedResultStore.IsLicenseValid() || !EncryptedResultStore.IsHwidValid())
        {
            Loaded += (_, _) => Close();
            return;
        }

        // Create ViewModel in code-behind so exceptions give clear diagnostics
        // instead of being masked as StaticResourceExtension errors.
        try
        {
            DataContext = new MainViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize settings:\n{ex.Message}\n\n{ex.StackTrace}",
                "LegitX V2 – Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            DataContext = new MainViewModel(); // retry with defaults
        }

        // Wire AlwaysOnTop so Settings tab can toggle window Topmost
        if (DataContext is MainViewModel vm2)
        {
            Topmost = vm2.AlwaysOnTop;
            vm2.AlwaysOnTopChanged += on => Dispatcher.Invoke(() => Topmost = on);
        }

        // Show logged-in username
        var user = AuthService.GetLoggedInUser();
        if (!string.IsNullOrEmpty(user))
            LoggedInUser.Text = user;

        // Apply saved window size
        LoadWindowPosition();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // ── Loading Overlay ──
    private void InitializeSpinner()
    {
        var animation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(animation);
        Storyboard.SetTarget(animation, SpinnerRing);
        Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
    }

    private void ShowLoadingOverlay(bool show, string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (show)
            {
                LoadingText.Text = text;
                LoadingOverlay.Visibility = Visibility.Visible;
                _spinnerStoryboard?.Begin();
            }
            else
            {
                _spinnerStoryboard?.Stop();
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        });
    }

    // ── Custom chrome handlers ──
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void LogoutBtn_Click(object sender, MouseButtonEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to logout?\n\nYou will need to sign in again next time.",
            "Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Stop monitoring during logout flow
            _sessionMonitor.Stop();

            SaveWindowPosition();
            AuthService.Logout();

            // Show login window again
            var login = new LoginWindow();
            login.ShowDialog();

            if (login.LoginSuccessful)
            {
                // Update the displayed username
                var user = AuthService.GetLoggedInUser();
                LoggedInUser.Text = user;

                // Refresh Firebase feature flags for the newly logged-in user
                if (DataContext is ViewModels.MainViewModel vm)
                    _ = vm.RefreshFeatureFlagsAsync();

                // Restart session monitor for the new user
                _sessionMonitor.Start();
            }
            else
            {
                // User closed login without logging in — close app
                _windowService.Dispose();
                _sessionMonitor.Dispose();
                Application.Current.Shutdown();
            }
        }
    }

    // ── Numeric-only input filter for editable value boxes (integers) ──
    private void NumericOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Allow digits only (for integer text boxes like resolution)
        e.Handled = !int.TryParse(e.Text, out _);
    }

    // ── Decimal-friendly input filter — allows digits, dot, and comma ──
    private void DecimalOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        string ch = e.Text;
        // Allow digits, single dot, or single comma
        if (ch == "." || ch == ",")
        {
            // Only allow one decimal separator in the text box
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb != null && (tb.Text.Contains('.') || tb.Text.Contains(',')))
                e.Handled = true; // already has a decimal
            else
                e.Handled = false;
        }
        else
        {
            e.Handled = !char.IsDigit(ch, 0);
        }
    }

    private void ShortcutBadge_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OpenShortcutSettingsCommand.Execute(null);
    }

    // ── Window size persistence ──
    private void LoadWindowPosition()
    {
        var (w, h, left, top, max) = SettingsService.LoadWindowSize();
        Width = w;
        Height = h;
        if (left >= 0 && top >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        if (max)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowPosition()
    {
        if (WindowState == WindowState.Normal)
            SettingsService.SaveWindowSize(Width, Height, Left, Top, false);
        else if (WindowState == WindowState.Maximized)
            SettingsService.SaveWindowSize(RestoreBounds.Width, RestoreBounds.Height,
                RestoreBounds.Left, RestoreBounds.Top, true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _windowService.Initialize(hwnd);
        _windowService.ToggleVisibilityRequested += ToggleVisibility;
        _windowService.PauseSensitivityRequested += TogglePauseSensitivity;

        // Initialize spinner animation for loading overlay
        InitializeSpinner();

        if (DataContext is MainViewModel vm)
        {
            vm.StreamerModeChanged += on => _windowService.SetStreamerMode(on);

            vm.FormBypassChanged += on =>
            {
                _windowService.SetHideFromAltTab(on);
                ShowInTaskbar = !on;
            };

            vm.PinOnTopChanged += on => Topmost = on;

            vm.StopNetworkChanged += on =>
            {
                var exe = Environment.ProcessPath ?? "";
                if (on) _windowService.CreateFirewallRule(exe);
                else _windowService.RemoveFirewallRule(exe);
            };

            // Wire up loading overlay from ViewModel
            vm.LoadingOverlayRequested += ShowLoadingOverlay;

            // Open custom driver report popup
            vm.ShowDriverReportPopupRequested += () =>
            {
                var dlg = new DriverReportDialog { Owner = this };
                dlg.ShowDialog();
            };

            // Open shortcut settings dialog
            vm.ShowShortcutSettingsRequested += () =>
            {
                var dlg = new ShortcutSettingsDialog { Owner = this };
                dlg.ShortcutsSaved += _ =>
                {
                    _windowService.RegisterVisibilityHotkey();
                    _windowService.RegisterPauseSensitivityHotkey();
                    UpdateShortcutDisplay();
                };
                dlg.ShowDialog();
            };

            // Save window size when main Save is clicked
            vm.SaveSettingsRequested += SaveWindowPosition;

            // Set always-on-top since PinOnTop defaults to true
            Topmost = vm.PinOnTop;
        }

        UpdateShortcutDisplay();

        // ── Firebase session monitor — detect deleted accounts / server down ──
        _sessionMonitor.SessionInvalidated += OnSessionInvalidated;
        _sessionMonitor.MaintenanceDetected += OnMaintenanceDetected;
        _sessionMonitor.Start();

        // ── Deep security verification — async check against Firebase + License ──
        // This is the "cannot-bypass" layer — even if someone patches past the login,
        // this fires immediately and kills the app if no valid session + license exists.
        _ = DeepSessionVerificationAsync();
    }

    /// <summary>
    /// Called by FirebaseSessionMonitor when the account is deleted/disabled
    /// or the server has been unreachable for too long. Forces logout.
    /// </summary>
    private void OnSessionInvalidated(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            // Stop the monitor
            _sessionMonitor.Stop();

            // Save work before forced logout
            SaveWindowPosition();

            // Cleanup ViewModel
            if (DataContext is ViewModels.MainViewModel vm)
                vm.Cleanup();

            // Clear session
            AuthService.Logout();

            // Determine the status type from the reason text
            var statusType = DetermineStatusType(reason);

            // Show the beautiful popup
            AccountStatusDialog.Show(statusType, reason, owner: this);

            // Show login window — let them try to sign in again
            var login = new LoginWindow();
            login.ShowDialog();

            if (login.LoginSuccessful)
            {
                // Re-create ViewModel with fresh state
                try { DataContext = new ViewModels.MainViewModel(); } catch { }

                // Update displayed username
                var user = AuthService.GetLoggedInUser();
                LoggedInUser.Text = user;

                // Wire up events again for the new ViewModel
                if (DataContext is ViewModels.MainViewModel newVm)
                {
                    newVm.StreamerModeChanged += on => _windowService.SetStreamerMode(on);
                    newVm.FormBypassChanged += on =>
                    {
                        _windowService.SetHideFromAltTab(on);
                        ShowInTaskbar = !on;
                    };
                    newVm.PinOnTopChanged += on => Topmost = on;
                    newVm.StopNetworkChanged += on =>
                    {
                        var exe = Environment.ProcessPath ?? "";
                        if (on) _windowService.CreateFirewallRule(exe);
                        else _windowService.RemoveFirewallRule(exe);
                    };
                    newVm.LoadingOverlayRequested += ShowLoadingOverlay;
                    newVm.ShowDriverReportPopupRequested += () =>
                    {
                        var dlg = new DriverReportDialog { Owner = this };
                        dlg.ShowDialog();
                    };
                    newVm.ShowShortcutSettingsRequested += () =>
                    {
                        var dlg = new ShortcutSettingsDialog { Owner = this };
                        dlg.ShortcutsSaved += _ =>
                        {
                            _windowService.RegisterVisibilityHotkey();
                            _windowService.RegisterPauseSensitivityHotkey();
                            UpdateShortcutDisplay();
                        };
                        dlg.ShowDialog();
                    };
                    newVm.SaveSettingsRequested += SaveWindowPosition;
                    Topmost = newVm.PinOnTop;
                }

                // Restart the session monitor
                _sessionMonitor.Start();
            }
            else
            {
                // User didn't log in — close app
                _windowService.Dispose();
                _sessionMonitor.Dispose();
                Application.Current.Shutdown();
            }
        });
    }

    /// <summary>
    /// Called by FirebaseSessionMonitor when maintenance mode is detected.
    /// Shows a console window with the maintenance message and closes the app.
    /// </summary>
    private void OnMaintenanceDetected(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _sessionMonitor.Stop();
            SaveWindowPosition();

            if (DataContext is ViewModels.MainViewModel vm)
                vm.Cleanup();

            AuthService.Logout();
            _windowService.Dispose();
            _sessionMonitor.Dispose();

            // Show maintenance message in a console window
            MaintenanceConsole.ShowAndExit(message);
        });
    }

    private void UpdateShortcutDisplay()
    {
        var (key, mod) = ShortcutService.GetShortcut("ToggleVisibility");
        var display = ShortcutService.FormatShortcut(key, mod);
        if (ShortcutKeyBadge != null) ShortcutKeyBadge.Text = display;
    }

    private void ToggleVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (_isFormHidden)
            {
                Show();
                WindowState = WindowState.Normal;
                _isFormHidden = false;
            }
            else
            {
                Hide();
                _isFormHidden = true;
            }
        });
    }

    private void TogglePauseSensitivity()
    {
        Dispatcher.Invoke(() =>
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsPaused)
                    vm.ActivateCommand.Execute(null);
                else
                    vm.PauseCommand.Execute(null);
            }
        });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowPosition();

        // Stop session monitor
        _sessionMonitor.Dispose();

        // Cleanup sensitivity engine hook
        if (DataContext is MainViewModel vm)
            vm.Cleanup();

        _windowService.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECURITY — Independent session verification
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Synchronous check: Is there a locally-stored session at all?
    /// This blocks cracked binaries that skip the login window entirely.
    /// </summary>
    private static bool VerifySessionIntegrity()
    {
        // Must have a valid local session in registry
        if (!AuthService.IsLoggedIn())
            return false;

        // Must have a UID and token
        var uid = AuthService.GetUserId();
        var token = AuthService.GetIdToken();
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token))
            return false;

        return true;
    }

    /// <summary>
    /// Async deep check: Validates the session against Firebase Auth + Firestore license
    /// AFTER the window is visible. If this fails, the window closes itself.
    /// Called from OnLoaded — this is the "cannot-bypass" layer.
    /// </summary>
    private async Task DeepSessionVerificationAsync()
    {
        try
        {
            var uid = AuthService.GetUserId();
            var idToken = AuthService.GetIdToken();

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(idToken))
            {
                ForceShutdown("Session invalid. Please log in again.");
                return;
            }

            // ── Verify Firebase Auth account still exists ──
            var (authValid, authReason) = await AuthService.ValidateSessionAsync();
            if (!authValid)
            {
                ForceShutdown(authReason);
                return;
            }

            // ── Re-read the idToken AFTER ValidateSessionAsync (it may have refreshed it) ──
            idToken = AuthService.GetIdToken();

            // ── Verify HWID is not hardware-banned ──
            var hwidBan = await LicenseService.CheckHwidBanAsync(idToken);
            if (hwidBan.Banned)
            {
                ForceShutdownWithStatus(AccountStatusType.HwidBanned, hwidBan.Reason);
                return;
            }

            // ── Verify license + account status is active ──
            var licenseCheck = await LicenseService.VerifyLicenseAsync(uid, idToken);

            // Account banned/suspended/deactivated/trial-expired → force shutdown
            if (!licenseCheck.Licensed && !licenseCheck.NeedsCode
                && !licenseCheck.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
            {
                var st = licenseCheck.Issue switch
                {
                    LicenseService.AccountIssue.Banned => AccountStatusType.Banned,
                    LicenseService.AccountIssue.Suspended => AccountStatusType.Suspended,
                    LicenseService.AccountIssue.Deactivated => AccountStatusType.Deactivated,
                    LicenseService.AccountIssue.TrialExpired => AccountStatusType.TrialExpired,
                    LicenseService.AccountIssue.HwidBanned => AccountStatusType.HwidBanned,
                    _ => DetermineStatusType(licenseCheck.Reason)
                };

                (string, string)? d1 = null, d2 = null, d3 = null;
                if (licenseCheck.Issue == LicenseService.AccountIssue.TrialExpired)
                {
                    d1 = ("License Type", "Trial");
                    d2 = ("Trial Duration", $"{licenseCheck.TrialDaysTotal} days");
                    if (licenseCheck.TrialExpiresAt.HasValue)
                        d3 = ("Expired On", licenseCheck.TrialExpiresAt.Value.ToString("MMM dd, yyyy"));
                }

                ForceShutdownWithStatus(st, licenseCheck.Reason, d1, d2, d3);
                return;
            }

            // License revoked → force shutdown
            if (!licenseCheck.Licensed && licenseCheck.NeedsCode
                && !licenseCheck.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
            {
                ForceShutdownWithStatus(AccountStatusType.LicenseRevoked,
                    "Your license has been revoked or is no longer active.\n\n" +
                    "You will need a new license key to continue using LegitX V2.");
                return;
            }
        }
        catch
        {
            // Network errors are tolerated — session monitor handles long outages
        }
    }

    /// <summary>
    /// Force-kills the application with a message. Used when security checks fail.
    /// </summary>
    private void ForceShutdown(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            _sessionMonitor.Stop();

            AuthService.Logout();

            // Show beautiful popup instead of plain MessageBox
            var statusType = DetermineStatusType(reason);
            AccountStatusDialog.Show(statusType, reason, owner: this);

            try
            {
                if (DataContext is ViewModels.MainViewModel vm)
                    vm.Cleanup();
            }
            catch { }

            _windowService.Dispose();
            _sessionMonitor.Dispose();

            Application.Current.Shutdown();
        });
    }

    /// <summary>
    /// Force-kills the application with a specific status type popup.
    /// </summary>
    private void ForceShutdownWithStatus(
        AccountStatusType statusType, string message,
        (string, string)? d1 = null, (string, string)? d2 = null, (string, string)? d3 = null)
    {
        Dispatcher.Invoke(() =>
        {
            _sessionMonitor.Stop();
            AuthService.Logout();

            AccountStatusDialog.Show(statusType, message, d1, d2, d3, owner: this);

            try
            {
                if (DataContext is ViewModels.MainViewModel vm)
                    vm.Cleanup();
            }
            catch { }

            _windowService.Dispose();
            _sessionMonitor.Dispose();

            Application.Current.Shutdown();
        });
    }

    /// <summary>
    /// Helper: determine AccountStatusType from a reason string (used when we only have a string).
    /// </summary>
    private static AccountStatusType DetermineStatusType(string reason)
    {
        var r = reason.ToLowerInvariant();
        if (r.Contains("permanently banned") || r.Contains("account has been permanently"))
            return AccountStatusType.Banned;
        if ((r.Contains("hardware") && r.Contains("banned")) || (r.Contains("hardware") && r.Contains("blocked")))
            return AccountStatusType.HwidBanned;
        if (r.Contains("suspended"))
            return AccountStatusType.Suspended;
        if (r.Contains("deactivated"))
            return AccountStatusType.Deactivated;
        if (r.Contains("trial") && r.Contains("expired"))
            return AccountStatusType.TrialExpired;
        if (r.Contains("license") && (r.Contains("revoked") || r.Contains("no active")))
            return AccountStatusType.LicenseRevoked;
        if (r.Contains("unable to connect") || (r.Contains("connection") && r.Contains("lost")))
            return AccountStatusType.ServerDown;
        if (r.Contains("session") || r.Contains("log in again"))
            return AccountStatusType.SessionInvalid;
        return AccountStatusType.SessionInvalid;
    }
}
