using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Engine;
using Omics.BioPolymer;
using Omics.Modifications;
using Proteomics;
using Tasks;

namespace GUI
{
    /// <summary>
    /// Interaction logic for ProteinResultsWindow.xaml
    /// This window displays protein-level digestion results including:
    /// - A searchable list of proteins from the digested database(s)
    /// - Sequence coverage maps showing which regions are covered by peptides
    /// - Summary statistics (unique/shared peptide counts, coverage percentages)
    /// - Export functionality for coverage maps and peptide data
    /// </summary>
    public partial class ProteinResultsWindow : UserControl
    {
        #region Private Fields

        /// <summary>
        /// Complete list of protein accessions from all databases
        /// </summary>
        private ObservableCollection<string> proteinList;

        /// <summary>
        /// Filtered list of proteins based on user search input
        /// </summary>
        private ObservableCollection<string> filteredList;

        /// <summary>
        /// Tree view data for displaying protein digestion summary statistics
        /// </summary>
        private ObservableCollection<ProteinSummaryForTreeView> ProteinDigestionSummary;

        /// <summary>
        /// Master data structure: Database -> Protease -> Protein -> Peptides
        /// Contains all peptide results organized hierarchically
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;

        /// <summary>
        /// Reorganized peptide data: Protein -> Protease -> Peptides
        /// Used for quick lookup when drawing coverage maps
        /// </summary>
        private Dictionary<Protein, Dictionary<string, List<InSilicoPep>>> PeptideByProteaseAndProtein;

        /// <summary>
        /// Maps Protein objects to their tree view representation
        /// </summary>
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;

        /// <summary>
        /// Tracks peptides that span multiple lines in the coverage map
        /// Key: peptide, Value: (remaining residues to highlight, highlight row index)
        /// </summary>
        private Dictionary<InSilicoPep, (int, int)> partialPeptideMatches = new Dictionary<InSilicoPep, (int, int)>();

        /// <summary>
        /// Maps each protease name to a unique color for visualization
        /// </summary>
        private Dictionary<string, Color> ProteaseByColor;

        /// <summary>
        /// Maps modification names to colors for PTM visualization
        /// </summary>
        private Dictionary<string, SolidColorBrush> ModsByColor;

        /// <summary>
        /// List of all proteases used in the digestion
        /// </summary>
        private List<string> Proteases;

        /// <summary>
        /// Currently selected proteases for coverage map display
        /// </summary>
        private List<string> SelectedProteases;

        /// <summary>
        /// Currently selected protein being displayed
        /// </summary>
        private ProteinForTreeView SelectedProtein;

        /// <summary>
        /// Flag to show database count message only once per session
        /// </summary>
        private bool MessageShow;

        /// <summary>
        /// User-specified digestion parameters
        /// </summary>
        Parameters UserParams;

        /// <summary>
        /// Sequence coverage statistics: Protease -> Protein -> (total coverage, unique coverage)
        /// </summary>
        private Dictionary<string, Dictionary<Protein, (double, double)>> SequenceCoverageByProtease;

        /// <summary>
        /// Counter for generating unique protein export folder names
        /// </summary>
        int ProteinExportCount = 1;

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor required for XAML designer
        /// </summary>
        public ProteinResultsWindow()
        {
        }

