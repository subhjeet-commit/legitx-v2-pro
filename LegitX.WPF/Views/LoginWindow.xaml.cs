using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LegitX.WPF.Services;

namespace LegitX.WPF.Views;

public partial class LoginWindow : Window
{
    public bool LoginSuccessful { get; private set; }
    private bool _passwordVisible;
    private bool _isBusy;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => EmailBox.Focus();

        // Wire up PasswordChanged for placeholder visibility
        PasswordBox.PasswordChanged += (_, _) =>
            PasswordPlaceholder.Visibility = PasswordBox.Password.Length == 0 && !_passwordVisible
                ? Visibility.Visible : Visibility.Collapsed;

        // Keep TextBox and PasswordBox in sync
        PasswordTextBox.TextChanged += (_, _) =>
        {
            if (_passwordVisible)
                PasswordPlaceholder.Visibility = PasswordTextBox.Text.Length == 0
                    ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    // ── Title bar drag ──
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        LoginSuccessful = false;
        Close();
    }

    // ── Google Sign In ──
    private async void GoogleSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        _isBusy = true;

        GoogleBtn.IsEnabled = false;
        GoogleBtnText.Text = "Signing in…";
        LoginBtn.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        SuccessText.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;

        try
        {
            var (success, message) = await AuthService.GoogleSignInAsync();

            if (success)
            {
                // ── Immediately store user email in Firestore so the admin panel can see the user ──
                var uid = AuthService.GetUserId();
                var idToken = AuthService.GetIdToken();
                var email2 = AuthService.GetLoggedInUser();
                try
                {
                    // Read full email from registry (GetLoggedInUser may truncate at @)
                    using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\LegitX V2\Auth", false);
                    var fullEmail = regKey?.GetValue("Email")?.ToString() ?? email2;
                    await FirebaseFeatureService.StoreUserEmailAsync(uid, fullEmail, idToken);
                }
                catch { /* silent — non-critical */ }

                // ── Hardware ID lockdown check ──
                GoogleBtnText.Text = "Verifying PC…";
                var hwResult = await HardwareIdService.VerifyAndRegisterAsync(uid, idToken);

                if (!hwResult.Allowed)
                {
                    // HWID mismatch → block login, log out the session
                    AuthService.Logout();
                    ShowError(hwResult.Reason);
                    ShakeWindow();
                    return;
                }

                // ── HWID Ban check — catches banned hardware even with new accounts ──
                GoogleBtnText.Text = "Checking restrictions…";
                var hwidBan = await LicenseService.CheckHwidBanAsync(idToken);
                if (hwidBan.Banned)
                {
                    AuthService.Logout();
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    AccountStatusDialog.Show(AccountStatusType.HwidBanned, hwidBan.Reason, owner: this);
                    return;
                }

                // ── Layer 5: Bind server-side session token ──
                GoogleBtnText.Text = "Binding session…";
                await SessionTokenService.BindSessionAsync(uid, idToken);

                // ── License / Account Status / Redemption Code check ──
                GoogleBtnText.Text = "Checking license…";
                var licenseResult = await LicenseService.VerifyLicenseAsync(uid, idToken);

                // Account banned/suspended/deactivated/trial-expired
                if (!licenseResult.Licensed && !licenseResult.NeedsCode
                    && !licenseResult.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
                {
                    AuthService.Logout();
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    ShowAccountStatusPopup(licenseResult);
                    return;
                }

                if (!licenseResult.Licensed && licenseResult.NeedsCode)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    var redeemDialog = new RedeemCodeDialog { Owner = this };
                    redeemDialog.ShowDialog();

                    if (!redeemDialog.CodeActivated)
                    {
                        AuthService.Logout();
                        ShowError("License activation required to use LegitX V2.");
                        ShakeWindow();
                        return;
                    }
                }

                LoginSuccessful = true;
                LoadingPanel.Visibility = Visibility.Collapsed;
                GoogleBtnText.Text = "✓ Success";
                await Task.Delay(600);
                Close();
            }
            else
            {
                ShowError(message);
                ShakeWindow();
            }
        }
        catch
        {
            ShowError("Google sign-in failed. Please try again.");
            ShakeWindow();
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            GoogleBtn.IsEnabled = true;
            GoogleBtnText.Text = "Continue with Google";
            LoginBtn.IsEnabled = true;
            _isBusy = false;
        }
    }

    // ── Input field focus effects ──
    private void InputField_GotFocus(object sender, RoutedEventArgs e)
    {
        var border = FindParentBorder((DependencyObject)sender);
        if (border != null)
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEF4444"));
    }

    private void InputField_LostFocus(object sender, RoutedEventArgs e)
    {
        var border = FindParentBorder((DependencyObject)sender);
        if (border != null)
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF27273a"));

        // Update password placeholder
        if (sender == PasswordBox)
            PasswordPlaceholder.Visibility = PasswordBox.Password.Length == 0
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Border? FindParentBorder(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is Border b && b.Name != "")
                return b;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    // ── Enter / Tab key handling ──
    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            DoLogin();
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
            if (sender == EmailBox)
                PasswordBox.Focus();
        }

        // Hide messages on typing
        ErrorText.Visibility = Visibility.Collapsed;
        SuccessText.Visibility = Visibility.Collapsed;
    }

    // ── Show/hide password ──
    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;

        if (_passwordVisible)
        {
            // Show password as plain text
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            PasswordTextBox.Focus();
            PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            PasswordPlaceholder.Visibility = PasswordTextBox.Text.Length == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            // Hide password back to dots
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
            PasswordPlaceholder.Visibility = PasswordBox.Password.Length == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // Update eye icon appearance
        EyeIcon.Opacity = _passwordVisible ? 1.0 : 0.5;
        EyeIcon.Fill = _passwordVisible
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFa1a1aa"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4a4a62"));
    }

    // ── Login button ──
    private void LoginBtn_Click(object sender, RoutedEventArgs e) => DoLogin();

    private async void DoLogin()
    {
        if (_isBusy) return;

        var email = EmailBox.Text.Trim();
        var password = _passwordVisible ? PasswordTextBox.Text : PasswordBox.Password;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowError("Please fill in all fields.");
            ShakeWindow();
            return;
        }

        _isBusy = true;
        LoginBtn.IsEnabled = false;
        GoogleBtn.IsEnabled = false;
        LoginBtnText.Text = "Signing in…";
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
        SuccessText.Visibility = Visibility.Collapsed;

        try
        {
            var (success, message) = await AuthService.LoginAsync(email, password);

            if (success)
            {
                // ── Immediately store user email in Firestore so the admin panel can see the user ──
                var uid = AuthService.GetUserId();
                var idToken = AuthService.GetIdToken();
                try
                {
                    await FirebaseFeatureService.StoreUserEmailAsync(uid, email.Trim(), idToken);
                }
                catch { /* silent — non-critical */ }

                // ── Hardware ID lockdown check ──
                LoginBtnText.Text = "Verifying PC…";
                var hwResult = await HardwareIdService.VerifyAndRegisterAsync(uid, idToken);

                if (!hwResult.Allowed)
                {
                    // HWID mismatch → block login, log out the session
                    AuthService.Logout();
                    ShowError(hwResult.Reason);
                    ShakeWindow();
                    return;
                }

                // ── HWID Ban check — catches banned hardware even with new accounts ──
                LoginBtnText.Text = "Checking restrictions…";
                var hwidBan = await LicenseService.CheckHwidBanAsync(idToken);
                if (hwidBan.Banned)
                {
                    AuthService.Logout();
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    AccountStatusDialog.Show(AccountStatusType.HwidBanned, hwidBan.Reason, owner: this);
                    return;
                }

                // ── Layer 5: Bind server-side session token ──
                LoginBtnText.Text = "Binding session…";
                await SessionTokenService.BindSessionAsync(uid, idToken);

                // ── License / Account Status / Redemption Code check ──
                LoginBtnText.Text = "Checking license…";
                var licenseResult = await LicenseService.VerifyLicenseAsync(uid, idToken);

                // Account banned/suspended/deactivated/trial-expired
                if (!licenseResult.Licensed && !licenseResult.NeedsCode
                    && !licenseResult.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
                {
                    AuthService.Logout();
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    ShowAccountStatusPopup(licenseResult);
                    return;
                }

                if (!licenseResult.Licensed && licenseResult.NeedsCode)
                {
                    // User needs to enter a redemption code
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    var redeemDialog = new RedeemCodeDialog { Owner = this };
                    redeemDialog.ShowDialog();

                    if (!redeemDialog.CodeActivated)
                    {
                        AuthService.Logout();
                        ShowError("License activation required to use LegitX V2.");
                        ShakeWindow();
                        return;
                    }
                }

                LoginSuccessful = true;
                LoadingPanel.Visibility = Visibility.Collapsed;
                LoginBtnText.Text = "✓ Success";
                await Task.Delay(600);
                Close();
            }
            else
            {
                ShowError(message);
                ShakeWindow();
            }
        }
        catch
        {
            ShowError("Network error. Please check your connection.");
            ShakeWindow();
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            LoginBtn.IsEnabled = true;
            GoogleBtn.IsEnabled = true;
            LoginBtnText.Text = "Sign In";
            _isBusy = false;
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private async void ShakeWindow()
    {
        var originalLeft = Left;
        for (int i = 0; i < 4; i++)
        {
            Left = originalLeft + 6;
            await Task.Delay(40);
            Left = originalLeft - 6;
            await Task.Delay(40);
        }
        Left = originalLeft;
    }

    /// <summary>
    /// Maps a LicenseCheckResult to the beautiful AccountStatusDialog popup.
    /// </summary>
    private void ShowAccountStatusPopup(LicenseService.LicenseCheckResult result)
    {
        var statusType = result.Issue switch
        {
            LicenseService.AccountIssue.Banned => AccountStatusType.Banned,
            LicenseService.AccountIssue.Suspended => AccountStatusType.Suspended,
            LicenseService.AccountIssue.Deactivated => AccountStatusType.Deactivated,
            LicenseService.AccountIssue.TrialExpired => AccountStatusType.TrialExpired,
            LicenseService.AccountIssue.LicenseRevoked => AccountStatusType.LicenseRevoked,
            LicenseService.AccountIssue.HwidBanned => AccountStatusType.HwidBanned,
            LicenseService.AccountIssue.NoLicense => AccountStatusType.NoLicense,
            LicenseService.AccountIssue.SessionInvalid => AccountStatusType.SessionInvalid,
            LicenseService.AccountIssue.ServerDown => AccountStatusType.ServerDown,
            _ => AccountStatusType.NoLicense
        };

        // Build detail rows for trial expired
        (string, string)? d1 = null, d2 = null, d3 = null;
        if (result.Issue == LicenseService.AccountIssue.TrialExpired)
        {
            d1 = ("License Type", "Trial");
            d2 = ("Trial Duration", $"{result.TrialDaysTotal} days");
            if (result.TrialExpiresAt.HasValue)
                d3 = ("Expired On", result.TrialExpiresAt.Value.ToString("MMM dd, yyyy"));
        }

        AccountStatusDialog.Show(statusType, result.Reason, d1, d2, d3, owner: this);
    }
}
