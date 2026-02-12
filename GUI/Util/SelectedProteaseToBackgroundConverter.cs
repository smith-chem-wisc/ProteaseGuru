using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GUI;

public class SelectedProteaseToBackgroundConverter : IValueConverter
{
    public static SolidColorBrush NotSelected = new(Color.FromRgb(213, 213, 213));
    public static SolidColorBrush Selected = new(Color.FromRgb(239, 239, 239));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value is not bool isSelected)
            return NotSelected;

        return isSelected ? Selected : NotSelected;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