        /// <summary>
        /// Main constructor that initializes the protein results view with digestion data
        /// </summary>
        /// <param name="peptideByFile">Hierarchical peptide data: Database -> Protease -> Protein -> Peptides</param>
        /// <param name="userParams">User-specified digestion parameters</param>
        /// <param name="sequenceCoverageByProtease">Pre-calculated sequence coverage statistics</param>
        public ProteinResultsWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile,
            Parameters userParams,
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease)
        {
            InitializeComponent();

            // Initialize collections and state
            SelectedProteases = new List<string>();
            UserParams = userParams;
            SelectedProtein = null;
            PeptideByFile = peptideByFile;
            SequenceCoverageByProtease = sequenceCoverageByProtease;
            MessageShow = true; // Will show database count message on first protein selection

            // Initialize data structures
            PeptideByProteaseAndProtein = new Dictionary<Protein, Dictionary<string, List<InSilicoPep>>>();
            ProteinDigestionSummary = new ObservableCollection<ProteinSummaryForTreeView>();
            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();

            // Populate the protein list and organize data for display
            SetUpTreeView();
            dataGridProteins.DataContext = proteinList;

            // Set up color mappings for proteases and modifications
            SetUpDictionaries();

            // Register window loaded event for cleanup handling
            this.Loaded += results_Loaded;

            // Set up search functionality with debounced text input
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Initialization Methods

        /// <summary>
        /// Sets up color dictionaries for proteases and modifications.
        /// Each protease gets a unique color for visual distinction in coverage maps.
        /// </summary>
        public void SetUpDictionaries()
        {
            // Define a palette of 29 distinct colors for protease visualization
            List<Color> colors = new List<Color>()
            {
                Color.FromRgb(130, 88, 159),   // Purple
                Color.FromRgb(0, 148, 50),     // Green
                Color.FromRgb(181, 52, 113),   // Magenta
                Color.FromRgb(52, 152, 219),   // Blue
                Color.FromRgb(230, 126, 34),   // Orange
                Color.FromRgb(27, 20, 100),    // Dark Blue
                Color.FromRgb(253, 167, 223),  // Pink
                Color.FromRgb(99, 110, 114),   // Gray
                Color.FromRgb(255, 221, 89),   // Yellow
                Color.FromRgb(162, 155, 254),  // Light Purple
                Color.FromRgb(58, 227, 116),   // Light Green
                Color.FromRgb(252, 66, 123),   // Hot Pink
                Color.FromRgb(126, 214, 223),  // Cyan
                Color.FromRgb(249, 127, 81),   // Coral
                Color.FromRgb(189, 195, 199),  // Silver
                Color.FromRgb(241, 196, 15),   // Gold
                Color.FromRgb(0, 98, 102),     // Teal
                Color.FromRgb(142, 68, 173),   // Violet
                Color.FromRgb(225, 112, 85),   // Salmon
                Color.FromRgb(255, 184, 184),  // Light Pink
                Color.FromRgb(61, 193, 211),   // Sky Blue
                Color.FromRgb(224, 86, 253),   // Bright Purple
                Color.FromRgb(196, 229, 56),   // Lime
                Color.FromRgb(255, 71, 87),    // Red
                Color.FromRgb(88, 177, 159),   // Sea Green
                Color.FromRgb(111, 30, 81),    // Maroon
                Color.FromRgb(129, 236, 236),  // Aqua
                Color.FromRgb(179, 57, 57),    // Dark Red
                Color.FromRgb(232, 67, 147)    // Deep Pink
            };

            ProteaseByColor = new Dictionary<string, Color>();
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            // Get all unique proteases from the data and assign colors
            var proteases = PeptideByFile.SelectMany(p => p.Value.Keys).Distinct().ToList();
            foreach (var protease in proteases)
            {
                ProteaseByColor.Add(protease, colors.ElementAt(proteases.IndexOf(protease)));
            }
        }

        /// <summary>
        /// Organizes peptide data into a structure suitable for tree view display.
        /// Creates a protein list and maps proteins to their peptides by protease.
        /// </summary>
        private void SetUpTreeView()
        {
            // Use HashSet to avoid duplicate protein accessions
            HashSet<string> proteinListDuplicates = new HashSet<string>();

            // Iterate through all databases, proteases, and proteins
            foreach (var db in PeptideByFile)
            {
                foreach (var protease in db.Value)
                {
                    foreach (var protein in protease.Value)
                    {
                        var prot = protein.Key;
                        proteinListDuplicates.Add(prot.Accession);

                        var peptidesByProtease = new Dictionary<string, List<InSilicoPep>>();

                        // Add or update protein entry in the reorganized dictionary
                        if (PeptideByProteaseAndProtein.ContainsKey(prot))
                        {
                            // Protein exists - add peptides to existing or new protease entry
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
                            // New protein - create new entries
                            peptidesByProtease.Add(protease.Key, protein.Value);
                            PeptideByProteaseAndProtein.Add(prot, peptidesByProtease);

                            // Create tree view representation for this protein
                            var name = prot.Accession ?? prot.Name;
                            var newPtv = new ProteinForTreeView(prot, name,
                                new List<InSilicoPep>(),
                                new List<InSilicoPep>(),
                                new List<InSilicoPep>());
                            ProteinsForTreeView.Add(prot, newPtv);
                        }

                        // Categorize peptides as unique or shared
                        // When multiple databases are analyzed, use UniqueAllDbs flag
                        // When single database, use the simpler Unique flag
                        if (PeptideByFile.Keys.Count > 1)
                        {
                            ProteinsForTreeView[prot].AllPeptides.AddRange(protein.Value);
                            ProteinsForTreeView[prot].UniquePeptides.AddRange(protein.Value.Where(p => p.UniqueAllDbs));
                            ProteinsForTreeView[prot].SharedPeptides.AddRange(protein.Value.Where(p => !p.UniqueAllDbs));
                        }
                        else
                        {
                            ProteinsForTreeView[prot].AllPeptides.AddRange(protein.Value);
                            ProteinsForTreeView[prot].UniquePeptides.AddRange(protein.Value.Where(p => p.Unique));
                            ProteinsForTreeView[prot].SharedPeptides.AddRange(protein.Value.Where(p => !p.Unique));
                        }
                    }
                }
            }

            // Populate the observable collections for UI binding
            foreach (var prot in proteinListDuplicates)
            {
                proteinList.Add(prot);
                dataGridProteins.Items.Add(prot);
            }
        }

        #endregion

        #region Search Functionality

        /// <summary>
        /// Event handler for search text box changes.
        /// Triggers debounced search timer to avoid searching on every keystroke.
        /// </summary>
        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchModifications.SetTimer();
        }

