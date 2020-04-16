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
using System.ComponentModel;

namespace GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class PeptideResultAnalysisWindow : Window
    {
        
        public PeptideResultAnalysisWindow(PeptideResultAnalysisTask task)
        {
            InitializeComponent();
            TheTask = task ?? new PeptideResultAnalysisTask();           
            PopulateChoices();
            UpdateFieldsFromTask(TheTask);            

           
            base.Closing += this.OnClosing;
        }
        internal PeptideResultAnalysisTask TheTask { get; private set; }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            
        }
        private void PopulateChoices()
        { 
        
        }

        private void UpdateFieldsFromTask(PeptideResultAnalysisTask task)
        { 
        
        }
        private void AddPsmtsvFile_Click( object sender, RoutedEventArgs e)
        {

        }
        private void ClearPsmtsvFile_Click(object sender, RoutedEventArgs e)
        { 
        
        }
        private void AddPeptideResultAnalysisTask_Click(object sender, RoutedEventArgs e)
        { 
        
        }

    }
}
