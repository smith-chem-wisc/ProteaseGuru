using System.Windows;
using System.Windows.Controls;
using GuiFunctions;


namespace GUI;
/// <summary>
/// Interaction logic for DigestionConditions.xaml
/// </summary>
public partial class DigestionConditions : UserControl
{
    public DigestionConditions()
    {
        InitializeComponent();
    }

    //triggers the opening of the customprotease window
    private void AddCustomProtease_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DigestionConditionsSetupViewModel viewModel)
            return;

        var dialog = new CustomProteaseWindow();
        dialog.ShowDialog();

        if (dialog.proteaseAdded)
            viewModel.PopulateProteaseCollection();
    }
}