        /// <summary>
        /// Handles the debounced search timer tick.
        /// Filters the protein list based on user input.
        /// </summary>
        private void searchBox_TextChangedHandler(object sender, EventArgs e)
        {
            string userInput = SearchTextBox.Text;

            // If search is empty, show all proteins
            if (string.IsNullOrEmpty(userInput))
            {
                dataGridProteins.DataContext = proteinList;
                return;
            }

            // Filter proteins and update display
            searchProtein(userInput);
            dataGridProteins.Items.Clear();
            foreach (var entry in filteredList)
            {
                dataGridProteins.Items.Add(entry);
            }

            SearchModifications.Timer.Stop();
        }

        /// <summary>
        /// Filters the protein list by checking if accession contains the search text.
        /// Search is case-insensitive (converts input to uppercase).
        /// </summary>
        /// <param name="txt">Search text entered by user</param>
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

        #endregion

        #region Protease Selection

        /// <summary>
        /// Clears all selected proteases and redraws the coverage map without peptide overlays.
        /// </summary>
        private void ClearSelectedProteases_Click(object sender, RoutedEventArgs e)
        {
            ProteaseSelectedForUse.SelectedItems.Clear();
            SelectedProteases.Clear();
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

        /// <summary>
        /// Updates the selected proteases list and redraws the coverage map
        /// to show peptides from the selected proteases.
        /// </summary>
        private void SelectProteases_Click(object sender, RoutedEventArgs e)
        {
            SelectedProteases.Clear();
            foreach (var protease in ProteaseSelectedForUse.SelectedItems)
            {
                SelectedProteases.Add(protease.ToString());
            }

            // Use first protein if none selected
            if (SelectedProtein == null)
            {
                DrawSequenceCoverageMap(ProteinsForTreeView.FirstOrDefault().Value, SelectedProteases);
            }
            else
            {
                DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
            }
        }

        /// <summary>
        /// Populates the protease selection list when the control loads.
        /// </summary>
        private void proteaseCoverageMaps_loaded(object sender, RoutedEventArgs e)
        {
            Proteases = PeptideByFile.SelectMany(p => p.Value.Keys).Distinct().ToList();
            ListBox combo = sender as ListBox;
            combo.ItemsSource = Proteases;
        }

        #endregion

        #region Protein Selection and Summary

        /// <summary>
        /// Handles protein selection changes.
        /// Updates the summary statistics and redraws the coverage map for the selected protein.
        /// </summary>
        private void OnSelectionChanged()
        {
            // Show informational message about unique peptide definition (once per session)
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

            // Determine which protein is selected
            if (dataGridProteins.SelectedItem != null)
            {
                string proteinName = dataGridProteins.SelectedItem.ToString();
                var protein = ProteinsForTreeView.Where(p => p.Key.Accession == proteinName).FirstOrDefault().Value;
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }
            else
            {
                // Default to first protein if none selected
                var protein = ProteinsForTreeView.FirstOrDefault().Value;
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }

            // Build summary statistics for the selected protein
            var ptv = SelectedProtein;
            var proteaseList = UserParams.ProteasesForDigestion.Select(p => p.Name).ToList();

            // Group peptides by protease for counting
            var uniquePeps = ptv.UniquePeptides.GroupBy(p => p.Protease).ToDictionary(group => group.Key, group => group.ToList());
            var sharedPeps = ptv.SharedPeptides.GroupBy(p => p.Protease).ToDictionary(group => group.Key, group => group.ToList());

            // Create tree view structure for summary display
            ProteinSummaryForTreeView thisProtein = new ProteinSummaryForTreeView("Digestion Results for " + ptv.Protein.Accession + ":");

            // Add unique peptide counts by protease
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

            // Add shared peptide counts by protease
            AnalysisSummaryForTreeView sharedPep = new AnalysisSummaryForTreeView("Number of Shared Peptides: ");
            foreach (var protease in proteaseList)
            {
                if (sharedPeps.ContainsKey(protease))
                {
                    sharedPep.Summary.Add(new ProtSummaryForTreeView(protease + ": " + sharedPeps[protease].ToHashSet().Count()));
                }
                else
                {
                    sharedPep.Summary.Add(new ProtSummaryForTreeView(protease + ": 0"));
                }
            }
            thisProtein.Summary.Add(sharedPep);

            // Add total sequence coverage percentages
            AnalysisSummaryForTreeView percentCov = new AnalysisSummaryForTreeView("Percent Sequence Coverage (all peptides):");
            foreach (var protease in SequenceCoverageByProtease)
            {
                var coverage = Math.Round(protease.Value[SelectedProtein.Protein].Item1, 2);
                percentCov.Summary.Add(new ProtSummaryForTreeView(protease.Key + ": " + Math.Round(coverage, 3) + "%"));
            }
            thisProtein.Summary.Add(percentCov);

            // Add unique peptide sequence coverage percentages
            AnalysisSummaryForTreeView percentCovUniq = new AnalysisSummaryForTreeView("Percent Sequence Coverage (unique peptides):");
            if (PeptideByFile.Keys.Count > 1)
            {
                // Recalculate for multi-database scenario using UniqueAllDbs
                foreach (var seqCovKvp in CalculateSequenceCoverageUnique(SelectedProtein.Protein))
                {
                    percentCovUniq.Summary.Add(new ProtSummaryForTreeView(seqCovKvp.Item1 + ": " + Math.Round(seqCovKvp.Item2, 3) + "%"));
                }
            }
            else
            {
                // Use pre-calculated values for single database
                foreach (var protease in SequenceCoverageByProtease)
                {
                    var coverage = protease.Value[SelectedProtein.Protein].Item2;
                    percentCovUniq.Summary.Add(new ProtSummaryForTreeView(protease.Key + ": " + Math.Round(coverage, 3) + "%"));
                }
            }
            thisProtein.Summary.Add(percentCovUniq);

            // Update the tree view display
            ProteinDigestionSummary.Clear();
            ProteinDigestionSummary.Add(thisProtein);
            proteinResults.DataContext = ProteinDigestionSummary;

            // Redraw the sequence coverage map
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

        /// <summary>
        /// Calculates sequence coverage using only unique peptides for multi-database analysis.
        /// </summary>
        /// <param name="protein">The protein to calculate coverage for</param>
        /// <returns>Enumerable of (protease name, coverage fraction) tuples</returns>
        public IEnumerable<(string, double)> CalculateSequenceCoverageUnique(Protein protein)
        {
            HashSet<InSilicoPep> peptides = new HashSet<InSilicoPep>();
            foreach (var proteaseKvp in PeptideByProteaseAndProtein[protein])
            {
                HashSet<int> coveredOneBasedResidues = new HashSet<int>();

                // Filter to unique peptides based on database count
                if (PeptideByFile.Keys.Count() > 1)
                {
                    peptides = proteaseKvp.Value.Where(p => p.UniqueAllDbs == true).ToHashSet();
                }
                else
                {
                    peptides = proteaseKvp.Value.Where(p => p.Unique == true).ToHashSet();
                }

                // Mark all residues covered by unique peptides
                foreach (var peptide in peptides)
                {
                    for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                    {
                        coveredOneBasedResidues.Add(i);
                    }
                }

                // Calculate coverage fraction
                var fract = (double)coveredOneBasedResidues.Count / protein.Length;
                yield return (proteaseKvp.Key, Math.Round(fract, 2));
            }
        }

        #endregion

        #region Sequence Coverage Map Drawing

        /// <summary>
        /// Splits modifications into groups for each line of the sequence display.
        /// Adjusts indices to be relative to each line.
        /// </summary>
        /// <param name="mods">All modifications on the protein</param>
        /// <param name="proteinLength">Total protein length</param>
        /// <param name="spacing">Number of residues per line</param>
        /// <returns>List of modification dictionaries, one per line</returns>
        private List<Dictionary<int, List<Modification>>> SplitMods(IDictionary<int, List<Modification>> mods, int proteinLength, int spacing)
        {
            // Calculate number of lines needed
            double round = proteinLength / spacing;
            var remainder = proteinLength % spacing;
            if (remainder > 0)
            {
                round = round + 1;
            }
            var splitCount = Convert.ToInt32(round);

            var splitMods = new List<Dictionary<int, List<Modification>>>();

            // Process each line
            for (int j = 0; j < splitCount; j++)
            {
                Dictionary<int, List<Modification>> modsInArea = new Dictionary<int, List<Modification>>();
                int min = 1 + (j * spacing);  // First residue on this line (1-based)
                int max = spacing + (j * spacing);  // Last residue on this line

                foreach (var entry in mods)
                {
                    if (entry.Key >= min && entry.Key <= max)
                    {
                        // Adjust index to be relative to line start
                        modsInArea.Add((entry.Key - (j * spacing)), entry.Value);
                    }
                }
                splitMods.Add(modsInArea);
            }
            return splitMods;
        }

        /// <summary>
        /// Splits sequence variations into groups for each line of the sequence display.
        /// Handles variants that span multiple lines.
        /// </summary>
        /// <param name="variants">All sequence variations on the protein</param>
        /// <param name="proteinLength">Total protein length</param>
        /// <param name="spacing">Number of residues per line</param>
        /// <returns>List of residue positions with variants, one list per line</returns>
        private List<List<int>> SplitVariations(List<SequenceVariation> variants, int proteinLength, int spacing)
        {
            // Calculate number of lines needed
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
                    // Case 1: Variant completely within this line
                    if (entry.OneBasedBeginPosition >= min && entry.OneBasedBeginPosition <= max &&
                        entry.OneBasedEndPosition >= min && entry.OneBasedEndPosition <= max)
                    {
                        // Add all positions from start to end
                        for (int pos = entry.OneBasedBeginPosition; pos <= entry.OneBasedEndPosition; pos++)
                        {
                            variantsInArea.Add(pos - (j * spacing));
                        }
                    }
                    // Case 2: Variant starts on this line but ends on a later line
                    else if (entry.OneBasedBeginPosition >= min && entry.OneBasedBeginPosition <= max &&
                             entry.OneBasedEndPosition > max)
                    {
                        // Add positions from start to end of line
                        for (int pos = entry.OneBasedBeginPosition; pos <= max; pos++)
                        {
                            variantsInArea.Add(pos - (j * spacing));
                        }
                    }
                    // Case 3: Variant ends on this line but started on an earlier line
                    else if (entry.OneBasedEndPosition >= min && entry.OneBasedEndPosition <= max &&
                             entry.OneBasedBeginPosition < min)
                    {
                        // Add positions from start of line to variant end
                        for (int pos = min; pos <= entry.OneBasedEndPosition; pos++)
                        {
                            variantsInArea.Add(pos - (j * spacing));
                        }
                    }
                    // Case 4: Variant spans entire line (starts before, ends after)
                    else if (entry.OneBasedBeginPosition < min && entry.OneBasedEndPosition > max)
                    {
                        // Add all positions on this line
                        for (int pos = min; pos <= max; pos++)
                        {
                            variantsInArea.Add(pos - (j * spacing));
                        }
                    }
                }
                splitVariants.Add(variantsInArea.Distinct().ToList());
            }
            return splitVariants;
        }

