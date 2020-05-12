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
        public bool proteaseAdded = false;
        public CustomProteaseWindow()
        {
            InitializeComponent();
            PopulateListBoxes();            
        }
        private void PopulateListBoxes()
        {
            cleavageSpecificityListBox.Items.Add("Full");
            cleavageSpecificityListBox.Items.Add("Semi");
            cleavageTerminusListBox.Items.Add("C");
            cleavageTerminusListBox.Items.Add("N");
        }
        private void SaveCustomProtease_Click(object sender, RoutedEventArgs e)
        {
            string proteaseDirectory = System.IO.Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion");
            string proteaseFilePath = System.IO.Path.Combine(proteaseDirectory, @"proteases.tsv");
            List<string> proteaseFileText = new List<string>();
            proteaseFileText = File.ReadAllLines(proteaseFilePath).ToList();

            string name = proteaseNameTextBox.Text;
            string allCleavageResidues = sequencesInducingCleavageTextBox.Text;
            string allResiduesStoppingCleavage = sequencesPreventingCleavageBox.Text;
            var cleavageTerminus = (string)cleavageTerminusListBox.SelectedItem;
            var cleavageSpecificity = (string)cleavageSpecificityListBox.SelectedItem;
            string psiAccession = psiAccessionNumber.Text;
            string psiNames = psiName.Text;

            string proteaseInfo = name + "\t" ;

            var singleCleavageSites = new List<string>();
            var singlePreventionSites = new List<string>();

            if (allCleavageResidues != "")
            {
                singleCleavageSites = allCleavageResidues.Split(',').ToList();
            }

            if (allResiduesStoppingCleavage != "")
            {
                singlePreventionSites = allResiduesStoppingCleavage.Split(',').ToList();
            }

            if (cleavageTerminus == "C")
            {
                string cleavageMotif = "";
                var residues = singleCleavageSites.Count();
                var count = 1;
                foreach (var residue in singleCleavageSites)
                {
                    cleavageMotif += residue;
                    if (singlePreventionSites.Count() != 0)
                    {
                        foreach (var prevent in singlePreventionSites)
                        {
                            cleavageMotif += "[" + prevent + "]";
                        }
                    }
                    if (count < residues)
                    {
                        cleavageMotif += "|,";
                        count++;
                    }
                    else if (count == residues)
                    {
                        cleavageMotif += "|";
                    }
                                        
                }
                proteaseInfo += cleavageMotif;
            }
            else if (cleavageTerminus == "N")
            {
                string cleavageMotif = "";
                var residues = singleCleavageSites.Count();
                var count = 1;
                foreach (var residue in singleCleavageSites)
                {
                    cleavageMotif += "|"+ residue;
                    if (singlePreventionSites.Count() != 0)
                    {
                        foreach (var prevent in singlePreventionSites)
                        {
                            cleavageMotif += "[" + prevent + "]";
                        }
                    }
                    if (count < residues)
                    {
                        cleavageMotif += ",";
                        count++;
                    }
                }
                proteaseInfo += cleavageMotif;
            }
            proteaseInfo += "\t"+ "\t"+ "\t"+ cleavageSpecificity +"\t" + psiAccession+ "\t" + psiNames;
            proteaseFileText.Add(proteaseInfo);
            File.WriteAllLines(proteaseFilePath, proteaseFileText);
            proteaseAdded = true;
        }

        private void ClearCustomProtease_Click(object sender, RoutedEventArgs e)
        {
            proteaseNameTextBox.Clear();
            sequencesInducingCleavageTextBox.Clear();
            sequencesPreventingCleavageBox.Clear();
            cleavageTerminusListBox.SelectedIndex = -1;
            cleavageSpecificityListBox.SelectedIndex = -1;
            psiAccessionNumber.Clear();
            psiName.Clear();
        }

    }
}
