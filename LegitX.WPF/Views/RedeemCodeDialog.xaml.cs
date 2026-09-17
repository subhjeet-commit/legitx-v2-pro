using System.Windows;
using System.Windows.Input;
using LegitX.WPF.Services;

namespace LegitX.WPF.Views;

public partial class RedeemCodeDialog : Window
{
    /// <summary>True if the user successfully activated a code.</summary>
    public bool CodeActivated { get; private set; }

    private bool _isBusy;

    public RedeemCodeDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        CodeActivated = false;
        Close();
    }

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = TryActivateAsync();
        }
    }

    private async void ActivateBtn_Click(object sender, MouseButtonEventArgs e)
    {
        await TryActivateAsync();
    }

    private async Task TryActivateAsync()
    {
        if (_isBusy) return;

        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            ShowError("Please enter a redemption code.");
            ShakeWindow();
            return;
        }

        _isBusy = true;
        ActivateBtnText.Text = "Verifying…";
        ErrorText.Visibility = Visibility.Collapsed;
        SuccessText.Visibility = Visibility.Collapsed;

        try
        {
            var uid = AuthService.GetUserId();
            var idToken = AuthService.GetIdToken();

            var result = await LicenseService.RedeemCodeAsync(code, uid, idToken);

            if (result.Success)
            {
                SuccessText.Text = result.Message;
                SuccessText.Visibility = Visibility.Visible;
                CodeActivated = true;
                await Task.Delay(800);
                Close();
            }
            else
            {
                ShowError(result.Message);
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
            ActivateBtnText.Text = "Activate";
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
}