        /// <summary>
        /// Main method for drawing the protein sequence coverage map.
        /// Displays:
        /// - Protein sequence (25 residues per line)
        /// - Sequence variations (in red)
        /// - Post-translational modifications (colored circles)
        /// - Peptide coverage highlights for selected proteases
        /// </summary>
        /// <param name="protein">The protein to display</param>
        /// <param name="proteases">List of proteases whose peptides should be shown</param>
        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            // Layout constants
            double spacing = 25;  // Characters per line
            int height = 10;      // Starting Y position
            int totalHeight = 0;
            int accumIndex = 0;   // Tracks position in full sequence

            map.Width = 0.90 * ResultsGrid.ActualWidth;

            // Get protein data
            string seqCoverage = protein.Protein.BaseSequence;
            IDictionary<int, List<Modification>> mods = protein.Protein.OneBasedPossibleLocalizedModifications;
            var variants = protein.Protein.AppliedSequenceVariations;

            // Prepare modifications and variants for line-by-line display
            var modsSplitByLine = new List<Dictionary<int, List<Modification>>>();
            var variantsByLine = new List<List<int>>();

            // Define colors for common PTM types based on mass
            var modColors = new Dictionary<string, SolidColorBrush>();
            var modWeight = new Dictionary<double, string>();

            // Map PTM masses to names and colors
            modWeight.Add(42.0106, "Acetylation");
            modColors.Add("Acetylation", Brushes.Aqua);
            modWeight.Add(541.0611, "ADP-Ribosylation");
            modColors.Add("ADP-Ribosylation", Brushes.MediumAquamarine);
            modWeight.Add(70.0419, "Butyrylation");
            modColors.Add("Butyrylation", Brushes.LimeGreen);
            modWeight.Add(43.9898, "Carboxylation");
            modColors.Add("Carboxylation", Brushes.Lavender);
            modWeight.Add(0.9840, "Citrullination");
            modColors.Add("Citrullination", Brushes.MediumSlateBlue);
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

