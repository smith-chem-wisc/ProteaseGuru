using Engine;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        private ObservableCollection<ProteinForTreeView> proteinTree;
        private ObservableCollection<ProteinForTreeView> filteredTree;
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;
        private Dictionary<Protein, Dictionary<string, List<InSilicoPep>>> PeptideByProteaseAndProtein;
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;
        private Dictionary<InSilicoPep, (int,int)> partialPeptideMatches = new Dictionary<InSilicoPep, (int,int)>();
        private Dictionary<string, Color> ProteaseByColor;
        private Dictionary<string, SolidColorBrush> ModsByColor;
        private List<string> Proteases;
        private List<string> SelectedProteases;
        private ProteinForTreeView SelectedProtein;
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
            PeptideByProteaseAndProtein = new Dictionary<Protein, Dictionary<string, List<InSilicoPep>>>();
            proteinTree = new ObservableCollection<ProteinForTreeView>();
            filteredTree = new ObservableCollection<ProteinForTreeView>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();            
            dataGridProteins.DataContext = proteinTree;            
            SetUpDictionaries();

            this.Loaded += results_Loaded;

            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        //set up protease to color dictionary for peptides as well as mods
        public void SetUpDictionaries()
        {
            List<Color> colors = new List<Color>(){Colors.MediumBlue, Colors.Goldenrod, Colors.ForestGreen, Colors.DarkOrchid, Colors.Chocolate,
                Colors.Teal, Colors.PaleVioletRed, Colors.DimGray, Colors.LightSkyBlue, Colors.PaleGoldenrod, Colors.DarkSeaGreen, Colors.Thistle,
                Colors.PeachPuff, Colors.PaleTurquoise, Colors.MistyRose, Colors.Gainsboro, Colors.Navy, Colors.DarkGoldenrod, Colors.DarkGreen, Colors.Purple,
                Colors.Sienna, Colors.DarkSlateGray, Colors.MediumVioletRed, Colors.Black, Colors.CornflowerBlue, Colors.Gold, Colors.MediumSeaGreen, 
                Colors.MediumOrchid, Colors.DarkSalmon, Colors.LightSeaGreen, Colors.LightPink, Colors.DarkGray, Colors.Aquamarine, Colors.Coral, 
                Colors.CadetBlue, Colors.DarkMagenta, Colors.DarkOliveGreen, Colors.DeepPink, Colors.GreenYellow, Colors.Maroon, Colors.Yellow,
                Colors.Plum, Colors.PowderBlue};
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
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

        // handler for searching through tree
        private void searchBox_TextChangedHandler(object sender, EventArgs e)
        {
            string userInput = SearchTextBox.Text;

            if (string.IsNullOrEmpty(userInput))
            {
                dataGridProteins.DataContext = proteinTree;
                return;
            }

            searchProtein(userInput);
            dataGridProteins.DataContext = filteredTree;
            SearchModifications.Timer.Stop();
        }

        // search through protein list based on user input
        private void searchProtein(string txt)
        {
            filteredTree.Clear();
            foreach (var protein in proteinTree)
            {
                if (protein.DisplayName.ToUpper().Contains(txt.ToUpper()))
                {
                    filteredTree.Add(protein);
                }
            }
        }

        // updat when the protein selected is changed
        private void OnSelectionChanged()
        {
            if (dataGridProteins.SelectedItem != null)
            {
                var protein = (ProteinForTreeView)dataGridProteins.SelectedItem;
                // draw sequence coverage map if exists
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }
            else
            {
                var protein = (ProteinForTreeView)ProteinsForTreeView.FirstOrDefault().Value;
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }
                          

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

        //draw the sequence coverage map, write out the protein seqeunce, overlay modifications, and display peptides for all proteases
        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases) 
        {
            double spacing = 22;
            int height = 10;
            int totalHeight = 0;
            int accumIndex = 0;

            string seqCoverage = protein.Protein.BaseSequence;
            IDictionary<int, List<Modification>> mods = protein.Protein.OneBasedPossibleLocalizedModifications;
            var modsSplitByLine = new List<Dictionary<int, List<Modification>>>();
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
                modsSplitByLine = SplitMods(mods, protein.Protein.Length, Convert.ToInt32(map.Width / spacing));
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

            SequenceCoverageMap.txtDrawing(map, new Point(5,height), mapTitle, Brushes.Black);
            height = height + 30;
            // draw sequence
            foreach (var line in splitSeq)
            {
                indices.Clear();
                for (int r = 0; r < line.Length; r++)
                {
                    SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 10, height), line[r].ToString().ToUpper(), Brushes.Black);
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
                            SequenceCoverageMap.stackedCircledTxtDraw(map, new Point((mod.Key) * spacing - 17, height), colors);

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
                            SequenceCoverageMap.circledTxtDraw(map, new Point((mod.Key) * spacing - 17, height), color);
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
                        
                            SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[peptide.Key.Protease],
                                peptide.Key.Unique, highlightIndex);
                        
                        
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

                            var highlightIndex = SequenceCoverageMap.Highlight(start, end, map, indices, height, ProteaseByColor[peptide.Protease],
                                peptide.Unique);

                            if (partialIndex >= 0)
                            {
                                partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
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
                    height += (proteases.Count() * 50);
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
                totalHeight += splitSeq.Count() * (proteases.Count() * 50);
                map.Height = totalHeight + (proteases.Count() * 50);
            }

            if (mods.Count > 0)
            {
                SequenceCoverageMap.drawLegendMods(legend, ProteaseByColor, ModsByColor, proteases, legendGrid);
            }
            else 
            {
                SequenceCoverageMap.drawLegend(legend, ProteaseByColor, proteases, legendGrid);
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
            int size = Convert.ToInt32(map.Width / spacing);
            var splitSequence = Enumerable.Range(0, sequence.Length / size).Select(i => sequence.Substring(i * size, size)).ToList();
            splitSequence.Add(sequence.Substring(splitSequence.Count() * size));

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

        //populate the search space with all proteins from all databases digested
        private void LoadProteins_Click(object sender, RoutedEventArgs e)
        {
            ProteinLoadButton.IsEnabled = false;

            // populate proteins
            foreach (var db in PeptideByFile)
            {
                foreach (var protease in db.Value)
                {
                    // protease.Value is <Protein, List<Peptides>>
                    foreach (var protein in protease.Value)
                    {
                        var prot = protein.Key;
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
                            proteinTree.Add(newPtv);
                        }

                        // define peptides for protein for tree view
                        ProteinsForTreeView[prot].AllPeptides.AddRange(protein.Value);
                        ProteinsForTreeView[prot].UniquePeptides.AddRange(protein.Value.Where(p => p.Unique));
                        ProteinsForTreeView[prot].SharedPeptides.AddRange(protein.Value.Where(p => !p.Unique));
                    }
                }
            }

            // add summary for proteins
            foreach (var protein in PeptideByProteaseAndProtein)
            {
                var ptv = ProteinsForTreeView[protein.Key];
                var proteaseList = UserParams.ProteasesForDigestion.Select(p=>p.Name).ToList();
                var uniquePeps = ptv.UniquePeptides.GroupBy(p => p.Protease).ToDictionary(group => group.Key, group => group.ToList());
                var sharedPeps = ptv.SharedPeptides.GroupBy(p => p.Protease).ToDictionary(group => group.Key, group => group.ToList());
                ptv.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: "));
                foreach (var protease in proteaseList)
                {
                    if (uniquePeps.ContainsKey(protease))
                    {
                        ptv.Summary.Add(new SummaryForTreeView("     " + protease + ": " + uniquePeps[protease].Count()));
                    }
                    else 
                    {
                        ptv.Summary.Add(new SummaryForTreeView("     " + protease + ": 0" ));
                    }
                    
                }
                ptv.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " ));
                foreach (var protease in proteaseList)
                {
                    if (sharedPeps.ContainsKey(protease))
                    {
                        ptv.Summary.Add(new SummaryForTreeView("     " + protease + ": " + sharedPeps[protease].Count()));
                    }
                    else
                    {
                        ptv.Summary.Add(new SummaryForTreeView("     " + protease + ": 0"));
                    }

                }
                                
                ptv.Summary.Add(new SummaryForTreeView("Sequence Coverage Fraction:")); // break down by protease

                foreach (var seqCovKvp in CalculateSequenceCoverage(protein.Key))
                {
                    ptv.Summary.Add(new SummaryForTreeView("     " + seqCovKvp.Item1 + ": " + Math.Round(seqCovKvp.Item2*100, 3) + "%")); // break down by protease
                }
            }
        }

        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = .70 * ResultsGrid.ActualHeight;
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

        private void proteins_SelectedCellsChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // if protease selected item is null, dont draw map
            if (ProteaseSelectedForUse.SelectedItems != null)
            {
                OnSelectionChanged();
            }
        }

        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGridProteins.SelectedItem != null)
            {
                OnSelectionChanged();
            }
        }

        private void proteaseCoverageMaps_loaded(object sender, RoutedEventArgs e)
        {
            // get list of proteases to generate cov maps           
            Proteases = PeptideByFile.SelectMany(p => p.Value.Keys).Distinct().ToList();
            ListBox combo = sender as ListBox;
            combo.ItemsSource = Proteases;            
        }
        
        // export the coverage map and legend as separate .png files
        private void exportCoverageMap(object sender, RoutedEventArgs e)
        {
            var fileDirectory = UserParams.OutputFolder;
            var fileName = String.Concat("SequenceCoverageMap_"+SelectedProtein.DisplayName+".png");
            Rect bounds = VisualTreeHelper.GetDescendantBounds(map);
            double dpi = 96d;
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, dpi, dpi, System.Windows.Media.PixelFormats.Default);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(map);
                dc.DrawRectangle(vb, null, new Rect(new Point(), bounds.Size));
            }
            rtb.Render(dv);

            BitmapEncoder pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(rtb));

            System.IO.MemoryStream ms = new System.IO.MemoryStream();
            pngEncoder.Save(ms);
            ms.Close();
            var filePath = System.IO.Path.Combine(fileDirectory, fileName);
            System.IO.File.WriteAllBytes(filePath, ms.ToArray());
                        
            var fileNameLegend = String.Concat("SequenceCoverageMapLegend_" + SelectedProtein.DisplayName + ".png");
            Rect boundsLegend = VisualTreeHelper.GetDescendantBounds(legend);           
            RenderTargetBitmap rtbLegend = new RenderTargetBitmap((int)boundsLegend.Width, (int)boundsLegend.Height, dpi, dpi, System.Windows.Media.PixelFormats.Default);
            DrawingVisual dvLegend = new DrawingVisual();
            using (DrawingContext dc = dvLegend.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(legend);
                dc.DrawRectangle(vb, null, new Rect(new Point(), boundsLegend.Size));
            }
            rtbLegend.Render(dvLegend);

            BitmapEncoder pngEncoderLegend = new PngBitmapEncoder();
            pngEncoderLegend.Frames.Add(BitmapFrame.Create(rtbLegend));

            System.IO.MemoryStream msLegend = new System.IO.MemoryStream();
            pngEncoderLegend.Save(msLegend);
            ms.Close();
            var filePathLegend = System.IO.Path.Combine(fileDirectory, fileNameLegend);
            System.IO.File.WriteAllBytes(filePathLegend, msLegend.ToArray());
        }
    }
}
