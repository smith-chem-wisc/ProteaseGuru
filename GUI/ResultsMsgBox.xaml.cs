using System.Windows;

namespace GUI
{
    /// <summary>
    /// Interaction logic for ResultsMsgBox.xaml
    /// </summary>
    public partial class ResultsMsgBox : Window
    {
        public ResultsMsgBox()
        {
            InitializeComponent();

            CloseButton.Click += new RoutedEventHandler(CloseButton_Click);
            ResultsButton.Click += new RoutedEventHandler(ResultsButton_Click);
        }

        static ResultsMsgBox MsgBox;
        static MessageBoxResult result = MessageBoxResult.No;

        // this method will be called from task layer when digestions tasks are done
        public static new MessageBoxResult Show()
        {
            MsgBox = new ResultsMsgBox();
            MsgBox.ShowDialog();
            return result;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.OK;
            MsgBox.Close();
        }

        private void ResultsButton_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.Yes;
            MsgBox.Close();
        }
    }
}