            // Split modifications by line if present
            if (mods.Count() != 0)
            {
                modsSplitByLine = SplitMods(mods, protein.Protein.Length, Convert.ToInt32(spacing));
            }

            // Split variants by line if present
            if (variants.Count() != 0)
            {
                variantsByLine = SplitVariations(variants, protein.Protein.Length, Convert.ToInt32(spacing));
            }

            // Clear previous drawing
            map.Children.Clear();
            legendGrid.Children.Clear();

            // Split sequence into lines for display
            var splitSeq = Split(seqCoverage, spacing);

            // Collect peptides to draw from selected proteases
            var peptidesToDraw = new List<InSilicoPep>();
            foreach (var protease in proteases)
            {
                if (PeptideByProteaseAndProtein[protein.Protein].ContainsKey(protease))
                {
                    peptidesToDraw.AddRange(PeptideByProteaseAndProtein[protein.Protein][protease]);
                }
            }

            // Draw title
            var mapTitle = "Sequence Coverage Map of " + protein.Protein.Accession + ":";
            peptidesToDraw = peptidesToDraw.Distinct().ToList();
            var indices = new Dictionary<int, List<int>>();  // Tracks highlight positions per row

            SequenceCoverageMap.txtDrawing(map, new Point(0, height), mapTitle, Brushes.Black);
            height = height + 30;
            int totalAddedSpace = 0;

