using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace GUI;

public class BooleanToVisibilityConverter: IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value is not bool isVisible)
            return Visibility.Collapsed;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
