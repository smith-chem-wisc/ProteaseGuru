using Engine;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tasks;

namespace ProteaseGuruGUI
{
    /// <summary>
    /// Interaction logic for ProteinResultsWindow.xaml
    /// </summary>
    public partial class ProteinResultsWindow : UserControl
    {        
        private ObservableCollection<string> proteinList;
        private ObservableCollection<string> filteredList;
        private ObservableCollection<ProteinSummaryForTreeView> ProteinDigestionSummary;
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;
        private Dictionary<Protein, Dictionary<string, List<InSilicoPep>>> PeptideByProteaseAndProtein;
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;
        private Dictionary<InSilicoPep, (int,int)> partialPeptideMatches = new Dictionary<InSilicoPep, (int,int)>();
        private Dictionary<string, Color> ProteaseByColor;
        private Dictionary<string, SolidColorBrush> ModsByColor;
        private List<string> Proteases;
        private List<string> SelectedProteases;
        private ProteinForTreeView SelectedProtein;
        private bool MessageShow;
        Parameters UserParams;

        public ProteinResultsWindow()
        {

        }

        public ProteinResultsWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile, Parameters userParams) // change constructor to receive analysis information
        {
            InitializeComponent();
            SelectedProteases = new List<string>();
            UserParams = userParams;
            SelectedProtein = null;
            PeptideByFile = peptideByFile;
            MessageShow = true;
            PeptideByProteaseAndProtein = new Dictionary<Protein, Dictionary<string, List<InSilicoPep>>>();
            ProteinDigestionSummary = new ObservableCollection<ProteinSummaryForTreeView>();
            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();
            SetUpTreeView();
            dataGridProteins.DataContext = proteinList;            
            SetUpDictionaries();            

            this.Loaded += results_Loaded;

            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);           
        }

        //set up protease to color dictionary for peptides as well as mods
        public void SetUpDictionaries()
        {
            List<Color> colors = new List<Color>(){ Color.FromRgb(130, 88, 159), Color.FromRgb(0, 148, 50), Color.FromRgb(181, 52, 113), Color.FromRgb(52, 152, 219), Color.FromRgb(230, 126, 34),
           Color.FromRgb(27, 20, 100), Color.FromRgb(253, 167, 223), Color.FromRgb(99, 110, 114), Color.FromRgb(255, 221, 89), Color.FromRgb(162, 155, 254), Color.FromRgb(58, 227, 116),
           Color.FromRgb(252, 66, 123), Color.FromRgb(126, 214, 223), Color.FromRgb(249, 127, 81), Color.FromRgb(189, 195, 199), Color.FromRgb(241, 196, 15), Color.FromRgb(0, 98, 102), Color.FromRgb(142, 68, 173),
           Color.FromRgb(225, 112, 85), Color.FromRgb(255, 184, 184), Color.FromRgb(61, 193, 211), Color.FromRgb(224, 86, 253), Color.FromRgb(196, 229, 56), Color.FromRgb(255, 71, 87),
            Color.FromRgb(88, 177, 159), Color.FromRgb(111, 30, 81), Color.FromRgb(129, 236, 236), Color.FromRgb(179, 57, 57), Color.FromRgb(232, 67, 147)};
            ProteaseByColor = new Dictionary<string, Color>();
            ModsByColor = new Dictionary<string, SolidColorBrush>();
            var proteases = PeptideByFile.SelectMany(p => p.Value.Keys).Distinct().ToList();
            foreach (var protease in proteases)
            {
                
                ProteaseByColor.Add(protease, colors.ElementAt(proteases.IndexOf(protease)));
            } 
        }

        //update search when a new accession is provided
        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchModifications.SetTimer();
        }

        //clear proteaes selected from the sequence coverage mapp
        private void ClearSelectedProteases_Click(object sender, RoutedEventArgs e)
        {
            ProteaseSelectedForUse.SelectedItems.Clear();
            SelectedProteases.Clear();
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

        //select proteases and display their peptides on the sequence coverage map
        private void SelectProteases_Click(object sender, RoutedEventArgs e)
        {
            SelectedProteases.Clear();
            foreach (var protease in ProteaseSelectedForUse.SelectedItems)
            {
                SelectedProteases.Add(protease.ToString());
            }
            if (SelectedProtein == null)
            {
                DrawSequenceCoverageMap(ProteinsForTreeView.FirstOrDefault().Value, SelectedProteases);
            }
            else 
            {
                DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
            }
            
        }

        // handler for searching through tree
        private void searchBox_TextChangedHandler(object sender, EventArgs e)
        {
            string userInput = SearchTextBox.Text;

            if (string.IsNullOrEmpty(userInput))
            {
                dataGridProteins.DataContext = proteinList;
                return;
            }

            searchProtein(userInput);
            dataGridProteins.Items.Clear();
            foreach (var entry in filteredList)
            {
                dataGridProteins.Items.Add(entry);
            }
            
            SearchModifications.Timer.Stop();
        }

        // search through protein list based on user input
        private void searchProtein(string txt)
        {
            filteredList.Clear();
            foreach (var protein in proteinList)
            {
                if (protein.Contains(txt.ToUpper()))
                {
                    filteredList.Add(protein);
                }
            }
        }

        private void SetUpTreeView()
        {
            List<string> proteinListDuplicates = new List<string>();
            foreach (var db in PeptideByFile)
            {
                foreach (var protease in db.Value)
                {
                    // protease.Value is <Protein, List<Peptides>>
                    foreach (var protein in protease.Value)
                    {
                        var prot = protein.Key;
                        
                        proteinListDuplicates.Add(prot.Accession);
                        var peptidesByProtease = new Dictionary<string, List<InSilicoPep>>();

                        if (PeptideByProteaseAndProtein.ContainsKey(prot))
                        {
                            if (PeptideByProteaseAndProtein[prot].ContainsKey(protease.Key))
                            {
                                PeptideByProteaseAndProtein[prot][protease.Key].AddRange(protein.Value);
                            }
                            else
                            {
                                PeptideByProteaseAndProtein[prot].Add(protease.Key, protein.Value);
                            }
                        }
                        else
                        {
                            peptidesByProtease.Add(protease.Key, protein.Value);
                            PeptideByProteaseAndProtein.Add(prot, peptidesByProtease);

                            var name = prot.Accession ?? prot.Name;
                            var newPtv = new ProteinForTreeView(prot, name, new List<InSilicoPep>(), new List<InSilicoPep>(), new List<InSilicoPep>());
                            ProteinsForTreeView.Add(prot, newPtv);
                            
                        }

                        if (PeptideByFile.Keys.Count > 1)
                        {
                            // define peptides for protein for tree view
                            ProteinsForTreeView[prot].AllPeptides.AddRange(protein.Value);
                            ProteinsForTreeView[prot].UniquePeptides.AddRange(protein.Value.Where(p => p.UniqueAllDbs));
                            ProteinsForTreeView[prot].SharedPeptides.AddRange(protein.Value.Where(p => !p.UniqueAllDbs));
                        }
                        else
                        {
                            // define peptides for protein for tree view
                            ProteinsForTreeView[prot].AllPeptides.AddRange(protein.Value);
                            ProteinsForTreeView[prot].UniquePeptides.AddRange(protein.Value.Where(p => p.Unique));
                            ProteinsForTreeView[prot].SharedPeptides.AddRange(protein.Value.Where(p => !p.Unique));
                        }

                    }
                }
            }
            foreach (var prot in proteinListDuplicates.Distinct())
            {
                proteinList.Add(prot);
                dataGridProteins.Items.Add(prot);
            }
            
        }

        // update when the protein selected is changed
        private void OnSelectionChanged()
        {
            if (MessageShow == true)
            {
                if (PeptideByFile.Keys.Count > 1)
                {
                    MessageBox.Show("Note: More than one protein database was analyzed. Unique peptides are defined as being unique to a single protein in all analyzed databases.");
                }
                else
                {
                    MessageBox.Show("Note: One protein database was analyzed. Unique peptides are defined as being unique to a single protein in the analyzed database.");
                }
                MessageShow = false;
            }
            

            if (dataGridProteins.SelectedItem != null)
            {
                string proteinName = dataGridProteins.SelectedItem.ToString();
                var protein = ProteinsForTreeView.Where(p => p.Key.Accession == proteinName).FirstOrDefault().Value;
                // draw sequence coverage map if exists
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }
            else
            {
                var protein = ProteinsForTreeView.FirstOrDefault().Value;
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }

            var ptv = SelectedProtein;
                var proteaseList = UserParams.ProteasesForDigestion.Select(p => p.Name).ToList();
                var uniquePeps = ptv.UniquePeptides.GroupBy(p => p.Protease).ToDictionary(group => group.Key, group => group.ToList());
                var sharedPeps = ptv.SharedPeptides.GroupBy(p => p.Protease).ToDictionary(group => group.Key, group => group.ToList());

            ProteinSummaryForTreeView thisProtein = new ProteinSummaryForTreeView("Digestion Results for " + ptv.Protein.Accession + ":");
            AnalysisSummaryForTreeView uniquePep = new AnalysisSummaryForTreeView("Number of Unique Peptides: ");
            foreach (var protease in proteaseList)
            {
                if (uniquePeps.ContainsKey(protease))
                {
                    uniquePep.Summary.Add(new ProtSummaryForTreeView(protease + ": " + uniquePeps[protease].Count()));
                }
                else
                {
                    uniquePep.Summary.Add(new ProtSummaryForTreeView(protease + ": 0"));
                }

            }
            thisProtein.Summary.Add(uniquePep);

            AnalysisSummaryForTreeView sharedPep = new AnalysisSummaryForTreeView("Number of Shared Peptides: ");           
            foreach (var protease in proteaseList)
            {
                if (sharedPeps.ContainsKey(protease))
                {
                    sharedPep.Summary.Add(new ProtSummaryForTreeView(protease + ": " + sharedPeps[protease].Count()));
                }
                else
                {
                    sharedPep.Summary.Add(new ProtSummaryForTreeView(protease + ": 0"));
                }
            }
            thisProtein.Summary.Add(sharedPep);

            AnalysisSummaryForTreeView percentCov = new AnalysisSummaryForTreeView("Percent Sequence Coverage (all peptides):" );
            foreach (var seqCovKvp in CalculateSequenceCoverage(SelectedProtein.Protein))
            {
                percentCov.Summary.Add(new ProtSummaryForTreeView(seqCovKvp.Item1 + ": " + Math.Round(seqCovKvp.Item2 * 100, 3) + "%")); // break down by protease
            }
            thisProtein.Summary.Add(percentCov);

            AnalysisSummaryForTreeView percentCovUniq = new AnalysisSummaryForTreeView("Percent Sequence Coverage (unique peptides):");
            foreach (var seqCovKvp in CalculateSequenceCoverageUnique(SelectedProtein.Protein))
            {
                percentCovUniq.Summary.Add(new ProtSummaryForTreeView(seqCovKvp.Item1 + ": " + Math.Round(seqCovKvp.Item2 * 100, 3) + "%")); // break down by protease
            }
            thisProtein.Summary.Add(percentCovUniq);

            ProteinDigestionSummary.Clear();

            ProteinDigestionSummary.Add(thisProtein);

            proteinResults.DataContext = ProteinDigestionSummary;

            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);


        }

        //splits the list of modifcations to lines and gives them the proper index to line up to the same amino acid
        private List<Dictionary<int, List<Modification>>> SplitMods(IDictionary<int, List<Modification>> mods, int proteinLength, int spacing)
        {
            double round = proteinLength / spacing;
            var remainder = proteinLength % spacing;
            if (remainder > 0)
            {
                round = round + 1;
            }
            var splitCount = Convert.ToInt32(round);
            var splitMods = new List<Dictionary<int, List<Modification>>>();
            for (int j = 0; j < splitCount; j++)
            {
                Dictionary<int, List<Modification>> modsInArea = new Dictionary<int, List<Modification>>();
                int min = 1 + (j*spacing);
                int max = spacing + (j * spacing);
                foreach (var entry in mods)
                {
                    if (entry.Key >= min && entry.Key <= max)
                    {
                        modsInArea.Add((entry.Key - (j * spacing)), entry.Value);
                    }
                }
                splitMods.Add(modsInArea);
            }            
            return splitMods;
        }
        private List<List<int>> SplitVariations(List<SequenceVariation> variants, int proteinLength, int spacing)
        {
            double round = proteinLength / spacing;
            var remainder = proteinLength % spacing;
            if (remainder > 0)
            {
                round = round + 1;
            }
            var splitCount = Convert.ToInt32(round);
            var splitVariants = new List<List<int>>();
            for (int j = 0; j < splitCount; j++)
            {
                List<int> variantsInArea = new List<int>();
                int min = 1 + (j * spacing);
                int max = spacing + (j * spacing);
                foreach (var entry in variants)
                {
                    //varaint start and end is all on this line
                    if (entry.OneBasedBeginPosition >= min && entry.OneBasedBeginPosition <= max && entry.OneBasedEndPosition >= min && entry.OneBasedEndPosition <= max)
                    {
                        // add all numbers from start position to end position
                        int numberToAdd = (entry.OneBasedBeginPosition - (j * spacing));
                        variantsInArea.Add(numberToAdd);
                        numberToAdd++;
                        while (numberToAdd > (entry.OneBasedBeginPosition - (j * spacing)) && numberToAdd < (entry.OneBasedEndPosition - (j * spacing)))
                        {
                            variantsInArea.Add(numberToAdd);
                            numberToAdd++;
                        }
                        variantsInArea.Add((entry.OneBasedEndPosition - (j * spacing)));
                    }
                    // variant starts on this line but ends on another line
                    if (entry.OneBasedBeginPosition >= min && entry.OneBasedBeginPosition <= max && entry.OneBasedEndPosition > max)
                    {
                        // add all numbers from start position to end of line (max)
                        int numberToAdd = (entry.OneBasedBeginPosition - (j * spacing));
                        variantsInArea.Add(numberToAdd);
                        numberToAdd++;
                        while (numberToAdd > (entry.OneBasedBeginPosition - (j * spacing)) && numberToAdd < (max - (j * spacing)))
                        {
                            variantsInArea.Add(numberToAdd);
                            numberToAdd++;
                        }
                        variantsInArea.Add((max - (j * spacing)));

                    }
                    // variant ends on this line but started on another line
                    if (entry.OneBasedEndPosition >= min && entry.OneBasedEndPosition <= max && entry.OneBasedBeginPosition < min)
                    {
                        // add all numbers from start of line (min) to end position
                        int numberToAdd = (min - (j * spacing));
                        variantsInArea.Add(numberToAdd);
                        numberToAdd++;
                        while (numberToAdd > (min - (j * spacing)) && numberToAdd < (entry.OneBasedEndPosition - (j * spacing)))
                        {
                            variantsInArea.Add(numberToAdd);
                            numberToAdd++;
                        }
                        variantsInArea.Add((entry.OneBasedEndPosition - (j * spacing)));
                    }
                    // variant covers all residues in this line but starts on a previous line and ends on a future line
                    if(entry.OneBasedBeginPosition< min && entry.OneBasedEndPosition > max)
                    {
                        // add all numbers from start of line (min) to end of line (max)
                        int numberToAdd = (min - (j * spacing));
                        variantsInArea.Add(numberToAdd);
                        numberToAdd++;
                        while (numberToAdd > (min - (j * spacing)) && numberToAdd < (max - (j * spacing)))
                        {
                            variantsInArea.Add(numberToAdd);
                            numberToAdd++;
                        }
                        variantsInArea.Add((max - (j * spacing)));
                    }
                }
                splitVariants.Add(variantsInArea.Distinct().ToList());
            }
            return splitVariants;
        }

        //draw the sequence coverage map, write out the protein seqeunce, overlay modifications, and display peptides for all proteases
        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases) 
        {
            double spacing = 25;
            int height = 10;
            int totalHeight = 0;
            int accumIndex = 0;

            map.Width = 0.90* ResultsGrid.ActualWidth;

            string seqCoverage = protein.Protein.BaseSequence;
            IDictionary<int, List<Modification>> mods = protein.Protein.OneBasedPossibleLocalizedModifications;
            var variants = protein.Protein.AppliedSequenceVariations;            
            var modsSplitByLine = new List<Dictionary<int, List<Modification>>>();
            var variantsByLine = new List<List<int>>();
            var allModsInDb = GlobalVariables.AllModsKnown;
            var modColors = new Dictionary<string, SolidColorBrush>();
            var modWeight = new Dictionary<double, string>();
            modWeight.Add(42.0106, "Acetylation");
            modColors.Add("Acetylation", Brushes.Aqua);
            modWeight.Add(541.0611, "ADP-Ribosylation");
            modColors.Add("ADP-Ribosylation", Brushes.MediumAquamarine);
            modWeight.Add(70.0419, "Butyrylation");
            modColors.Add("Butyrylation", Brushes.LimeGreen);
            modWeight.Add(43.9898, "Carboxylation");
            modColors.Add("Carboxylation",Brushes.Lavender);
            modWeight.Add(0.9840, "Citrullination");
            modColors.Add("Citrullination",Brushes.MediumSlateBlue);
            modWeight.Add(68.0262, "Crotonylation");
            modColors.Add("Crotonylation", Brushes.LightSalmon);
            modWeight.Add(28.0313, "Dimethylation");
            modColors.Add("Dimethylation", Brushes.PaleVioletRed);
            modWeight.Add(27.9949, "Formylation");
            modColors.Add("Formylation", Brushes.Yellow);
            modWeight.Add(114.0317, "Glutarylation");
            modColors.Add("Glutarylation", Brushes.DarkKhaki);
            modWeight.Add(203.0794, "HexNAc");
            modColors.Add("HexNAc", Brushes.PowderBlue);
            modWeight.Add(87.0446, "Hydroxybutyrylation");
            modColors.Add("Hydroxybutyrylation", Brushes.MediumPurple);
            modWeight.Add(15.9949, "Hydroxylation");
            modColors.Add("Hydroxylation", Brushes.Tomato);
            modWeight.Add(86.0004, "Malonylation");
            modColors.Add("Malonylation", Brushes.LightSteelBlue);
            modWeight.Add(14.0157, "Methylation");
            modColors.Add("Methylation", Brushes.Pink);
            modWeight.Add(28.9902, "Nitrosylation");
            modColors.Add("Nitrosylation", Brushes.Plum);
            modWeight.Add(79.9663, "Phosphorylation");
            modColors.Add("Phosphorylation", Brushes.Chartreuse);
            modWeight.Add(229.0140, "Pyridoxal Phosphate");
            modColors.Add("Pyridoxal Phosphate", Brushes.LightCoral);
            modWeight.Add(100.0160, "Succinylation");
            modColors.Add("Succinylation", Brushes.DodgerBlue);
            modWeight.Add(79.9568, "Sulfonation");
            modColors.Add("Sulfonation", Brushes.PaleGreen);
            modWeight.Add(42.0470, "Trimethylation");
            modColors.Add("Trimethylation", Brushes.MediumVioletRed);
            if (mods.Count() != 0)
            {
                modsSplitByLine = SplitMods(mods, protein.Protein.Length, Convert.ToInt32( spacing));
            }

            if (variants.Count() != 0)
            {
                variantsByLine = SplitVariations(variants, protein.Protein.Length, Convert.ToInt32(spacing));
            }
            map.Children.Clear();
            legendGrid.Children.Clear();

            
            var splitSeq = Split(seqCoverage, spacing);
            var peptidesToDraw = new List<InSilicoPep>();
            foreach (var protease in proteases)
            {
                if (PeptideByProteaseAndProtein[protein.Protein].ContainsKey(protease))
                {
                    peptidesToDraw.AddRange(PeptideByProteaseAndProtein[protein.Protein][protease]);
                }                
            }

            var mapTitle = "Sequence Coverage Map of " + protein.Protein.Accession +":";

            peptidesToDraw = peptidesToDraw.Distinct().ToList();
            var indices = new Dictionary<int, List<int>>();

            SequenceCoverageMap.txtDrawing(map, new Point(0,height), mapTitle, Brushes.Black);
            height = height + 30;
            // draw sequence
            foreach (var line in splitSeq)
            {
                indices.Clear();
                var lineCount = splitSeq.IndexOf(line);
                var lineLabel = (lineCount * 25) + 1;
                SequenceCoverageMap.txtDrawingLabel(map, new Point(0, height), lineLabel.ToString(), Brushes.Black);
                if (variants.Count() > 0)
                {
                    for (int r = 0; r < line.Length; r++)
                    {                        
                        if (variantsByLine[splitSeq.IndexOf(line)].Contains(r + 1))
                        {
                            SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 65, height), line[r].ToString().ToUpper(), Brushes.Red);
                        }
                        else
                        {
                            SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 65, height), line[r].ToString().ToUpper(), Brushes.Black);
                        }
                        
                    }
                }
                else
                {
                    for (int r = 0; r < line.Length; r++)
                    {
                        SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 65, height), line[r].ToString().ToUpper(), Brushes.Black);
                    }
                }
                
                if (mods.Count() > 0)
                {
                    var modsForLine = modsSplitByLine[splitSeq.IndexOf(line)];
                    foreach (var mod in modsForLine)
                    {
                        SolidColorBrush color = Brushes.Orange;
                        if (mod.Value.Count() > 1)
                        {
                            List<SolidColorBrush> colors = new List<SolidColorBrush>();
                            foreach (var m in mod.Value)
                            {
                                double roundedMass = Math.Round(Convert.ToDouble(m.MonoisotopicMass), 4, MidpointRounding.AwayFromZero);

                                if (modWeight.ContainsKey(roundedMass))
                                {
                                    if (roundedMass == 15.9949)
                                    {
                                        if (modWeight[roundedMass].Contains("hydroxy"))
                                        {
                                            color = modColors[modWeight[roundedMass]];
                                            colors.Add(color);
                                            if (!ModsByColor.ContainsKey(modWeight[roundedMass]))
                                            {
                                                ModsByColor.Add(modWeight[roundedMass], modColors[modWeight[roundedMass]]);
                                            }
                                        }
                                        else
                                        {
                                            if (!ModsByColor.ContainsKey("Other"))
                                            {
                                                ModsByColor.Add("Other", Brushes.Orange);
                                            }
                                            colors.Add(Brushes.Orange);
                                        }
                                    } 
                                    else
                                    {
                                        color = modColors[modWeight[roundedMass]];
                                        colors.Add(color);
                                        if (!ModsByColor.ContainsKey(modWeight[roundedMass]))
                                        {
                                            ModsByColor.Add(modWeight[roundedMass], modColors[modWeight[roundedMass]]);
                                        }
                                    }                                  

                                }
                                else
                                {
                                    if (!ModsByColor.ContainsKey("Other"))
                                    {
                                        ModsByColor.Add("Other", Brushes.Orange);
                                    }
                                    colors.Add(Brushes.Orange);

                                }
                            }
                            SequenceCoverageMap.stackedCircledTxtDraw(map, new Point((mod.Key) * spacing +38, height), colors);

                        }
                        else
                        {
                            double roundedMass = Math.Round(Convert.ToDouble(mod.Value.FirstOrDefault().MonoisotopicMass), 4, MidpointRounding.AwayFromZero);
                            if (modWeight.ContainsKey(roundedMass))
                            {
                                if (roundedMass == 15.9949)
                                {
                                    if (modWeight[roundedMass].Contains("hydroxy"))
                                    {
                                        color = modColors[modWeight[roundedMass]];
                                        if (!ModsByColor.ContainsKey(modWeight[roundedMass]))
                                        {
                                            ModsByColor.Add(modWeight[roundedMass], modColors[modWeight[roundedMass]]);
                                        }
                                    }
                                    else
                                    {
                                        if (!ModsByColor.ContainsKey("Other"))
                                        {
                                            ModsByColor.Add("Other", Brushes.Orange);
                                        }
                                    }
                                }
                                else
                                {
                                    color = modColors[modWeight[roundedMass]];
                                    if (!ModsByColor.ContainsKey(modWeight[roundedMass]))
                                    {
                                        ModsByColor.Add(modWeight[roundedMass], modColors[modWeight[roundedMass]]);
                                    }

                                }
                            }
                            else
                            {
                                if (!ModsByColor.ContainsKey("Other"))
                                {
                                    ModsByColor.Add("Other", Brushes.Orange);
                                }

                            }
                            SequenceCoverageMap.circledTxtDraw(map, new Point((mod.Key) * spacing + 38, height), color);
                        }
                        
                    }
                    
                }              
                

                // highlight partial peptide sequences (broken off into multiple lines)
                if (partialPeptideMatches.Count > 0)
                {
                    var temp = new Dictionary<InSilicoPep, (int, int)>(partialPeptideMatches);
                    partialPeptideMatches.Clear();

                    foreach (var peptide in temp)
                    {
                        var remaining = peptide.Value.Item1;
                        var highlightIndex = peptide.Value.Item2;

                        int start = 0;
                        int end = Math.Min(remaining, line.Length - 1);

                        // continue highlighting peptide from previous line
                        
                           
                        var partialIndex = CheckPartialMatch(peptide.Key, line, accumIndex);
                        if (partialIndex >= 0)
                        {
                            SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[peptide.Key.Protease],
                            peptide.Key.Unique, false, false, highlightIndex);
                            partialPeptideMatches.Add(peptide.Key, (partialIndex, highlightIndex));
                        }
                        else
                        {
                            SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[peptide.Key.Protease],
                                   peptide.Key.Unique, false, true, highlightIndex);
                        }

                    }
                }

                for (int i = 0; i < line.Length; ++i)
                {
                    // find peptides on this line
                    var temp = new List<InSilicoPep>(peptidesToDraw.Where(p => p.StartResidue - accumIndex - 1 < line.Length).OrderBy(p => p.StartResidue));
                    
                        foreach (InSilicoPep peptide in temp)
                        {
                            // identify partially highlighted peptides, will continue on next line
                            var partialIndex = CheckPartialMatch(peptide, line, accumIndex);

                            int start = peptide.StartResidue - accumIndex - 1;
                            int end = Math.Min(peptide.EndResidue - accumIndex - 1, line.Length - 1);

                            

                            if (partialIndex >= 0)
                            {
                                var highlightIndex = SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[peptide.Protease],
                                peptide.Unique, true, false);
                            partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
                            }
                            else
                            {
                                 var highlightIndex = SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[peptide.Protease],
                                 peptide.Unique, true, true);
                            }
                            peptidesToDraw.Remove(peptide);
                        }                   
                    
                }
                if (proteases.Count <= 2)
                {
                    height += 100;
                }
                else 
                {
                    height += (proteases.Count() * 60);
                }                
                accumIndex += line.Length;
            }
            if (proteases.Count <= 2)
            {
                totalHeight += splitSeq.Count() * 100;
                map.Height = totalHeight + 100;
            }
            else
            {
                totalHeight += splitSeq.Count() * (proteases.Count() * 60);
                map.Height = totalHeight + (proteases.Count() * 60);
            }

            if (mods.Count > 0)
            {
                if (variants.Count > 0)
                {
                    SequenceCoverageMap.drawLegendMods(legend, ProteaseByColor, ModsByColor, proteases, legendGrid, true);
                }
                else
                {
                    SequenceCoverageMap.drawLegendMods(legend, ProteaseByColor, ModsByColor, proteases, legendGrid, false);
                }
                
            }
            else 
            {
                if (variants.Count > 0)
                {
                    SequenceCoverageMap.drawLegend(legend, ProteaseByColor, proteases, legendGrid, true);
                }
                else
                {
                    SequenceCoverageMap.drawLegend(legend, ProteaseByColor, proteases, legendGrid,false);
                }
               
            }
            
        }


        //check to see if peptide spans accross multiple lines (if so it will partially match on one line)
        private int CheckPartialMatch(InSilicoPep peptide, string line, int accumIndex)
        {
            int remaining = peptide.EndResidue - accumIndex - line.Length - 1;
            if (remaining >= 0)
            {
                return remaining;
            }

            return -1;
        }

        //split the protein sequence into lines for the coverage map
        private List<string> Split(string sequence, double spacing)
        {
            int size = Convert.ToInt32(spacing);
            var splitSequence = Enumerable.Range(0, sequence.Length / size).Select(i => sequence.Substring(i * size, size)).ToList();
            var lineText = sequence.Substring(splitSequence.Count() * size);
            if (lineText != "")
            {
                splitSequence.Add(lineText);
            }            
            return splitSequence;
        }
       
        //caluclate protein sequence coverage for the individual protein summaries
        public IEnumerable<(string, double)> CalculateSequenceCoverage(Protein protein)
        {
            foreach(var proteaseKvp in PeptideByProteaseAndProtein[protein])
            {
                HashSet<int> coveredOneBasedResidues = new HashSet<int>();
                var peptides = proteaseKvp.Value;

                foreach (var peptide in peptides)
                {
                    for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                    {
                        coveredOneBasedResidues.Add(i);
                    }
                }

                var fract = (double)coveredOneBasedResidues.Count / protein.Length;
                yield return (proteaseKvp.Key, fract);
            }
        }

        public IEnumerable<(string, double)> CalculateSequenceCoverageUnique(Protein protein)
        {
            List<InSilicoPep> peptides = new List<InSilicoPep>();
            foreach (var proteaseKvp in PeptideByProteaseAndProtein[protein])
            {
                HashSet<int> coveredOneBasedResidues = new HashSet<int>();
                if (PeptideByFile.Keys.Count() > 1)
                {
                    peptides = proteaseKvp.Value.Where(p => p.UniqueAllDbs == true).ToList();
                }
                else
                {
                    peptides = proteaseKvp.Value.Where(p => p.Unique == true).ToList();
                }
                
                foreach (var peptide in peptides)
                {
                    for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                    {
                        coveredOneBasedResidues.Add(i);
                    }
                }

                var fract = (double)coveredOneBasedResidues.Count / protein.Length;
                yield return (proteaseKvp.Key, fract);
            }
        }
               
        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = .8 * ResultsGrid.ActualHeight;
            mapViewer.Width = .99 * ResultsGrid.ActualWidth;            

            ChangeMapScrollViewVisibility();
        }

        private void ChangeMapScrollViewVisibility()
        {
            
        }

        private void resultsSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ChangeMapScrollViewSize();
        }

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;
        }

        void window_Closing(object sender, global::System.ComponentModel.CancelEventArgs e)
        {
            SearchModifications.Timer.Tick -= new EventHandler(searchBox_TextChangedHandler);
        }

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e)
        {  
                OnSelectionChanged();            
        }

        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {            
                OnSelectionChanged();            
        }

        private void proteaseCoverageMaps_loaded(object sender, RoutedEventArgs e)
        {
            // get list of proteases to generate cov maps           
            Proteases = PeptideByFile.SelectMany(p => p.Value.Keys).Distinct().ToList();
            ListBox combo = sender as ListBox;
            combo.ItemsSource = Proteases;            
        }
        
        // export results
        private void exportCoverageMap(object sender, RoutedEventArgs e)
        {
            var fileDirectory = UserParams.OutputFolder + @"\ProteaseGuruDigestionResults";
            string subFolder = System.IO.Path.Combine(fileDirectory, SelectedProtein.DisplayName);
            Directory.CreateDirectory(subFolder);
            var fileName = String.Concat("SequenceCoverageMap_"+SelectedProtein.DisplayName+".png");            
            Rect bounds = VisualTreeHelper.GetDescendantBounds(mapGrid);
            double dpi = 96d;
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, dpi, dpi, System.Windows.Media.PixelFormats.Default);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(mapGrid);
                dc.DrawRectangle(vb, null, new Rect(new Point(), bounds.Size));
            }
            rtb.Render(dv);

            BitmapEncoder pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(rtb));

            System.IO.MemoryStream ms = new System.IO.MemoryStream();
            pngEncoder.Save(ms);
            ms.Close();
            var filePath = System.IO.Path.Combine(subFolder, fileName);
            System.IO.File.WriteAllBytes(filePath, ms.ToArray());
            var resultsFile = String.Concat(SelectedProtein.DisplayName + "_DigestionResults.txt");
            var proteinAccession = SelectedProtein.DisplayName;
            List<string> results = new List<string>();
            foreach (var protein in ProteinDigestionSummary)
            {
                results.Add(protein.DisplayName);               
                foreach (var analysis in protein.Summary)
                {
                    results.Add("   "+analysis.DisplayName);
                    foreach (var protease in analysis.Summary)
                    {
                        results.Add("       "+protease.DisplayName);
                    }
                }
            }

            File.WriteAllLines(System.IO.Path.Combine(subFolder, resultsFile), results);

            string tab = "\t";
            string header = "Database" + tab + "Protease" + tab + "Base Sequence" + tab + "Full Sequence" + tab + "Previous Amino Acid" + tab +
                "Next Amino Acid" + tab + "Start Residue" + tab + "End Residue" + tab + "Length" + tab + "Molecular Weight" + tab + "Protein Accession" + tab + "Protein Name" + tab + "Unique Peptide (in this database)" + tab + "Unique Peptide (in all databases)" + tab + "Peptide sequence exclusive to this Database" + tab +
                "Hydrophobicity" + tab + "Electrophoretic Mobility";
            var allPeptidesForProtein = PeptideByProteaseAndProtein.Where(p => p.Key.Accession == proteinAccession).FirstOrDefault().Value;
            List<InSilicoPep> allPeptides = new List<InSilicoPep>();
            List<InSilicoPep> allPeptidesUnique = new List<InSilicoPep>();
            foreach (var protease in allPeptidesForProtein)
            {
                allPeptides.AddRange(protease.Value);
                allPeptidesUnique.AddRange(protease.Value.Where(p => p.UniqueAllDbs == true));
            }   


            var numberOfPeptides = allPeptides.Count();
            double numberOfFiles = Math.Ceiling(numberOfPeptides / 1000000.0);
            var peptidesInFile = 1;
            var peptideIndex = 0;
            var fileCount = 1;

            while (fileCount <= Convert.ToInt32(numberOfFiles))
            {
                using (StreamWriter output = new StreamWriter(subFolder + @"\ProteaseGuruPeptides_"+proteinAccession+"_" + fileCount + ".tsv"))
                {
                    output.WriteLine(header);
                    while (peptidesInFile < 1000000)
                    {
                        if (peptideIndex < numberOfPeptides)
                        {
                            output.WriteLine(allPeptides[peptideIndex].ToString());
                            peptideIndex++;
                        }
                        peptidesInFile++;

                    }
                    output.Close();
                    peptidesInFile = 1;
                }
                fileCount++;
            }

            var numberOfUniquePeptides = allPeptidesUnique.Count();
            if (numberOfUniquePeptides != 0)
            {
                double numberOfFilesUnique = Math.Ceiling(numberOfPeptides / 1000000.0);
                var uniquePeptidesInFile = 1;
                var uniquePeptideIndex = 0;
                var fileCountUnique = 1;

                while (fileCountUnique <= Convert.ToInt32(numberOfFilesUnique))
                {
                    using (StreamWriter outputUnique = new StreamWriter(subFolder + @"\ProteaseGuruUniquePeptides_" + proteinAccession + "_" + fileCountUnique + ".tsv"))
                    {
                        outputUnique.WriteLine(header);
                        while (uniquePeptidesInFile < 1000000)
                        {
                            if (uniquePeptideIndex < numberOfUniquePeptides)
                            {
                                outputUnique.WriteLine(allPeptidesUnique[uniquePeptideIndex].ToString());
                                uniquePeptideIndex++;
                            }
                            uniquePeptidesInFile++;

                        }
                        outputUnique.Close();
                        uniquePeptidesInFile = 1;
                    }
                    fileCountUnique++;
                }

                string message = "PNG and txt files Created at " + subFolder + "! Would you like to copy the file paths?";
                var messageBox = MessageBox.Show(message, null, MessageBoxButton.YesNo);
                if (messageBox == MessageBoxResult.Yes)
                {
                    Clipboard.SetText("Coverage Map: " + filePath + "\r\nResults Summary File: " + System.IO.Path.Combine(subFolder, resultsFile) + "\r\nAll Peptide Files: " + subFolder + @"\ProteaseGuruPeptides_" + proteinAccession + "_" + 1 + ".tsv" + "\r\nUnique Peptides: " + subFolder + @"\ProteaseGuruUniquePeptides_" + proteinAccession + "_" + 1 + ".tsv");
                }
            }
            else
            {
                string message = "PNG and txt files Created at " + subFolder + "! Would you like to copy the file paths?";
                var messageBox = MessageBox.Show(message, null, MessageBoxButton.YesNo);
                if (messageBox == MessageBoxResult.Yes)
                {
                    Clipboard.SetText("Coverage Map: " + filePath + "\r\nResults Summary File: " + System.IO.Path.Combine(subFolder, resultsFile) + "\r\nAll Peptide Files: " + subFolder + @"\ProteaseGuruPeptides_" + proteinAccession + "_" + 1 + ".tsv");
                }
            }
            
        }
    }
}