            // Draw each line of the sequence
            foreach (var line in splitSeq)
            {
                indices.Clear();
                var lineCount = splitSeq.IndexOf(line);
                var lineLabel = (lineCount * 25) + 1;  // 1-based residue number

                // Draw line number label
                SequenceCoverageMap.txtDrawingLabel(map, new Point(0, height), lineLabel.ToString(), Brushes.Black);

                // Draw sequence characters, highlighting variants in red
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

                // Draw modification indicators (colored circles above residues)
                if (mods.Count() > 0)
                {
                    var modsForLine = modsSplitByLine[splitSeq.IndexOf(line)];
                    foreach (var mod in modsForLine)
                    {
                        SolidColorBrush color = Brushes.Orange;  // Default for unknown mods

                        if (mod.Value.Count() > 1)
                        {
                            // Multiple mods at same position - draw stacked circles
                            List<SolidColorBrush> colors = new List<SolidColorBrush>();
                            foreach (var m in mod.Value)
                            {
                                double roundedMass = Math.Round(Convert.ToDouble(m.MonoisotopicMass), 4, MidpointRounding.AwayFromZero);
                                if (modWeight.ContainsKey(roundedMass))
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
                            SequenceCoverageMap.stackedCircledTxtDraw(map, new Point((mod.Key) * spacing + 38, height), colors);
                        }
                        else
                        {
                            // Single mod - draw single circle
                            double roundedMass = Math.Round(Convert.ToDouble(mod.Value.FirstOrDefault().MonoisotopicMass), 4, MidpointRounding.AwayFromZero);
                            if (modWeight.ContainsKey(roundedMass))
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
                            SequenceCoverageMap.circledTxtDraw(map, new Point((mod.Key) * spacing + 38, height), color);
                        }
                    }
                }

                // Continue highlighting peptides that span from previous line
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

                        var partialIndex = CheckPartialMatch(peptide.Key, line, accumIndex);

                        // Use appropriate uniqueness flag based on database count
                        bool isUnique = PeptideByFile.Keys.Count > 1 ? peptide.Key.UniqueAllDbs : peptide.Key.Unique;

                        if (partialIndex >= 0)
                        {
                            // Peptide continues to next line
                            SequenceCoverageMap.Highlight(start, end, map, indices, height,
                                ProteaseByColor[peptide.Key.Protease], isUnique, false, false, highlightIndex);
                            partialPeptideMatches.Add(peptide.Key, (partialIndex, highlightIndex));
                        }
                        else
                        {
                            // Peptide ends on this line
                            SequenceCoverageMap.Highlight(start, end, map, indices, height,
                                ProteaseByColor[peptide.Key.Protease], isUnique, false, true, highlightIndex);
                        }
                    }
                }

                // Draw peptide highlights for peptides starting on this line
                for (int i = 0; i < line.Length; ++i)
                {
                    var temp = new List<InSilicoPep>(peptidesToDraw
                        .Where(p => p.StartResidue - accumIndex - 1 < line.Length)
                        .OrderBy(p => p.StartResidue));

                    foreach (InSilicoPep peptide in temp)
                    {
                        var partialIndex = CheckPartialMatch(peptide, line, accumIndex);

                        int start = peptide.StartResidue - accumIndex - 1;
                        int end = Math.Min(peptide.EndResidue - accumIndex - 1, line.Length - 1);

                        bool isUnique = PeptideByFile.Keys.Count > 1 ? peptide.UniqueAllDbs : peptide.Unique;

                        if (partialIndex >= 0)
                        {
                            // Peptide continues to next line
                            var highlightIndex = SequenceCoverageMap.Highlight(start, end, map, indices, height,
                                ProteaseByColor[peptide.Protease], isUnique, true, false);
                            if (!partialPeptideMatches.ContainsKey(peptide))
                            {
                                partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
                            }
                        }
                        else
                        {
                            // Peptide fits entirely on this line
                            SequenceCoverageMap.Highlight(start, end, map, indices, height,
                                ProteaseByColor[peptide.Protease], isUnique, true, true);
                        }
                        peptidesToDraw.Remove(peptide);
                    }
                }

                // Calculate extra space needed if many overlapping peptides
                int addedSpace = 0;
                if (indices.Count > 7)
                {
                    int extraPepLines = indices.Count - 7;
                    addedSpace = extraPepLines * 10;
                    totalAddedSpace = totalAddedSpace + addedSpace;
                }
                height += 100 + addedSpace;

