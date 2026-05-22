using System.Globalization;
using System.Windows.Data;
using GuiFunctions;

namespace GUI;

public class CollapseOnProteinModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Collapse the element if RNA mode is enabled
        return GuiGlobalParamsViewModel.Instance == null || !GuiGlobalParamsViewModel.Instance.IsRnaMode
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
