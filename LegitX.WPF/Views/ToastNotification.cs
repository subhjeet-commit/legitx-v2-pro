using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LegitX.WPF.Views;

public partial class ToastNotification : Window
{
    private readonly DispatcherTimer _autoClose;

    public ToastNotification()
    {
        InitializeComponent();
        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            SlideOut();
        };
    }

    /// <summary>
    /// Shows a toast notification in the bottom-right corner of the screen.
    /// </summary>
    public static void Show(string title, string subtitle, bool isActive)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var toast = new ToastNotification();
            toast.Configure(title, subtitle, isActive);
            toast.PositionBottomRight();
            toast.Show();
            toast.SlideIn();
        });
    }

    private void Configure(string title, string subtitle, bool isActive)
    {
        TitleText.Text = title;
        SubText.Text = subtitle;

        var color = isActive
            ? Color.FromRgb(0x10, 0xb9, 0x81)   // green (#10b981)
            : Color.FromRgb(0xfb, 0xbf, 0x24);   // yellow (#fbbf24)

        IconGlow.Color = color;
        IconDot.Color = color;
        TitleText.Foreground = new SolidColorBrush(color);
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void SlideIn()
    {
        // Simple opacity + translate animation (no storyboard resource needed)
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);

        var slide = new ThicknessAnimation(
            new Thickness(0, 20, 0, 0), new Thickness(0),
            TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ToastCard.BeginAnimation(MarginProperty, slide);

        _autoClose.Start();
    }

    private async void SlideOut()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeOut);

        var slide = new ThicknessAnimation(
            new Thickness(0), new Thickness(0, 20, 0, 0),
            TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ToastCard.BeginAnimation(MarginProperty, slide);

        await Task.Delay(450);
        Close();
    }
}
