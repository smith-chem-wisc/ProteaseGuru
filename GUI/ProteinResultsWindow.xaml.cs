using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Omics.BioPolymer;
using Omics.Modifications;
using Proteomics;
using Tasks;
using Tasks.CoverageMapConfiguration;

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
        /// Analyzer that organizes and calculates protein coverage data
        /// </summary>
        private readonly ProteinCoverageAnalyzer _analyzer;

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
        /// Maps Protein objects to their tree view representation (GUI-specific)
        /// </summary>
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;

        /// <summary>
        /// Tracks peptides that span multiple lines in the coverage map
        /// Key: peptide, Value: (remaining residues to highlight, highlight row index)
        /// </summary>
        private Dictionary<InSilicoPep, (int, int)> partialPeptideMatches = new Dictionary<InSilicoPep, (int, int)>();

        /// <summary>
        /// Maps each protease name to a unique WPF color for visualization
        /// </summary>
        private Dictionary<string, Color> ProteaseByColor;

        /// <summary>
        /// Maps modification names to WPF brushes for PTM visualization
        /// </summary>
        private Dictionary<string, SolidColorBrush> ModsByColor;

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
        private readonly Parameters UserParams;

        /// <summary>
        /// Counter for generating unique protein export folder names
        /// </summary>
        private int ProteinExportCount = 1;

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
        public ProteinResultsWindow(
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile,
            Parameters userParams,
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease)
        {
            InitializeComponent();

            // Initialize the analyzer with the data
            _analyzer = new ProteinCoverageAnalyzer(peptideByFile, sequenceCoverageByProtease);

            // Initialize state
            UserParams = userParams;
            SelectedProteases = new List<string>();
            SelectedProtein = null;
            MessageShow = true;

            // Initialize GUI collections
            ProteinDigestionSummary = new ObservableCollection<ProteinSummaryForTreeView>();
            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();

            // Set up GUI-specific data structures
            SetUpProteinsForTreeView();
            PopulateProteinList();

            // Set up color mappings
            SetUpColorDictionaries();

            // Register window loaded event for cleanup handling
            this.Loaded += results_Loaded;

            // Set up search functionality with debounced text input
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Initialization Methods

        /// <summary>
        /// Creates GUI-specific ProteinForTreeView objects from analyzer results
        /// </summary>
        private void SetUpProteinsForTreeView()
        {
            foreach (var kvp in _analyzer.ProteinCoverageResults)
            {
                var protein = kvp.Key;
                var result = kvp.Value;

                var ptv = new ProteinForTreeView(
                    protein,
                    result.DisplayName,
                    result.AllPeptides,
                    result.UniquePeptides,
                    result.SharedPeptides);

                ProteinsForTreeView[protein] = ptv;
            }
        }

        /// <summary>
        /// Populates the protein list for UI binding
        /// </summary>
        private void PopulateProteinList()
        {
            foreach (var accession in _analyzer.ProteinAccessions)
            {
                proteinList.Add(accession);
                dataGridProteins.Items.Add(accession);
            }
            dataGridProteins.DataContext = proteinList;
        }

        /// <summary>
        /// Sets up WPF color dictionaries for proteases and modifications
        /// </summary>
        private void SetUpColorDictionaries()
        {
            // Create protease color map using configuration
            var rgbColorMap = CoverageMapConfiguration.CreateProteaseColorMap(_analyzer.Proteases);
            ProteaseByColor = rgbColorMap.ToDictionary(
                kvp => kvp.Key,
                kvp => ToWpfColor(kvp.Value));

            // Initialize modification color dictionary (populated during drawing)
            ModsByColor = new Dictionary<string, SolidColorBrush>();
        }

        #endregion

        #region Color Conversion Helpers

        /// <summary>
        /// Converts an RgbColor to a WPF Color
        /// </summary>
        private static Color ToWpfColor(RgbColor rgb)
        {
            return Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }

        /// <summary>
        /// Converts an RgbColor to a WPF SolidColorBrush
        /// </summary>
        private static SolidColorBrush ToWpfBrush(RgbColor rgb)
        {
            return new SolidColorBrush(ToWpfColor(rgb));
        }

        /// <summary>
        /// Gets a WPF brush for a PTM based on its mass
        /// </summary>
        private SolidColorBrush GetPtmBrush(double mass)
        {
            var ptmName = CoverageMapConfiguration.GetPtmName(mass);
            var rgbColor = CoverageMapConfiguration.GetPtmColor(ptmName ?? "Other");

            // Track which mods we've seen for the legend
            var displayName = ptmName ?? "Other";
            if (!ModsByColor.ContainsKey(displayName))
            {
                ModsByColor[displayName] = ToWpfBrush(rgbColor);
            }

            return ToWpfBrush(rgbColor);
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

        /// <summary>
        /// Filters the protein list by checking if accession contains the search text.
        /// </summary>
        private void searchProtein(string txt)
        {
            filteredList.Clear();
            foreach (var protein in proteinList)
            {
                if (protein.Contains(txt, StringComparison.OrdinalIgnoreCase))
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
        /// Updates the selected proteases list and redraws the coverage map.
        /// </summary>
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

        /// <summary>
        /// Populates the protease selection list when the control loads.
        /// </summary>
        private void proteaseCoverageMaps_loaded(object sender, RoutedEventArgs e)
        {
            ListBox combo = sender as ListBox;
            combo.ItemsSource = _analyzer.Proteases;
        }

        #endregion

        #region Protein Selection and Summary

        /// <summary>
        /// Handles protein selection changes.
        /// Updates the summary statistics and redraws the coverage map.
        /// </summary>
        private void OnSelectionChanged()
        {
            // Show informational message about unique peptide definition (once per session)
            if (MessageShow)
            {
                string message = _analyzer.IsMultiDatabase
                    ? "Note: More than one protein database was analyzed. Unique peptides are defined as being unique to a single protein in all analyzed databases."
                    : "Note: One protein database was analyzed. Unique peptides are defined as being unique to a single protein in the analyzed database.";
                MessageBox.Show(message);
                MessageShow = false;
            }

            // Determine which protein is selected
            if (dataGridProteins.SelectedItem != null)
            {
                string proteinName = dataGridProteins.SelectedItem.ToString();
                var protein = ProteinsForTreeView.FirstOrDefault(p => p.Key.Accession == proteinName).Value;
                if (protein != null)
                {
                    SelectedProtein = protein;
                }
            }
            else
            {
                SelectedProtein = ProteinsForTreeView.FirstOrDefault().Value;
            }

            if (SelectedProtein == null) return;

            // Build summary using analyzer data
            BuildProteinSummary();

            // Redraw the sequence coverage map
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

        /// <summary>
        /// Builds the protein summary tree view using analyzer data
        /// </summary>
        private void BuildProteinSummary()
        {
            var coverageResult = _analyzer.ProteinCoverageResults[SelectedProtein.Protein];
            var proteaseList = UserParams.ProteasesForDigestion.Select(p => p.Name).ToList();

            var uniquePepCounts = coverageResult.GetUniquePeptideCountsByProtease();
            var sharedPepCounts = coverageResult.GetSharedPeptideCountsByProtease();

            // Create tree view structure
            var thisProtein = new ProteinSummaryForTreeView($"Digestion Results for {coverageResult.DisplayName}:");

            // Unique peptide counts
            var uniquePep = new AnalysisSummaryForTreeView("Number of Unique Peptides: ");
            foreach (var protease in proteaseList)
            {
                int count = uniquePepCounts.TryGetValue(protease, out var c) ? c : 0;
                uniquePep.Summary.Add(new ProtSummaryForTreeView($"{protease}: {count}"));
            }
            thisProtein.Summary.Add(uniquePep);

            // Shared peptide counts
            var sharedPep = new AnalysisSummaryForTreeView("Number of Shared Peptides: ");
            foreach (var protease in proteaseList)
            {
                int count = sharedPepCounts.TryGetValue(protease, out var c) ? c : 0;
                sharedPep.Summary.Add(new ProtSummaryForTreeView($"{protease}: {count}"));
            }
            thisProtein.Summary.Add(sharedPep);

            // Total sequence coverage
            var percentCov = new AnalysisSummaryForTreeView("Percent Sequence Coverage (all peptides):");
            foreach (var protease in _analyzer.SequenceCoverageByProtease)
            {
                var coverage = Math.Round(protease.Value[SelectedProtein.Protein].Item1, 2);
                percentCov.Summary.Add(new ProtSummaryForTreeView($"{protease.Key}: {Math.Round(coverage, 3)}%"));
            }
            thisProtein.Summary.Add(percentCov);

            // Unique peptide coverage
            var percentCovUniq = new AnalysisSummaryForTreeView("Percent Sequence Coverage (unique peptides):");
            if (_analyzer.IsMultiDatabase)
            {
                foreach (var (proteaseName, fraction) in _analyzer.CalculateSequenceCoverageUnique(SelectedProtein.Protein))
                {
                    percentCovUniq.Summary.Add(new ProtSummaryForTreeView($"{proteaseName}: {Math.Round(fraction, 3)}%"));
                }
            }
            else
            {
                foreach (var protease in _analyzer.SequenceCoverageByProtease)
                {
                    var coverage = protease.Value[SelectedProtein.Protein].Item2;
                    percentCovUniq.Summary.Add(new ProtSummaryForTreeView($"{protease.Key}: {Math.Round(coverage, 3)}%"));
                }
            }
            thisProtein.Summary.Add(percentCovUniq);

            // Update display
            ProteinDigestionSummary.Clear();
            ProteinDigestionSummary.Add(thisProtein);
            proteinResults.DataContext = ProteinDigestionSummary;
        }

        #endregion

        #region Sequence Coverage Map Drawing

        /// <summary>
        /// Main method for drawing the protein sequence coverage map.
        /// </summary>
        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            const int residuesPerLine = CoverageMapDataPreparer.DefaultResiduesPerLine;
            int height = 10;
            int totalHeight = 0;
            int accumIndex = 0;

            map.Width = 0.90 * ResultsGrid.ActualWidth;

            // Get protein data
            string seqCoverage = protein.Protein.BaseSequence;
            var mods = protein.Protein.OneBasedPossibleLocalizedModifications;
            var variants = protein.Protein.AppliedSequenceVariations;

            // Use CoverageMapDataPreparer for splitting
            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(seqCoverage, residuesPerLine);
            var modsSplitByLine = mods.Count > 0
                ? CoverageMapDataPreparer.SplitModificationsByLine(mods, protein.Protein.Length, residuesPerLine)
                : new List<Dictionary<int, List<Modification>>>();
            var variantsByLine = variants.Count > 0
                ? CoverageMapDataPreparer.SplitVariantsByLine(variants, protein.Protein.Length, residuesPerLine)
                : new List<List<int>>();

            // Clear previous drawing
            map.Children.Clear();
            legendGrid.Children.Clear();
            ModsByColor.Clear();

            // Collect peptides to draw (only from selected proteases)
            var peptidesToDraw = new List<InSilicoPep>();
            foreach (var protease in proteases)
            {
                peptidesToDraw.AddRange(_analyzer.GetPeptidesForProteinAndProtease(protein.Protein, protease));
            }
            peptidesToDraw = peptidesToDraw.Distinct().ToList();

            // Calculate covered residues from ALL peptides (all proteases)
            // Separates unique vs shared coverage for proper text styling
            var allPeptidesForProtein = _analyzer.GetAllPeptidesForProtein(protein.Protein);
            var (uniqueCovered, sharedOnlyCovered) = CalculateCoveredResiduesByType(allPeptidesForProtein);

            // Draw title
            var mapTitle = $"Sequence Coverage Map of {protein.Protein.Accession}:";
            var indices = new Dictionary<int, List<int>>();

            SequenceCoverageMap.txtDrawing(map, new Point(0, height), mapTitle, Brushes.Black);
            height += 30;
            int totalAddedSpace = 0;

            // Draw each line of the sequence
            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                indices.Clear();
                var lineLabel = (lineIndex * residuesPerLine) + 1;

                // Draw line number label
                SequenceCoverageMap.txtDrawingLabel(map, new Point(0, height), lineLabel.ToString(), Brushes.Black);

                // Draw sequence characters with coverage information
                int lineStartResidue = lineIndex * residuesPerLine + 1; // 1-based
                DrawSequenceCharacters(line, lineIndex, variantsByLine, height, residuesPerLine, uniqueCovered, sharedOnlyCovered, lineStartResidue);

                // Draw modification indicators
                if (mods.Count > 0 && lineIndex < modsSplitByLine.Count)
                {
                    DrawModifications(modsSplitByLine[lineIndex], height, residuesPerLine);
                }

                // Continue highlighting partial peptides from previous line
                ProcessPartialPeptides(line, accumIndex, height, indices);

                // Draw peptide highlights for peptides starting on this line
                DrawPeptideHighlights(line, accumIndex, height, indices, peptidesToDraw);

                // Calculate extra space for overlapping peptides
                int addedSpace = indices.Count > 7 ? (indices.Count - 7) * 10 : 0;
                totalAddedSpace += addedSpace;
                height += 100 + addedSpace;
                accumIndex += line.Length;
            }

            // Set final map height
            totalHeight = (splitSeq.Count * 100) + totalAddedSpace;
            map.Height = totalHeight + 100;

            // Draw legend
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
        /// Calculates which residues are covered by at least one peptide.
        /// Returns a HashSet of 1-based residue positions that are covered.
        /// </summary>
        private HashSet<int> CalculateCoveredResidues(List<InSilicoPep> peptides)
        {
            var coveredResidues = new HashSet<int>();

            foreach (var peptide in peptides)
            {
                // StartResidue and EndResidue are 1-based positions
                for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                {
                    coveredResidues.Add(i);
                }
            }

            return coveredResidues;
        }

        /// <summary>
        /// Calculates which residues are covered by unique peptides vs shared peptides.
        /// Returns two HashSets: one for unique coverage, one for shared-only coverage.
        /// </summary>
        private (HashSet<int> uniqueCovered, HashSet<int> sharedOnlyCovered) CalculateCoveredResiduesByType(List<InSilicoPep> peptides)
        {
            var uniqueCovered = new HashSet<int>();
            var sharedCovered = new HashSet<int>();

            foreach (var peptide in peptides)
            {
                // Determine if peptide is unique based on multi-database setting
                bool isUnique = _analyzer.IsMultiDatabase ? peptide.UniqueAllDbs : peptide.Unique;

                for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                {
                    if (isUnique)
                    {
                        uniqueCovered.Add(i);
                    }
                    else
                    {
                        sharedCovered.Add(i);
                    }
                }
            }

            // Shared-only means covered by shared but NOT by any unique peptide
            var sharedOnlyCovered = new HashSet<int>(sharedCovered.Except(uniqueCovered));

            return (uniqueCovered, sharedOnlyCovered);
        }

        /// <summary>
        /// Draws sequence characters with three styles:
        /// - Covered by unique peptides: Bold
        /// - Covered by shared peptides only: Normal weight (translucent)
        /// - Not covered: Normal weight, Underlined
        /// Variants are always shown in Red.
        /// </summary>
        private void DrawSequenceCharacters(string line, int lineIndex, List<List<int>> variantsByLine,
            int height, int spacing, HashSet<int> uniqueCovered, HashSet<int> sharedOnlyCovered, int lineStartResidue)
        {
            bool hasVariants = variantsByLine.Count > lineIndex && variantsByLine[lineIndex].Count > 0;

            for (int r = 0; r < line.Length; r++)
            {
                // Calculate the 1-based residue position in the full protein sequence
                int residuePosition = lineStartResidue + r;

                // Check if this is a variant position (r+1 is 1-based position within the line for variants)
                bool isVariant = hasVariants && variantsByLine[lineIndex].Contains(r + 1);

                // Determine coverage type
                bool isCoveredByUnique = uniqueCovered.Contains(residuePosition);
                bool isCoveredBySharedOnly = sharedOnlyCovered.Contains(residuePosition);

                var brush = isVariant ? Brushes.Red : Brushes.Black;
                string character = line[r].ToString().ToUpper();

                if (isCoveredByUnique)
                {
                    // Covered by unique peptides: Bold
                    SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 65, height), character, brush);
                }
                else if (isCoveredBySharedOnly)
                {
                    // Covered by shared peptides only: Normal weight, translucent (no underline)
                    SequenceCoverageMap.txtDrawingShared(map, new Point(r * spacing + 65, height), character, brush);
                }
                else
                {
                    // Not covered: Normal weight with underline
                    SequenceCoverageMap.txtDrawingUncovered(map, new Point(r * spacing + 65, height), character, brush);
                }
            }
        }

        /// <summary>
        /// Draws modification indicators as colored circles
        /// </summary>
        private void DrawModifications(Dictionary<int, List<Modification>> modsForLine, int height, int spacing)
        {
            foreach (var mod in modsForLine)
            {
                if (mod.Value.Count > 1)
                {
                    // Multiple mods at same position - stack circles
                    var colors = mod.Value
                        .Select(m => GetPtmBrush(Convert.ToDouble(m.MonoisotopicMass)))
                        .ToList();
                    SequenceCoverageMap.stackedCircledTxtDraw(map, new Point(mod.Key * spacing + 38, height), colors);
                }
                else
                {
                    // Single mod - draw one circle
                    var mass = Convert.ToDouble(mod.Value.First().MonoisotopicMass);
                    var brush = GetPtmBrush(mass);
                    SequenceCoverageMap.circledTxtDraw(map, new Point(mod.Key * spacing + 38, height), brush);
                }
            }
        }

        /// <summary>
        /// Processes peptides that span from previous lines
        /// </summary>
        private void ProcessPartialPeptides(string line, int accumIndex, int height, Dictionary<int, List<int>> indices)
        {
            if (partialPeptideMatches.Count == 0) return;

            var temp = new Dictionary<InSilicoPep, (int, int)>(partialPeptideMatches);
            partialPeptideMatches.Clear();

            foreach (var peptide in temp)
            {
                var remaining = peptide.Value.Item1;
                var highlightIndex = peptide.Value.Item2;

                int start = 0;
                int end = Math.Min(remaining, line.Length - 1);
                var partialIndex = CoverageMapDataPreparer.CheckPartialMatch(peptide.Key, line.Length, accumIndex);
                bool isUnique = _analyzer.IsMultiDatabase ? peptide.Key.UniqueAllDbs : peptide.Key.Unique;

                if (partialIndex >= 0)
                {
                    SequenceCoverageMap.Highlight(start, end, map, indices, height,
                        ProteaseByColor[peptide.Key.Protease], isUnique, false, false, highlightIndex);
                    partialPeptideMatches.Add(peptide.Key, (partialIndex, highlightIndex));
                }
                else
                {
                    SequenceCoverageMap.Highlight(start, end, map, indices, height,
                        ProteaseByColor[peptide.Key.Protease], isUnique, false, true, highlightIndex);
                }
            }
        }

        /// <summary>
        /// Draws peptide highlights for peptides starting on this line
        /// </summary>
        private void DrawPeptideHighlights(string line, int accumIndex, int height, Dictionary<int, List<int>> indices, List<InSilicoPep> peptidesToDraw)
        {
            var peptidesOnThisLine = peptidesToDraw
                .Where(p => p.StartResidue - accumIndex - 1 < line.Length)
                .OrderBy(p => p.StartResidue)
                .ToList();

            foreach (var peptide in peptidesOnThisLine)
            {
                var partialIndex = CoverageMapDataPreparer.CheckPartialMatch(peptide, line.Length, accumIndex);
                int start = peptide.StartResidue - accumIndex - 1;
                int end = Math.Min(peptide.EndResidue - accumIndex - 1, line.Length - 1);
                bool isUnique = _analyzer.IsMultiDatabase ? peptide.UniqueAllDbs : peptide.Unique;

                if (partialIndex >= 0)
                {
                    var highlightIndex = SequenceCoverageMap.Highlight(start, end, map, indices, height,
                        ProteaseByColor[peptide.Protease], isUnique, true, false);
                    if (!partialPeptideMatches.ContainsKey(peptide))
                    {
                        partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
                    }
                }
                else
                {
                    SequenceCoverageMap.Highlight(start, end, map, indices, height,
                        ProteaseByColor[peptide.Protease], isUnique, true, true);
                }
                peptidesToDraw.Remove(peptide);
            }
        }

        #endregion

        #region Event Handlers

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e) => OnSelectionChanged();

        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnSelectionChanged();

        private void resultsSizeChanged(object sender, SizeChangedEventArgs e) => ChangeMapScrollViewSize();

        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = .8 * ResultsGrid.ActualHeight;
            mapViewer.Width = .99 * ResultsGrid.ActualWidth;
        }

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;
        }

        void window_Closing(object sender, global::System.ComponentModel.CancelEventArgs e)
        {
            SearchModifications.Timer.Tick -= searchBox_TextChangedHandler;
        }

        #endregion

        #region Export Functionality

        private void saveMapToPDF(Grid myGrid)
        {
            PrintDialog pd = new PrintDialog();
            pd.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF");
            pd.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
            pd.PrintTicket.PageScalingFactor = 100;
            pd.PrintVisual(myGrid, "coverage map");
        }

        private void exportCoverageMap(object sender, RoutedEventArgs e)
        {
            var fileDirectory = UserParams.OutputFolder + @"\ProteaseGuruDigestionResults";
            string subFolder = Path.Combine(fileDirectory, SelectedProtein.DisplayName);
            string proteinName = SelectedProtein.DisplayName;

            if (subFolder.IndexOfAny(Path.GetInvalidPathChars()) != -1)
            {
                proteinName = "Protein" + ProteinExportCount++;
                MessageBox.Show($"Warning: Protein accession contains invalid characters. Using '{proteinName}' instead.");
                subFolder = Path.Combine(fileDirectory, proteinName);
            }

            saveMapToPDF(mapGrid);
            Directory.CreateDirectory(subFolder);

            // Render and save PNG
            var fileName = $"SequenceCoverageMap_{proteinName}.png";
            Rect bounds = VisualTreeHelper.GetDescendantBounds(mapGrid);
            var rtb = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, 96d, 96d, PixelFormats.Default);
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(mapGrid), null, new Rect(new Point(), bounds.Size));
            }
            rtb.Render(dv);

            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            pngEncoder.Save(ms);
            var filePath = Path.Combine(subFolder, fileName);
            File.WriteAllBytes(filePath, ms.ToArray());

            // Save results summary
            var resultsFile = $"{proteinName}_DigestionResults.txt";
            var results = new List<string>();
            foreach (var protein in ProteinDigestionSummary)
            {
                results.Add(protein.DisplayName);
                foreach (var analysis in protein.Summary)
                {
                    results.Add($"   {analysis.DisplayName}");
                    foreach (var protease in analysis.Summary)
                    {
                        results.Add($"       {protease.DisplayName}");
                    }
                }
            }
            File.WriteAllLines(Path.Combine(subFolder, resultsFile), results);

            // Get peptides using analyzer
            var allPeptides = _analyzer.GetAllPeptidesForProtein(SelectedProtein.Protein);
            var uniquePeptides = allPeptides.Where(p => _analyzer.IsMultiDatabase ? p.UniqueAllDbs : p.Unique).ToList();

            // Save metadata
            SaveMetadata(subFolder, proteinName, SelectedProtein.Protein, allPeptides);

            // Save peptide TSV files
            string header = BuildPeptideHeader();
            WritePeptidesToTsv(allPeptides, subFolder, proteinName, header, "ProteaseGuruPeptides");
            if (uniquePeptides.Count > 0)
            {
                WritePeptidesToTsv(uniquePeptides, subFolder, proteinName, header, "ProteaseGuruUniquePeptides");
            }

            // Offer to copy paths
            if (MessageBox.Show($"Files created at {subFolder}! Copy paths to clipboard?", "", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var clipboardText = $"Coverage Map: {filePath}\r\nResults: {Path.Combine(subFolder, resultsFile)}";
                Clipboard.SetText(clipboardText);
            }
        }

        private void SaveMetadata(string subFolder, string proteinName, Protein protein, List<InSilicoPep> allPeptides)
        {
            const string tab = "\t";
            var metaData = new List<string>
            {
                $"MetaData for {protein.Accession} Sequence Coverage Map",
                "Protein Sequence",
                protein.BaseSequence,
                "Sequence Variations",
                "Start Residue\tEnd Residue\tOriginal Sequence\tVariant Sequence"
            };

            foreach (var variant in protein.AppliedSequenceVariations)
            {
                metaData.Add($"{variant.OneBasedBeginPosition}{tab}{variant.OneBasedEndPosition}{tab}{variant.OriginalSequence}{tab}{variant.VariantSequence}");
            }

            metaData.Add("Post-Translational Modifications");
            metaData.Add("Residue\tModifications");
            foreach (var mod in protein.OneBasedPossibleLocalizedModifications)
            {
                metaData.Add($"{mod.Key}{tab}{string.Join(",", mod.Value.Select(m => m.IdWithMotif))}");
            }

            metaData.Add("All Peptides");
            metaData.Add("Start Residue\tEnd Residue\tProtease\tUnique");
            foreach (var peptide in allPeptides.Select(p => $"{p.StartResidue}{tab}{p.EndResidue}{tab}{p.Protease}{tab}{p.UniqueAllDbs}").Distinct())
            {
                metaData.Add(peptide);
            }

            File.WriteAllLines(Path.Combine(subFolder, $"{proteinName}_MapMetaData.txt"), metaData);
        }

        private static string BuildPeptideHeader()
        {
            return string.Join("\t",
                "Database", "Protease", "Base Sequence", "Full Sequence", "Previous Amino Acid",
                "Next Amino Acid", "Start Residue", "End Residue", "Length", "Molecular Weight",
                "Protein Accession", "Protein Name", "Unique Peptide (in this database)",
                "Unique Peptide (in all databases)", "Peptide sequence exclusive to this Database",
                "Hydrophobicity", "Electrophoretic Mobility");
        }

        private void WritePeptidesToTsv(List<InSilicoPep> peptides, string subFolder, string proteinName, string header, string filePrefix)
        {
            const int maxPerFile = 1000000;
            int fileCount = 1;
            int peptideIndex = 0;

            while (peptideIndex < peptides.Count)
            {
                var filePath = Path.Combine(subFolder, $"{filePrefix}_{proteinName}_{fileCount}.tsv");
                using var output = new StreamWriter(filePath);
                output.WriteLine(header);

                var written = new HashSet<string>();
                int inFile = 0;
                while (inFile < maxPerFile && peptideIndex < peptides.Count)
                {
                    var line = peptides[peptideIndex++].ToString();
                    if (written.Add(line))
                    {
                        output.WriteLine(line);
                    }
                    inFile++;
                }
                fileCount++;
            }
        }

        #endregion
    }
}
