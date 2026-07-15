using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Engine;
using Omics;
using Omics.BioPolymer;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;
using Tasks.CoverageMapConfiguration;

namespace ProteaseGuruGui
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

        /// <summary>Analyzer that organizes and calculates protein coverage data</summary>
        private readonly ProteinCoverageAnalyzer _analyzer;

        /// <summary>Complete list of protein accessions from all databases</summary>
        private ObservableCollection<string> proteinList;

        /// <summary>Filtered list of proteins based on user search input</summary>
        private ObservableCollection<string> filteredList;

        /// <summary>
        /// Maps Protein objects to their tree view representation (GUI-specific)
        /// </summary>
        private Dictionary<IBioPolymer, ProteinForTreeView> ProteinsForTreeView;

        /// <summary>Currently selected proteases for coverage map display</summary>
        private List<string> SelectedProteases;

        /// <summary>Currently selected protein being displayed</summary>
        private ProteinForTreeView SelectedProtein;

        /// <summary>Flag to show database count message only once per session</summary>
        private bool MessageShow;

        /// <summary>User-specified digestion parameters</summary>
        private readonly RunParameters UserParams;

        /// <summary>Counter for generating unique protein export folder names</summary>
        private int ProteinExportCount = 1;

        // ── Stable color map ──────────────────────────────────────────────────
        private readonly Dictionary<string, Color> _stableProteaseColors;
        private readonly Dictionary<string, SolidColorBrush> _stableProteaseBrushes;

        // ── Display mode toggle ──────────────────────────────────────────────
        private CoverageMapDisplayMode _displayMode = CoverageMapDisplayMode.PeptidePerBar;

        // ── Peptide-per-bar mode fields ───────────────────────────────────────
        private Dictionary<string, Color> ProteaseByColor;

        #endregion

        #region Constructors

        /// <summary>Default constructor required for XAML designer</summary>
        public ProteinResultsWindow()
        {
        }

        /// <summary>
        /// Main constructor that initializes the protein results view with digestion data
        /// </summary>
        public ProteinResultsWindow(
            Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile,
            RunParameters userParams,
            Dictionary<string, Dictionary<IBioPolymer, (double, double)>> sequenceCoverageByProtease)
        {
            InitializeComponent();

            _analyzer = new ProteinCoverageAnalyzer(peptideByFile, sequenceCoverageByProtease);
            UserParams = userParams;
            SelectedProteases = new List<string>();
            SelectedProtein = null;
            MessageShow = true;

            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<IBioPolymer, ProteinForTreeView>();

            SetUpProteinsForTreeView();
            PopulateProteinList();

            // Build stable color map from the full ProteaseDictionary
            (_stableProteaseColors, _stableProteaseBrushes) = SequenceCoverageMap.BuildStableColorMaps();

            // Build protease color map for peptide-per-bar mode
            var rgbColorMap = CoverageMapConfiguration.CreateProteaseColorMap(_analyzer.Proteases);
            ProteaseByColor = rgbColorMap.ToDictionary(kvp => kvp.Key, kvp => ToWpfColor(kvp.Value));

            this.Loaded += results_Loaded;
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Initialization Methods

        /// <summary>Creates GUI-specific ProteinForTreeView objects from analyzer results</summary>
        private void SetUpProteinsForTreeView()
        {
            foreach (var kvp in _analyzer.ProteinCoverageResults)
            {
                var protein = kvp.Key;
                var result = kvp.Value;
                var ptv = new ProteinForTreeView(
                    protein, result.DisplayName,
                    result.AllPeptides, result.UniquePeptides, result.SharedPeptides);
                ProteinsForTreeView[protein] = ptv;
            }
        }

        /// <summary>Populates the protein list for UI binding</summary>
        private void PopulateProteinList()
        {
            foreach (var accession in _analyzer.ProteinAccessions)
            {
                proteinList.Add(accession);
                dataGridProteins.Items.Add(accession);
            }
            dataGridProteins.DataContext = proteinList;
        }

        #endregion

        #region Color Conversion Helpers

        private static Color ToWpfColor(RgbColor rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);
        private static SolidColorBrush ToWpfBrush(RgbColor rgb) => new SolidColorBrush(ToWpfColor(rgb));

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
        /// Clears the explicit protease selection and reverts to showing all proteases.
        /// </summary>
        private void ClearSelectedProteases_Click(object sender, RoutedEventArgs e)
        {
            ProteaseSelectedForUse.SelectedItems.Clear();
            SelectedProteases.Clear();
            // Revert to showing all proteases from the run
            DrawSequenceCoverageMap(SelectedProtein, _analyzer.Proteases);
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

            var proteasesToDraw = SelectedProteases.Count > 0 ? SelectedProteases : _analyzer.Proteases;
            var targetProtein = SelectedProtein ?? ProteinsForTreeView.FirstOrDefault().Value;
            DrawSequenceCoverageMap(targetProtein, proteasesToDraw);
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
                NotificationService.Instance.AddNotification(message, NotificationType.Information);
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

            // Use explicitly selected proteases, or fall back to all proteases from the run
            var proteasesToDraw = SelectedProteases.Count > 0 ? SelectedProteases : _analyzer.Proteases;

            // Redraw the sequence coverage map
            DrawSequenceCoverageMap(SelectedProtein, proteasesToDraw);
        }

        /// <summary>
        /// Builds the protein summary table using analyzer data
        /// </summary>
        private void BuildProteinSummary()
        {
            var coverageResult = _analyzer.ProteinCoverageResults[SelectedProtein.Protein];
            var proteaseList = UserParams.ProteaseSpecificParameters.Select(p => p.DigestionAgentName).ToList();

            var uniquePepCounts = coverageResult.GetUniquePeptideCountsByProtease();
            var sharedPepCounts = coverageResult.GetSharedPeptideCountsByProtease();

            // Pre-compute unique coverage for multi-database scenario (once, not per-protease)
            Dictionary<string, double>? uniqueCoverageByProtease = null;
            if (_analyzer.IsMultiDatabase)
            {
                uniqueCoverageByProtease = _analyzer
                    .CalculateSequenceCoverageUnique(SelectedProtein.Protein)
                    .ToDictionary(x => x.ProteaseName, x => x.CoverageFraction);
            }

            // Build table rows
            var summaryRows = new List<ProteinDigestionSummaryRow>();

            foreach (var proteaseName in proteaseList)
            {
                int uniqueCount = uniquePepCounts.TryGetValue(proteaseName, out var uc) ? uc : 0;
                int sharedCount = sharedPepCounts.TryGetValue(proteaseName, out var sc) ? sc : 0;

                // Get coverage values
                string totalCoverage = "N/A";
                string uniqueCoverage = "N/A";

                if (_analyzer.SequenceCoverageByProtease.TryGetValue(proteaseName, out var proteaseCoverage) &&
                    proteaseCoverage.TryGetValue(SelectedProtein.Protein, out var coverageValues))
                {
                    totalCoverage = $"{Math.Round(coverageValues.Item1, 2)}%";

                    uniqueCoverage = _analyzer.IsMultiDatabase
                        ? uniqueCoverageByProtease!.TryGetValue(proteaseName, out var fraction)
                            ? $"{Math.Round(fraction * 100, 2)}%"
                            : "N/A"
                        : $"{Math.Round(coverageValues.Item2, 2)}%";
                }

                summaryRows.Add(new ProteinDigestionSummaryRow
                {
                    Protease = proteaseName,
                    UniquePeptides = uniqueCount,
                    SharedPeptides = sharedCount,
                    TotalCoverage = totalCoverage,
                    UniqueCoverage = uniqueCoverage
                });
            }

            // Bind to the DataGrid
            proteinSummaryGrid.ItemsSource = summaryRows;
        }

        #endregion

        #region Sequence Coverage Map Drawing

        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            if (_displayMode == CoverageMapDisplayMode.ProteaseLane)
                DrawProteaseLaneCoverageMap(protein, proteases);
            else
                DrawPeptidePerBarCoverageMap(protein, proteases);
        }

        private void DrawProteaseLaneCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            // Collect interval data from analyzer
            var intervalsByProtease = new Dictionary<string, List<(int Start, int End)>>();
            foreach (var proteaseName in proteases)
            {
                var peps = _analyzer.GetPeptidesForProteinAndProtease(protein.Protein, proteaseName)
                    .Distinct()
                    .OrderBy(p => p.StartResidue)
                    .Select(p => (Start: p.StartResidue, End: p.EndResidue))
                    .ToList();
                intervalsByProtease[proteaseName] = peps;
            }

            const double sequenceContentWidth = 25 * 25 + 45 + 20;
            double availableWidth = ResultsGrid.ActualWidth > 0 ? ResultsGrid.ActualWidth - 20 : sequenceContentWidth;

            SequenceCoverageMap.DrawLaneViewMap(
                map, legend, legendGrid,
                protein.Protein.Accession,
                null,
                protein.Protein.BaseSequence,
                proteases,
                intervalsByProtease,
                name => SequenceCoverageMap.GetProteaseBrush(_stableProteaseBrushes, name),
                Math.Min(availableWidth, sequenceContentWidth));
        }

        private void DrawPeptidePerBarCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            // Collect peptides to draw
            var peptidesToDraw = new List<InSilicoPep>();
            foreach (var protease in proteases)
                peptidesToDraw.AddRange(_analyzer.GetPeptidesForProteinAndProtease(protein.Protein, protease));
            peptidesToDraw = peptidesToDraw.Distinct().ToList();

            var allPeptidesForProtein = _analyzer.GetAllPeptidesForProtein(protein.Protein);
            var (uniqueCovered, sharedOnlyCovered) = SequenceCoverageMap.CalculateCoveredResiduesByType(
                allPeptidesForProtein, _analyzer.IsMultiDatabase);

            const double sequenceContentWidth = 25 * 25 + 45 + 20;
            double availableWidth = ResultsGrid.ActualWidth > 0 ? ResultsGrid.ActualWidth - 20 : sequenceContentWidth;
            double canvasWidth = Math.Min(availableWidth, sequenceContentWidth);

            SequenceCoverageMap.DrawPeptidePerBarMap(
                map, legend, legendGrid,
                protein.Protein.Accession,
                protein.Protein.BaseSequence,
                proteases,
                ProteaseByColor,
                peptidesToDraw,
                uniqueCovered,
                sharedOnlyCovered,
                _analyzer.IsMultiDatabase,
                canvasWidth);
        }

        #endregion

        #region Event Handlers

        private void CoverageViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _displayMode = _displayMode == CoverageMapDisplayMode.PeptidePerBar
                ? CoverageMapDisplayMode.ProteaseLane
                : CoverageMapDisplayMode.PeptidePerBar;

            UpdateToggleButtonStyle();

            if (SelectedProtein != null)
            {
                var proteasesToDraw = SelectedProteases.Count > 0 ? SelectedProteases : _analyzer.Proteases;
                DrawSequenceCoverageMap(SelectedProtein, proteasesToDraw);
            }
        }

        private void UpdateToggleButtonStyle()
        {
            if (coverageViewToggleButton == null) return;

            if (_displayMode == CoverageMapDisplayMode.ProteaseLane)
            {
                coverageViewToggleButton.Content = "Lane View";
            }
            else
            {
                coverageViewToggleButton.Content = $"{GlobalVariables.AnalyteType.GetUniqueFormLabel()} View";
            }
        }

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e) => OnSelectionChanged();

        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnSelectionChanged();

        private void resultsSizeChanged(object sender, SizeChangedEventArgs e) => ChangeMapScrollViewSize();

        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = .8 * ResultsGrid.ActualHeight;
            mapViewer.Width = ResultsGrid.ActualWidth;
        }

        private void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;

            // Auto-select the first protein so the coverage map is populated on load
            if (dataGridProteins.Items.Count > 0)
            {
                dataGridProteins.SelectedIndex = 0;
                dataGridProteins.ScrollIntoView(dataGridProteins.Items[0]);
            }

            UpdateToggleButtonStyle();
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
                NotificationService.Instance.AddNotification($"Warning: Protein accession contains invalid characters. Using '{proteinName}' instead.", NotificationType.Warning);
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
            var results = new List<string>
            {
                $"Digestion Results for {proteinName}",
                "",
                "Protease\tUnique Peptides\tShared Peptides\tTotal Peptides\tTotal Coverage\tUnique Coverage"
            };

            // Get data from the summary grid - use Items.OfType for robustness
            foreach (var row in proteinSummaryGrid.Items.OfType<ProteinDigestionSummaryRow>())
            {
                results.Add($"{row.Protease}\t{row.UniquePeptides}\t{row.SharedPeptides}\t{row.TotalPeptides}\t{row.TotalCoverage}\t{row.UniqueCoverage}");
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

            // Offer to copy paths - use notification instead of interactive dialog
            NotificationService.Instance.AddNotification($"Files created at {subFolder}!", NotificationType.Success);
            var clipboardText = $"Coverage Map: {filePath}\r\nResults: {Path.Combine(subFolder, resultsFile)}";
            Clipboard.SetText(clipboardText);
            NotificationService.Instance.AddNotification("File paths copied to clipboard.", NotificationType.Information);
        }

        private void SaveMetadata(string subFolder, string proteinName, IBioPolymer protein, List<InSilicoPep> allPeptides)
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

        #region Spectral Library Export

        /// <summary>
        /// Opens the spectral library export options dialog
        /// </summary>
        private async void ExportSpectralLibrary(object sender, RoutedEventArgs e)
        {
            // Gather available proteases and proteins from the analyzer
            var availableProteases = _analyzer.Proteases.ToList();
            var availableProteins = _analyzer.ProteinAccessions.ToList();

            // Get current selections to pre-populate the dialog
            List<string>? currentProteases = SelectedProteases.Any() ? SelectedProteases : null;
            string? currentProtein = SelectedProtein?.Protein.Accession;

            // Show the options dialog with available data
            var optionsWindow = new SpectralLibraryOptionsWindow(
                availableProteases,
                availableProteins,
                currentProteases,
                currentProtein
            );

            optionsWindow.Owner = Window.GetWindow(this);
            optionsWindow.ShowDialog();

            if (optionsWindow.DialogResultOk)
            {
                await ExecuteSpectralLibraryExportAsync(optionsWindow.ExportOptions);
            }
        }

        /// <summary>
        /// Executes the spectral library export based on user options
        /// </summary>
        private async Task ExecuteSpectralLibraryExportAsync(SpectralLibraryExportOptions options)
        {
            NotificationService.Instance.AddNotification("Starting spectral library export...", NotificationType.Information);
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = GetFileFilterForSpectralLibrary(options.OutputFormat),
                    DefaultExt = GetDefaultExtensionForSpectralLibrary(options.OutputFormat),
                    FileName = $"SpectralLibrary_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                var peptidesToExport = GetPeptidesForSpectralLibraryExport(options);

                if (!peptidesToExport.Any())
                {
                    NotificationService.Instance.AddNotification("No peptides found for the selected proteases and proteins. Export cancelled.", NotificationType.Error);
                    return;
                }

                NotificationService.Instance.AddNotification($"Generating spectral library for {peptidesToExport.Count} peptides. This may take several minutes...", NotificationType.Information);

                var generator = new SpectralLibraryGenerator(
                    peptidesToExport,
                    options,
                    saveDialog.FileName);

                Mouse.OverrideCursor = Cursors.Wait;
                var result = await Task.Run(() => generator.GenerateLibrary());
                Mouse.OverrideCursor = null;

                NotificationService.Instance.AddNotification($"Spectral library generated with {result.Count} spectra. File saved to: {saveDialog.FileName}", NotificationType.Success);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                NotificationService.Instance.AddNotification($"Error generating spectral library: {ex.Message}\n\n{ex.StackTrace}", NotificationType.Error);
            }
        }

        /// <summary>
        /// Gathers peptides for export based on user selections
        /// </summary>
        private List<InSilicoPep> GetPeptidesForSpectralLibraryExport(SpectralLibraryExportOptions options)
        {
            var peptides = new HashSet<InSilicoPep>();

            // Get Protein objects from selected protein accessions
            var selectedProteins = _analyzer.ProteinCoverageResults.Keys
                .Where(p => options.SelectedProteins.Contains(p.Accession))
                .ToList();

            // Gather peptides for each protein-protease combination
            foreach (var protein in selectedProteins)
            {
                foreach (var proteaseName in options.SelectedProteases)
                {
                    var proteinPeptides = _analyzer.GetPeptidesForProteinAndProtease(protein, proteaseName);
                    peptides.UnionWith(proteinPeptides);
                }
            }
            peptides = options.ExcludeUndetectablePeptides ? peptides.Where(p => p.PflyDetectability == true).ToHashSet()
                : peptides;
            return peptides.DistinctBy(p => p.FullSequence).ToList();
        }

        private string GetFileFilterForSpectralLibrary(string format)
        {
            return format switch
            {
                "SpectraST" => "SpectraST Files (*.sptxt)|*.sptxt",
                "BiblioSpec" => "BiblioSpec Files (*.blib)|*.blib",
                "MSP" => "MSP Files (*.msp)|*.msp",
                "NIST" => "NIST MSP Files (*.msp)|*.msp",
                _ => "All Files (*.*)|*.*"
            };
        }

        private string GetDefaultExtensionForSpectralLibrary(string format)
        {
            return format switch
            {
                "SpectraST" => ".sptxt",
                "BiblioSpec" => ".blib",
                _ => ".msp"
            };
        }

        #endregion
    }
}
