using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Easy.Common.Extensions;
using Engine;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using static Tasks.ProteaseGuruTask;

namespace ProteaseGuruGui
{
    /// <summary>
    /// Interaction logic for CustomProteaseWindow.xaml
    /// Allows users to make custom proteases for digestion
    /// </summary>
    public partial class CustomProteaseWindow : Window
    {
        public bool proteaseAdded = false;
        public string modName = "";
        public CustomProteaseWindow()
        {
            InitializeComponent();
            PopulateListBoxes();
        }
        //Fill in list boxes with options
        private void PopulateListBoxes()
        {
            cleavageSpecificityListBox.Items.Add("full");
            cleavageSpecificityListBox.Items.Add("semi");
            cleavageTerminusListBox.Items.Add("C");
            cleavageTerminusListBox.Items.Add("N");
        }

        private void OpenProteaseModification_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProteaseModificationWindow();
            dialog.ShowDialog();
            if (dialog.proteaseModAdded == true)
            {
                modName = dialog.modName;
                Omics.Modifications.IO.ModificationLoader.ReadModsFromFile(System.IO.Path.Combine(GlobalVariables.DataDir, @"Mods", @"ProteaseMods.txt"), out List<(Modification, string)> filteredModificationsWithWarnings);
            }

        }

        //Save all the user provided information in the user proteases file for future use
        private void SaveCustomProtease_Click(object sender, RoutedEventArgs e)
        {
            // Custom proteases are stored in a separate user-writable file.
            // mzLib's embedded proteases.tsv is the master list and is never modified.
            string proteaseDirectory = System.IO.Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion");
            if (!Directory.Exists(proteaseDirectory))
                Directory.CreateDirectory(proteaseDirectory);
            string proteaseFilePath = System.IO.Path.Combine(proteaseDirectory, @"user_proteases.tsv");

            // Seed header row on first use
            if (!File.Exists(proteaseFilePath))
                File.WriteAllText(proteaseFilePath,
                    "Name\tSequences Inducing Cleavage\tSequences Preventing Cleavage\t" +
                    "Cleavage Terminus\tCleavage Specificity\tPSI-MS Accession Number\t" +
                    "PSI-MS Name\tSite Regular Expression\tCleavage Mass Shifts\tNotes\n");

            List<string> proteaseFileText = File.ReadAllLines(proteaseFilePath).ToList();

            //all of the protease properties that the user provided
            string name = proteaseNameTextBox.Text;
            string allCleavageResidues = sequencesInducingCleavageTextBox.Text;
            string allResiduesStoppingCleavage = sequencesPreventingCleavageBox.Text;
            var cleavageTerminus = (string)cleavageTerminusListBox.SelectedItem;
            var cleavageSpecificity = (string)cleavageSpecificityListBox.SelectedItem;
            string psiAccession = psiAccessionNumber.Text;
            string psiNames = psiName.Text;

            //formatting these properties for writing to the protease file, so they can be read in each time ProteaseGuru is used
            string proteaseInfo = name + "\t";

            var singleCleavageSites = new List<string>();
            var singlePreventionSites = new List<string>();

            if (allCleavageResidues != "")
            {
                //it is possible that someone will put two commas in a row, which would result in whitespace which is not acceptable
                singleCleavageSites = allCleavageResidues.Split(',').Where(s => !s.IsNullOrEmptyOrWhiteSpace()).ToList();
            }

            if (allResiduesStoppingCleavage != "")
            {
                //it is possible that someone will put two commas in a row, which would result in whitespace which is not acceptable
                singlePreventionSites = allResiduesStoppingCleavage.Split(',').Where(s => !s.ToString().IsNullOrEmptyOrWhiteSpace()).ToList();
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
                    cleavageMotif += "|" + residue;
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
            if (modName != "")
            {
                proteaseInfo += "\t" + "\t" + "\t" + cleavageSpecificity + "\t" + psiAccession + "\t" + psiNames + "\t" + "\t" + modName;
            }
            else
            {
                proteaseInfo += "\t" + "\t" + "\t" + cleavageSpecificity + "\t" + psiAccession + "\t" + psiNames + "\t" + "\t";
            }
            proteaseFileText.Add(proteaseInfo);
            File.WriteAllLines(proteaseFilePath, proteaseFileText);
            ProteaseDictionary.LoadAndMergeCustomProteases(proteaseFilePath, GlobalVariables.ProteaseMods);
            GlobalVariables.UserAddedProteaseNames.Add(name);
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
