using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OrthoPlanner.App.Converters;

/// <summary>Bool-to-Visibility. Set Invert=true for "visible when false".</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        return (b ^ Invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Invert ? value is Visibility.Collapsed : value is Visibility.Visible;
}

/// <summary>Null-to-Visibility. Set Invert=true for "visible when NOT null".</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Invert
            ? (value != null ? Visibility.Visible : Visibility.Collapsed)
            : (value == null ? Visibility.Visible : Visibility.Collapsed);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            if (targetType == typeof(int) && int.TryParse(parameter?.ToString(), out int i)) return i;
            return Enum.Parse(targetType, parameter!.ToString()!);
        }
        return Binding.DoNothing;
    }
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && parameter is string p && int.TryParse(p, out int target))
            return i == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
