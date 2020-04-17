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
    public partial class CustomProteaseWindow : Window
    {        
        public CustomProteaseWindow()
        {
            InitializeComponent();          
            
        }

        private void SaveCustomProtease_Click(object sender, RoutedEventArgs e)
        {
            string proteaseDirectory = System.IO.Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion");
            string proteaseFilePath = System.IO.Path.Combine(proteaseDirectory, @"protease.tsv");
            List<string> proteaseFileText = new List<string>();
            proteaseFileText = File.ReadAllLines(proteaseFilePath).ToList();

            string name = proteaseNameTextBox.Text;
            string allCleavageResidues = sequencesInducingCleavageTextBox.Text;
            string allResiduesStoppingCleavage = sequencesPreventingCleavageBox.Text;
            string cleavageTerminus = cleavageTerminusComboBox.SelectedItem.ToString();
            string cleavageSpecificity = cleavageSpecificityComboBox.SelectedItem.ToString();
            string psiAccession = psiAccessionNumber.Text;
            string psiNames = psiName.Text;
        }

        private void ClearCustomProtease_Click()
        { 
        
        }

    }
}
