using System.Windows;
using System.Windows.Controls;

namespace GUI
{
    public enum ProteaseDisplayMode
    {
        Full,
        Compact
    }

    /// <summary>
    /// Interaction logic for ProteaseSpecificDigestionControl.xaml
    /// </summary>
    public partial class ProteaseSpecificDigestionControl : UserControl
    {
        public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
            nameof(DisplayMode),
            typeof(ProteaseDisplayMode),
            typeof(ProteaseSpecificDigestionControl),
            new PropertyMetadata(ProteaseDisplayMode.Full));

        public ProteaseDisplayMode DisplayMode
        {
            get => (ProteaseDisplayMode)GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        public ProteaseSpecificDigestionControl()
        {
            InitializeComponent();
        }
    }
}
