using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LegitX.WPF.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Support int == parameter comparison (for tab visibility: SelectedTab == "0")
        if (value is int intVal && parameter is string ps && int.TryParse(ps, out int target))
            return intVal == target ? Visibility.Visible : Visibility.Collapsed;

        bool boolVal = value is bool b && b;
        bool invert = parameter is string s && s == "Invert";
        if (invert) boolVal = !boolVal;
        return boolVal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool vis = value is Visibility v && v == Visibility.Visible;
        bool invert = parameter is string s && s == "Invert";
        return invert ? !vis : vis;
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>
/// true → Collapsed, false → Visible (inverse of BoolToVisibility).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>
/// Converts a text length to Visibility.
/// Length == 0 → Visible (show placeholder), Length > 0 → Collapsed (hide placeholder).
/// </summary>
public class InverseLengthToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int len && len > 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns Visible if the string is not null or empty, Collapsed otherwise.
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
