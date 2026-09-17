using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LegitX.WPF.Views;

/// <summary>
/// The type of account status issue. Controls icon, colors, and messaging.
/// </summary>
public enum AccountStatusType
{
    Banned,
    Suspended,
    Deactivated,
    HwidBanned,
    TrialExpired,
    LicenseRevoked,
    NoLicense,
    SessionInvalid,
    ServerDown
}

/// <summary>
/// Beautiful popup dialog that shows account status messages
/// (banned, suspended, deactivated, trial expired, etc.)
/// with proper icons, colors, and details.
/// </summary>
public partial class AccountStatusDialog : Window
{
    public AccountStatusDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Shows the dialog with the given status type and optional detail lines.
    /// </summary>
    public static void Show(
        AccountStatusType statusType,
        string? customMessage = null,
        (string Label, string Value)? detail1 = null,
        (string Label, string Value)? detail2 = null,
        (string Label, string Value)? detail3 = null,
        Window? owner = null)
    {
        var dlg = new AccountStatusDialog();
        if (owner != null) dlg.Owner = owner;

        dlg.ConfigureForStatus(statusType, customMessage);

        // Set detail rows
        if (detail1 != null)
        {
            dlg.DetailsCard.Visibility = Visibility.Visible;
            dlg.DetailRow1.Visibility = Visibility.Visible;
            dlg.DetailLabel1.Text = detail1.Value.Label;
            dlg.DetailValue1.Text = detail1.Value.Value;
        }
        if (detail2 != null)
        {
            dlg.DetailsCard.Visibility = Visibility.Visible;
            dlg.DetailRow2.Visibility = Visibility.Visible;
            dlg.DetailLabel2.Text = detail2.Value.Label;
            dlg.DetailValue2.Text = detail2.Value.Value;
        }
        if (detail3 != null)
        {
            dlg.DetailsCard.Visibility = Visibility.Visible;
            dlg.DetailRow3.Visibility = Visibility.Visible;
            dlg.DetailLabel3.Text = detail3.Value.Label;
            dlg.DetailValue3.Text = detail3.Value.Value;
        }

        dlg.ShowDialog();
    }