                accumIndex += line.Length;
            }

            // Set final map height
            totalHeight += (splitSeq.Count() * 100) + totalAddedSpace;
            map.Height = totalHeight + 100;

            // Draw legend showing protease colors and modification colors
            if (mods.Count > 0)
            {
                SequenceCoverageMap.drawLegendMods(legend, ProteaseByColor, ModsByColor, proteases, legendGrid, variants.Count > 0);
            }
            else
            {
                SequenceCoverageMap.drawLegend(legend, ProteaseByColor, proteases, legendGrid, variants.Count > 0);
            }
        }

        /// <summary>
        /// Checks if a peptide spans beyond the current line.
        /// </summary>
        /// <param name="peptide">The peptide to check</param>
        /// <param name="line">Current line string</param>
        /// <param name="accumIndex">Accumulated index from previous lines</param>
        /// <returns>Number of remaining residues if partial match, -1 otherwise</returns>
        private int CheckPartialMatch(InSilicoPep peptide, string line, int accumIndex)
        {
            int remaining = peptide.EndResidue - accumIndex - line.Length - 1;
            if (remaining >= 0)
            {
                return remaining;
            }
            return -1;
        }

        /// <summary>
        /// Splits a protein sequence into fixed-length lines for display.
        /// </summary>
        /// <param name="sequence">Full protein sequence</param>
        /// <param name="spacing">Characters per line</param>
        /// <returns>List of sequence fragments</returns>
        private List<string> Split(string sequence, double spacing)
        {
            int size = Convert.ToInt32(spacing);
            var splitSequence = Enumerable.Range(0, sequence.Length / size)
                .Select(i => sequence.Substring(i * size, size))
                .ToList();

            // Handle remaining characters
            var lineText = sequence.Substring(splitSequence.Count() * size);
            if (lineText != "")
            {
                splitSequence.Add(lineText);
            }
            return splitSequence;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles protein selection changes in the data grid.
        /// </summary>
        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e)
        {
            OnSelectionChanged();
        }

        /// <summary>
        /// Handles protease selection changes in the combo box.
        /// </summary>
        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OnSelectionChanged();
        }

        /// <summary>
        /// Adjusts scroll view size when the window is resized.
        /// </summary>
        private void resultsSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ChangeMapScrollViewSize();
        }

        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = .8 * ResultsGrid.ActualHeight;
            mapViewer.Width = .99 * ResultsGrid.ActualWidth;
            ChangeMapScrollViewVisibility();
        }

        private void ChangeMapScrollViewVisibility()
        {
            // Placeholder for visibility logic
        }

        /// <summary>
        /// Registers window closing event when control is loaded.
        /// </summary>
        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;
        }

        /// <summary>
        /// Cleanup: unsubscribe from search timer when window closes.
        /// </summary>
        void window_Closing(object sender, global::System.ComponentModel.CancelEventArgs e)
        {
            SearchModifications.Timer.Tick -= new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Export Functionality

        /// <summary>
        /// Saves the coverage map grid to PDF using print dialog.
        /// </summary>
        private void saveMapToPDF(Grid myGrid)
        {
            PrintDialog pd = new PrintDialog();
            pd.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF");
            pd.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
            pd.PrintTicket.PageScalingFactor = 100;
            pd.PrintVisual(myGrid, "coverage map");
        }

        /// <summary>
        /// Exports the sequence coverage map, metadata, and peptide data to files.
        /// Creates:
        /// - PNG image of the coverage map
        /// - Text file with digestion results summary
        /// - Metadata file with sequence, variants, modifications, and peptide info
        /// - TSV files with all peptides and unique peptides
        /// </summary>
        private void exportCoverageMap(object sender, RoutedEventArgs e)
        {
            // Set up output directory
            var fileDirectory = UserParams.OutputFolder + @"\ProteaseGuruDigestionResults";
            string subFolder = System.IO.Path.Combine(fileDirectory, SelectedProtein.DisplayName);
            string proteinName = SelectedProtein.DisplayName;

            // Handle invalid characters in protein names
            if (subFolder.IndexOfAny(System.IO.Path.GetInvalidPathChars()) == -1)
            {
                Directory.CreateDirectory(subFolder);
            }
            else
            {
                proteinName = "Protein" + ProteinExportCount;
                ProteinExportCount++;
                MessageBox.Show("Warning: The accession of the protein selected contains invalid characters and has been replaced with -" + proteinName + "- for the generation of folder and file names.");
                subFolder = System.IO.Path.Combine(fileDirectory, proteinName);
            }

            // Save PDF
            saveMapToPDF(mapGrid);
            Directory.CreateDirectory(subFolder);

            // Render and save PNG image
            var fileName = String.Concat("SequenceCoverageMap_" + proteinName + ".png");
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

            // Save digestion results summary
            var resultsFile = String.Concat(proteinName + "_DigestionResults.txt");
            var proteinAccession = SelectedProtein.DisplayName;
            List<string> results = new List<string>();
            foreach (var protein in ProteinDigestionSummary)
            {
                results.Add(protein.DisplayName);
                foreach (var analysis in protein.Summary)
                {
                    results.Add("   " + analysis.DisplayName);
                    foreach (var protease in analysis.Summary)
                    {
                        results.Add("       " + protease.DisplayName);
                    }
                }
            }
            File.WriteAllLines(System.IO.Path.Combine(subFolder, resultsFile), results);

            // Collect all peptides for this protein
            var allPeptidesForProtein = PeptideByProteaseAndProtein.Where(p => p.Key.Accession == proteinAccession).FirstOrDefault();
            List<InSilicoPep> allPeptides = new List<InSilicoPep>();
            List<InSilicoPep> allPeptidesUnique = new List<InSilicoPep>();
            foreach (var protease in allPeptidesForProtein.Value)
            {
                allPeptides.AddRange(protease.Value);
                allPeptidesUnique.AddRange(protease.Value.Where(p => p.UniqueAllDbs == true));
            }

            // Save metadata file
            string tab = "\t";
            List<string> metaData = new List<string>();
            metaData.Add("MetaData for " + allPeptidesForProtein.Key.Accession + " Sequence Coverage Map");
            metaData.Add("Protein Sequence");
            metaData.Add(allPeptidesForProtein.Key.BaseSequence);

            // Add sequence variations
            metaData.Add("Sequence Variations");
            metaData.Add("Start Residue \t End Residue \t Original Sequence \t Variant Sequence");
            var sequenceVariants = allPeptidesForProtein.Key.AppliedSequenceVariations;
            foreach (var variant in sequenceVariants)
            {
                var line = variant.OneBasedBeginPosition + tab + variant.OneBasedEndPosition + tab + variant.OriginalSequence + tab + variant.VariantSequence;
                metaData.Add(line);
            }

            // Add PTM information
            metaData.Add("Post-Translational Modifications");
            metaData.Add("Residue \t Modifications");
            var mods = allPeptidesForProtein.Key.OneBasedPossibleLocalizedModifications;
            foreach (var mod in mods)
            {
                var modList = string.Join(',', mod.Value.Select(m => m.IdWithMotif));
                metaData.Add(mod.Key + tab + modList);
            }

            // Add peptide summary
            metaData.Add("All Peptides");
            metaData.Add("Start Residue \t End Residue \t Protease \t Unique");
            HashSet<string> peptideStrings = new HashSet<string>();
            foreach (var peptide in allPeptides)
            {
                var line = peptide.StartResidue + tab + peptide.EndResidue + tab + peptide.Protease + tab + peptide.UniqueAllDbs;
                peptideStrings.Add(line);
            }
            metaData.AddRange(peptideStrings);

            var metaFile = String.Concat(proteinName + "_MapMetaData.txt");
            File.WriteAllLines(System.IO.Path.Combine(subFolder, metaFile), metaData);

            // Save peptide TSV files (chunked if > 1 million peptides)
            string header = "Database" + tab + "Protease" + tab + "Base Sequence" + tab + "Full Sequence" + tab + "Previous Amino Acid" + tab +
                "Next Amino Acid" + tab + "Start Residue" + tab + "End Residue" + tab + "Length" + tab + "Molecular Weight" + tab + "Protein Accession" + tab + "Protein Name" + tab + "Unique Peptide (in this database)" + tab + "Unique Peptide (in all databases)" + tab + "Peptide sequence exclusive to this Database" + tab +
                "Hydrophobicity" + tab + "Electrophoretic Mobility";

            // Write all peptides
            WritePeptidesToTsv(allPeptides, subFolder, proteinName, header, "ProteaseGuruPeptides");

            // Write unique peptides if any exist
            if (allPeptidesUnique.Count != 0)
            {
                WritePeptidesToTsv(allPeptidesUnique, subFolder, proteinName, header, "ProteaseGuruUniquePeptides");
            }

            // Offer to copy file paths to clipboard
            string message = "PNG and txt files Created at " + subFolder + "! Would you like to copy the file paths?";
            var messageBox = MessageBox.Show(message, "", MessageBoxButton.YesNo);
            if (messageBox == MessageBoxResult.Yes)
            {
                var clipboardText = "Coverage Map: " + filePath +
                    "\r\n Coverage Map MetaData: " + System.IO.Path.Combine(subFolder, metaFile) +
                    "\r\nResults Summary File: " + System.IO.Path.Combine(subFolder, resultsFile) +
                    "\r\nAll Peptide Files: " + subFolder + @"\ProteaseGuruPeptides_" + proteinName + "_1.tsv";

                if (allPeptidesUnique.Count != 0)
                {
                    clipboardText += "\r\nUnique Peptides: " + subFolder + @"\ProteaseGuruUniquePeptides_" + proteinName + "_1.tsv";
                }
                Clipboard.SetText(clipboardText);
            }
        }

        /// <summary>
        /// Helper method to write peptides to TSV files, splitting into multiple files if needed.
        /// </summary>
        private void WritePeptidesToTsv(List<InSilicoPep> peptides, string subFolder, string proteinName, string header, string filePrefix)
        {
            var numberOfPeptides = peptides.Count();
            double numberOfFiles = Math.Ceiling(numberOfPeptides / 1000000.0);
            var peptidesInFile = 1;
            var peptideIndex = 0;
            var fileCount = 1;

            while (fileCount <= Convert.ToInt32(numberOfFiles))
            {
                using (StreamWriter output = new StreamWriter(subFolder + @"\" + filePrefix + "_" + proteinName + "_" + fileCount + ".tsv"))
                {
                    output.WriteLine(header);
                    HashSet<string> outputString = new HashSet<string>();
                    while (peptidesInFile < 1000000)
                    {
                        if (peptideIndex < numberOfPeptides)
                        {
                            outputString.Add(peptides[peptideIndex].ToString());
                            peptideIndex++;
                        }
                        peptidesInFile++;
                    }
                    foreach (var peptide in outputString)
                    {
                        output.WriteLine(peptide);
                    }
                    output.Close();
                    peptidesInFile = 1;
                }
                fileCount++;
            }
        }

        #endregion
    }
}
