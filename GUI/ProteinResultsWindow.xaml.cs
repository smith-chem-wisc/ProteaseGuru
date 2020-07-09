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
            var proteases = PeptideByFile.SelectMany(p => p.Value.Keys).ToList();
            foreach (var protease in proteases)
            {
                ProteaseByColor.Add(protease, colors.ElementAt(proteases.IndexOf(protease)));
            } 
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchModifications.SetTimer();
        }

        private void ClearSelectedProteases_Click(object sender, RoutedEventArgs e)
        {
            ProteaseSelectedForUse.SelectedItems.Clear();
            SelectedProteases.Clear();
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

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

        private void OnSelectionChanged()
        {
            try
            {
                var protein = (ProteinForTreeView)dataGridProteins.SelectedItem;                
                // draw sequence coverage map if exists
                if (protein != null)
                {
                    SelectedProtein = protein;                    
                }
                
            }
            catch (Exception e)
            {
                // user selected summary item
                // do nothing
                Console.WriteLine(e.ToString());
            }

        }

        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases) //string accession, Dictionary<string, PeptideForTreeView> uniquePeptides, Dictionary<string, PeptideForTreeView> sharedPeptides)
        {            
            string seqCoverage = protein.Protein.BaseSequence;            
            map.Children.Clear();
            legendGrid.Children.Clear();

            double spacing = 22;
            int height = 10;
            int totalHeight = 0;
            int accumIndex = 0;

            var splitSeq = Split(seqCoverage, spacing);
            var peptidesToDraw = new List<InSilicoPep>();
            foreach (var protease in proteases)
            {
                peptidesToDraw.AddRange(PeptideByProteaseAndProtein[protein.Protein][protease]);
            }            
            var indices = new Dictionary<int, List<int>>();

            // draw sequence
            foreach (var line in splitSeq)
            {
                indices.Clear();
                for (int r = 0; r < line.Length; r++)
                {
                    SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 10, height), line[r].ToString().ToUpper(), Brushes.Black);
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

            SequenceCoverageMap.drawLegend(legend, ProteaseByColor, proteases, legendGrid);
        }
        
        private int CheckPartialMatch(InSilicoPep peptide, string line, int accumIndex)
        {
            int remaining = peptide.EndResidue - accumIndex - line.Length - 1;
            if (remaining >= 0)
            {
                return remaining;
            }

            return -1;
        }

        private List<string> Split(string sequence, double spacing)
        {
            int size = Convert.ToInt32(map.Width / spacing);
            var splitSequence = Enumerable.Range(0, sequence.Length / size).Select(i => sequence.Substring(i * size, size)).ToList();
            splitSequence.Add(sequence.Substring(splitSequence.Count() * size));

            return splitSequence;
        }
                
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
                var proteaseList = ptv.AllPeptides.Select(p => p.Protease).Distinct().ToList();
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
            mapViewer.Height = 0.75 * ResultsGrid.ActualHeight;
            mapViewer.Width = 0.75 * ResultsGrid.ActualWidth;

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
            Proteases = PeptideByFile.SelectMany(p => p.Value.Keys).ToList();
            ListBox combo = sender as ListBox;
            combo.ItemsSource = Proteases;            
        }

        private void exportCoverageMap(object sender, RoutedEventArgs e)
        {
            var fileDirectory = UserParams.OutputFolder;
            var fileName = String.Concat("SequenceCoverageMap.png");
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
        }
    }
}
