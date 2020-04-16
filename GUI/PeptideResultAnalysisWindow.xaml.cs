using Proteomics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Engine;
using Tasks;
using Proteomics.ProteolyticDigestion;
using System.IO;
using System.Globalization;
using static Tasks.ProteaseGuruTask;
using MzLibUtil;

namespace GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class PeptideResultAnalysisWindow : Window
    {
        
        public PeptideResultAnalysisWindow(ProteaseGuruTask task)
        {
            InitializeComponent();
            
        }
        internal PeptideResultAnalysisTask TheTask { get; private set; }
    }
}
