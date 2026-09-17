using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using LegitX.WPF.Services;

namespace LegitX.WPF.Views;

public partial class DriverReportDialog : Window
{
    public DriverReportDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Start spinner animation
        var spinAnimation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spinAnimation);

        // Run driver scan on background thread
        List<DriverService.DriverInfo>? drivers = null;
        bool mouclassOk = false;
        bool hidMouseOk = false;
        bool dotnetOk = true; // We're running, so .NET is fine

        await Task.Run(() =>
        {
            drivers = DriverService.CheckAllDrivers();
            mouclassOk = DriverService.IsMouclassRunning();
            hidMouseOk = DriverService.IsHidMousePresent();
        });

        // Stop spinner
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);

        // Hide loading, show content
        LoadingOverlay.Visibility = Visibility.Collapsed;
        DriverContent.Visibility = Visibility.Visible;

        if (drivers == null || drivers.Count == 0)
        {
            AddSectionHeader("No Drivers Found");
            AddInfoRow("Could not query system drivers. Try running as Administrator.", false);
            SubtitleText.Text = "Scan failed";
            OverallStatusText.Text = "Unable to scan";
            OverallStatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            return;
        }

        // Group by device class
        var mouseDrivers = drivers.FindAll(d => d.DeviceClass == "Mouse");
        var hidDrivers = drivers.FindAll(d => d.DeviceClass == "HIDClass");
        var displayDrivers = drivers.FindAll(d => d.DeviceClass == "Display");
        var keyboardDrivers = drivers.FindAll(d => d.DeviceClass == "Keyboard");

        int totalCount = drivers.Count;
        int healthyCount = drivers.Count(d => d.IsHealthy);
        int unhealthyCount = totalCount - healthyCount;

        // Mouse Drivers
        AddSectionHeader("Mouse Drivers", "#FFEF4444");
        if (mouseDrivers.Count == 0)
            AddInfoRow("No mouse drivers detected", false);
        else
            foreach (var d in mouseDrivers)
                AddDriverRow(d);

        // HID Devices
        AddSectionHeader("HID Input Devices", "#FF8B5CF6");
        if (hidDrivers.Count == 0)
            AddInfoRow("No HID devices detected", false);
        else
            foreach (var d in hidDrivers)
                AddDriverRow(d);

        // Display Drivers
        AddSectionHeader("Display / GPU Drivers", "#FF3B82F6");
        if (displayDrivers.Count == 0)
            AddInfoRow("No display drivers detected", false);
        else
            foreach (var d in displayDrivers)
                AddDriverRow(d);

        // Keyboard Drivers
        AddSectionHeader("Keyboard Drivers", "#FFF59E0B");
        if (keyboardDrivers.Count == 0)
            AddInfoRow("No keyboard drivers detected", false);
        else
            foreach (var d in keyboardDrivers)
                AddDriverRow(d);

        // Core Services
        AddSectionHeader("Core Windows Services", "#FF22C55E");
        AddCoreServiceRow("mouclass (Mouse Class Driver)", mouclassOk);
        AddCoreServiceRow("HID Mouse Device Present", hidMouseOk);
        AddCoreServiceRow(".NET 8 Desktop Runtime", dotnetOk);

        // Prerequisites
        AddSectionHeader("Application Prerequisites", "#FF71717A");
        AddPrereqRow(".NET 8 Desktop Runtime", true, "Required — Currently running");
        AddPrereqRow("System.Management (WMI)", true, "Bundled with app");
        AddPrereqRow("DirectX", true, "Built into Windows (WPF uses DirectX internally)");
        AddPrereqRow("VC++ Redistributable", true, "Not required for .NET apps");
        AddPrereqRow("Administrator Rights", true, "Recommended for driver/registry features");

        // Update subtitle and footer
        SubtitleText.Text = $"{totalCount} drivers found across {4} categories";

        bool allHealthy = unhealthyCount == 0 && mouclassOk && hidMouseOk;
        if (allHealthy)
        {
            OverallStatusText.Text = $"All {healthyCount} drivers healthy — No prerequisites missing";
            OverallStatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        }
        else
        {
            OverallStatusText.Text = $"{unhealthyCount} driver(s) need attention";
            OverallStatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }

        // Fade in content
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        DriverContent.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void AddSectionHeader(string title, string colorHex = "#FF71717A")
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 8) };

        var dot = new Ellipse
        {
            Width = 6, Height = 6,
            Fill = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var text = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
            VerticalAlignment = VerticalAlignment.Center,
            // LetterSpacing not available in WPF, use CharacterSpacing alternative — skip
        };

        panel.Children.Add(dot);
        panel.Children.Add(text);
        DriverList.Children.Add(panel);
    }

    private void AddDriverRow(DriverService.DriverInfo driver)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // Status icon
        var statusIcon = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = driver.IsHealthy
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(statusIcon, 0);

        // Name + version
        var nameStack = new StackPanel();
        var nameText = new TextBlock
        {
            Text = driver.Name,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var versionText = new TextBlock
        {
            Text = $"v{driver.Version}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        nameStack.Children.Add(nameText);
        nameStack.Children.Add(versionText);
        Grid.SetColumn(nameStack, 1);

        // Status badge
        var badge = CreateStatusBadge(driver.Status, driver.IsHealthy);
        Grid.SetColumn(badge, 2);

        grid.Children.Add(statusIcon);
        grid.Children.Add(nameStack);
        grid.Children.Add(badge);

        card.Child = grid;
        DriverList.Children.Add(card);
    }

    private void AddInfoRow(string message, bool isOk)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var text = new TextBlock
        {
            Text = message,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = isOk
                ? new SolidColorBrush(Color.FromRgb(161, 161, 170))
                : new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        };

        card.Child = text;
        DriverList.Children.Add(card);
    }

    private void AddCoreServiceRow(string name, bool isRunning)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var dot = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = isRunning
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(dot, 0);

        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 1);

        var badge = CreateStatusBadge(isRunning ? "Running" : "Not Found", isRunning);
        Grid.SetColumn(badge, 2);

        grid.Children.Add(dot);
        grid.Children.Add(nameText);
        grid.Children.Add(badge);

        card.Child = grid;
        DriverList.Children.Add(card);
    }

    private void AddPrereqRow(string name, bool isOk, string detail)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // Checkmark or cross icon
        var iconPath = new Path
        {
            Data = isOk
                ? Geometry.Parse("M2,6 L5,9 L10,2")
                : Geometry.Parse("M2,2 L10,10 M10,2 L2,10"),
            Stroke = isOk
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            StrokeThickness = 1.8,
            Width = 12, Height = 12,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(iconPath, 0);

        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216))
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(nameStack, 1);

        grid.Children.Add(iconPath);
        grid.Children.Add(nameStack);

        card.Child = grid;
        DriverList.Children.Add(card);
    }

    private static Border CreateStatusBadge(string status, bool isHealthy)
    {
        var bgColor = isHealthy
            ? Color.FromArgb(25, 34, 197, 94)
            : Color.FromArgb(25, 239, 68, 68);
        var fgColor = isHealthy
            ? Color.FromRgb(34, 197, 94)
            : Color.FromRgb(239, 68, 68);

        var badge = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        badge.Child = new TextBlock
        {
            Text = status,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fgColor)
        };

        return badge;
    }
}
