using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LegitX.WPF.Services;

namespace LegitX.WPF.Views;

public partial class ShortcutSettingsDialog : Window
{
    private readonly Dictionary<string, (Key key, ModifierKeys modifiers)> _currentBindings;
    private TextBlock? _activeKeyBox;
    private string? _activeShortcutId;

    /// <summary>
    /// Raised when user saves shortcuts — passes the new bindings.
    /// </summary>
    public event Action<Dictionary<string, (Key key, ModifierKeys modifiers)>>? ShortcutsSaved;

    public ShortcutSettingsDialog()
    {
        InitializeComponent();
        _currentBindings = ShortcutService.GetAllShortcuts();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildShortcutList();
    }

    private void BuildShortcutList()
    {
        ShortcutList.Children.Clear();

        // Section header
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        headerPanel.Children.Add(new Ellipse
        {
            Width = 6, Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "KEYBOARD SHORTCUTS",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122))
        });
        ShortcutList.Children.Add(headerPanel);

        foreach (var shortcut in ShortcutService.AllShortcuts)
        {
            var (key, mod) = _currentBindings.ContainsKey(shortcut.Id)
                ? _currentBindings[shortcut.Id]
                : (shortcut.DefaultKey, shortcut.DefaultModifiers);

            AddShortcutRow(shortcut, key, mod);
        }

        // Add info section about prerequisites
        var prereqHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 12) };
        prereqHeader.Children.Add(new Ellipse
        {
            Width = 6, Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        prereqHeader.Children.Add(new TextBlock
        {
            Text = "SHORTCUT INFO",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122))
        });
        ShortcutList.Children.Add(prereqHeader);

        AddInfoCard("The Toggle Visibility hotkey is registered as a system-wide hotkey via Windows API.");
        AddInfoCard("It works globally even when the app is minimized or hidden.");
        AddInfoCard("Changes take effect immediately after saving. Restart may be needed for the visibility hotkey.");
    }

    private void AddShortcutRow(ShortcutService.ShortcutBinding shortcut, Key key, ModifierKeys mod)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // Left: name + description
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = shortcut.DisplayName,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216))
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = shortcut.Description,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(nameStack, 0);

        // Right: key binding box (clickable to rebind)
        var keyBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand,
            MinWidth = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Tag = shortcut.Id
        };

        var keyText = new TextBlock
        {
            Text = ShortcutService.FormatShortcut(key, mod),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = shortcut.Id
        };

        keyBox.Child = keyText;
        keyBox.MouseLeftButtonDown += KeyBox_Click;
        Grid.SetColumn(keyBox, 1);

        grid.Children.Add(nameStack);
        grid.Children.Add(keyBox);

        card.Child = grid;
        ShortcutList.Children.Add(card);
    }

    private void AddInfoCard(string text)
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

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new Path
        {
            Data = Geometry.Parse("M8,0 A8,8 0 1,1 8,16 A8,8 0 1,1 8,0 M8,4 L8,4.5 M8,7 L8,12"),
            Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            StrokeThickness = 1.2,
            Width = 11, Height = 11,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center
        });

        card.Child = stack;
        ShortcutList.Children.Add(card);
    }

    private void KeyBox_Click(object sender, MouseButtonEventArgs e)
    {
        // Deactivate previous
        if (_activeKeyBox != null)
        {
            _activeKeyBox.Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170));
            if (_activeKeyBox.Parent is Border prevBorder)
                prevBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64));
        }

        Border clickedBorder;
        if (sender is Border b)
            clickedBorder = b;
        else
            return;

        _activeShortcutId = clickedBorder.Tag?.ToString();
        _activeKeyBox = clickedBorder.Child as TextBlock;

        if (_activeKeyBox != null)
        {
            _activeKeyBox.Text = "Press a key...";
            _activeKeyBox.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            clickedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }

        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_activeKeyBox == null || _activeShortcutId == null) return;

        // Ignore pure modifier keys
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var modifiers = Keyboard.Modifiers;

        // Escape cancels the rebind
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            var (origKey, origMod) = _currentBindings[_activeShortcutId];
            _activeKeyBox.Text = ShortcutService.FormatShortcut(origKey, origMod);
            _activeKeyBox.Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170));
            if (_activeKeyBox.Parent is Border border)
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64));
            _activeKeyBox = null;
            _activeShortcutId = null;
            e.Handled = true;
            return;
        }

        // Apply the new binding
        _currentBindings[_activeShortcutId] = (key, modifiers);
        _activeKeyBox.Text = ShortcutService.FormatShortcut(key, modifiers);
        _activeKeyBox.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));

        if (_activeKeyBox.Parent is Border bd)
            bd.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 64));

        // Briefly flash green then restore
        var greenBox = _activeKeyBox;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        timer.Tick += (_, _) =>
        {
            greenBox.Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170));
            timer.Stop();
        };
        timer.Start();

        _activeKeyBox = null;
        _activeShortcutId = null;
        e.Handled = true;
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in ShortcutService.AllShortcuts)
            _currentBindings[s.Id] = (s.DefaultKey, s.DefaultModifiers);

        BuildShortcutList();
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        ShortcutService.SaveAllShortcuts(_currentBindings);
        ShortcutsSaved?.Invoke(_currentBindings);
        Close();
    }
}