    private void ConfigureForStatus(AccountStatusType statusType, string? customMessage)
    {
        var borderColor = "#FFEF4444"; // Default red

        switch (statusType)
        {
            case AccountStatusType.Banned:
                StatusIcon.Text = "⛔";
                TitleText.Text = "ACCOUNT PERMANENTLY BANNED";
                SubtitleText.Text = "Your account has been permanently blocked";
                MessageText.Text = customMessage ??
                    "Your account and hardware have been blocked by an administrator.\n" +
                    "This ban is tied to your PC hardware — creating a new account will not bypass this restriction.\n\n" +
                    "All access to LegitX V2 has been permanently revoked.";
                ActionBtnText.Text = "Close Application";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEF4444"));
                borderColor = "#FFEF4444";
                break;

            case AccountStatusType.HwidBanned:
                StatusIcon.Text = "🖥️";
                TitleText.Text = "HARDWARE BANNED";
                SubtitleText.Text = "This computer has been permanently blocked";
                MessageText.Text = customMessage ??
                    "The hardware of this computer has been blocked by an administrator.\n" +
                    "LegitX V2 cannot be used on this machine.\n\n" +
                    "This ban is permanent and hardware-based — reinstalling Windows, " +
                    "creating new accounts, or any other method will NOT bypass it.";
                ActionBtnText.Text = "Close Application";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEF4444"));
                borderColor = "#FFEF4444";
                break;

            case AccountStatusType.Suspended:
                StatusIcon.Text = "⚠️";
                TitleText.Text = "ACCOUNT SUSPENDED";
                SubtitleText.Text = "Your account has been suspended by an administrator";
                MessageText.Text = customMessage ??
                    "An administrator has suspended your account.\n" +
                    "Your license has been revoked and all features are disabled.\n\n" +
                    "Contact support if you believe this is an error.";
                ActionBtnText.Text = "Understood";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBBF24"));
                borderColor = "#FFFBBF24"; // Yellow/amber
                break;

            case AccountStatusType.Deactivated:
                StatusIcon.Text = "🚫";
                TitleText.Text = "ACCOUNT DEACTIVATED";
                SubtitleText.Text = "Your account has been deactivated";
                MessageText.Text = customMessage ??
                    "An administrator has deactivated your account.\n" +
                    "All features have been disabled.\n\n" +
                    "Contact support to reactivate your account.";
                ActionBtnText.Text = "Understood";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF6B35"));
                borderColor = "#FFFF6B35"; // Orange
                break;

            case AccountStatusType.TrialExpired:
                StatusIcon.Text = "⏰";
                TitleText.Text = "TRIAL PERIOD EXPIRED";
                SubtitleText.Text = "Your trial license has ended";
                MessageText.Text = customMessage ??
                    "Your trial period has expired and your license is no longer active.\n\n" +
                    "To continue using LegitX V2, please purchase a permanent license " +
                    "or contact your reseller for a new key.";
                ActionBtnText.Text = "Understood";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B5CF6"));
                borderColor = "#FF8B5CF6"; // Purple
                break;

            case AccountStatusType.LicenseRevoked:
                StatusIcon.Text = "🔑";
                TitleText.Text = "LICENSE REVOKED";
                SubtitleText.Text = "Your license has been revoked by an administrator";
                MessageText.Text = customMessage ??
                    "An administrator has revoked your LegitX V2 license.\n\n" +
                    "You will need a new license key to continue using the application.\n" +
                    "Contact your reseller or purchase a new key at legitx.com.";
                ActionBtnText.Text = "Understood";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEF4444"));
                borderColor = "#FFEF4444";
                break;

            case AccountStatusType.NoLicense:
                StatusIcon.Text = "🔒";
                TitleText.Text = "NO ACTIVE LICENSE";
                SubtitleText.Text = "License activation required";
                MessageText.Text = customMessage ??
                    "No active license was found for your account.\n\n" +
                    "Please enter a valid redemption code to activate LegitX V2.";
                ActionBtnText.Text = "Understood";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF71717a"));
                borderColor = "#FF3f3f58";
                break;

            case AccountStatusType.SessionInvalid:
                StatusIcon.Text = "🔐";
                TitleText.Text = "SESSION EXPIRED";
                SubtitleText.Text = "Your session is no longer valid";
                MessageText.Text = customMessage ??
                    "Your login session has expired or been invalidated.\n\n" +
                    "Please sign in again to continue using LegitX V2.";
                ActionBtnText.Text = "Sign In Again";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B82F6"));
                borderColor = "#FF3B82F6"; // Blue
                break;

            case AccountStatusType.ServerDown:
                StatusIcon.Text = "📡";
                TitleText.Text = "CONNECTION LOST";
                SubtitleText.Text = "Unable to reach LegitX servers";
                MessageText.Text = customMessage ??
                    "The connection to LegitX servers has been lost for an extended period.\n\n" +
                    "Please check your internet connection and try again.";
                ActionBtnText.Text = "Close";
                TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF71717a"));
                borderColor = "#FF3f3f58";
                break;
        }

        // Set border glow color
        DialogBorder.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(borderColor));

        // Update icon glow color
        var glowColor = (Color)ColorConverter.ConvertFromString(borderColor);
        IconGlow1.Color = Color.FromArgb(48, glowColor.R, glowColor.G, glowColor.B);
        IconGlow2.Color = Color.FromArgb(16, glowColor.R, glowColor.G, glowColor.B);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Entrance animation — subtle scale + fade in
        var scaleX = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(250))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scaleY = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(250))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

        var transform = new ScaleTransform(0.92, 0.92);
        DialogBorder.RenderTransform = transform;
        DialogBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        BeginAnimation(OpacityProperty, fadeIn);

        // Pulse animation on the icon
        var pulse = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(800))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        IconCircle.RenderTransform = new ScaleTransform(1, 1);
        IconCircle.RenderTransformOrigin = new Point(0.5, 0.5);
        ((ScaleTransform)IconCircle.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ((ScaleTransform)IconCircle.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void ActionBtn_Click(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Allow dragging the dialog
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
}
