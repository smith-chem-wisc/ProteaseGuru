using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuiFunctions;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;
using Tasks.CoverageMapConfiguration;

namespace GUI
{
    public partial class IndividualProteinAnalyzerWindow : UserControl
    {
        #region Private Fields

        private readonly ProteinCoverageAnalyzer _analyzer;
        private ObservableCollection<string> proteinList;
        private ObservableCollection<string> filteredList;
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;
        private Dictionary<string, Color> ProteaseByColor;
        private Dictionary<string, SolidColorBrush> ModsByColor;
        private List<string> SelectedProteases;
        private ProteinForTreeView SelectedProtein;
        private readonly RunParameters UserParams;
        private DigestionConditionsSetupViewModel _allProteaseVm;
        private readonly SeekMaximumCoverage _seeker = new SeekMaximumCoverage();

        // Track the FASTA path so the library is written alongside it
        private string? _fastaPath;

        // Cancellation for in-progress exports
        private CancellationTokenSource? _exportCts;

        #endregion

        #region Constructors

        public IndividualProteinAnalyzerWindow() { }

        /// <summary>
        /// Lightweight constructor — proteins come straight from the database,
        /// no prior Run required. All digestion is on-demand.
        /// </summary>
        public IndividualProteinAnalyzerWindow(List<Protein> proteins, string? fastaPath = null)
        {
            InitializeComponent();

            _fastaPath = fastaPath;

            var emptyPeptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();
            var emptySeqCov = new Dictionary<string, Dictionary<Protein, (double, double)>>();
            _analyzer = new ProteinCoverageAnalyzer(emptyPeptideByFile, emptySeqCov);

            SelectedProteases = new List<string>();
            SelectedProtein = null;
            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();
            ProteaseByColor = new Dictionary<string, Color>();
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            foreach (var protein in proteins)
            {
                var ptv = new ProteinForTreeView(protein, protein.Accession,
                    new List<InSilicoPep>(), new List<InSilicoPep>(), new List<InSilicoPep>());
                ProteinsForTreeView[protein] = ptv;
                proteinList.Add(protein.Accession);
                dataGridProteins.Items.Add(protein.Accession);
            }
            dataGridProteins.DataContext = proteinList;

            WireProteasePanel();
            this.Loaded += results_Loaded;
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        /// <summary>
        /// Full constructor used when results come from a completed Run.
        /// </summary>
        public IndividualProteinAnalyzerWindow(
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile,
            RunParameters userParams,
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease,
            string? fastaPath = null)
        {
            InitializeComponent();

            _fastaPath = fastaPath;
            _analyzer = new ProteinCoverageAnalyzer(peptideByFile, sequenceCoverageByProtease);
            UserParams = userParams;
            SelectedProteases = new List<string>();
            SelectedProtein = null;
            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();

            SetUpProteinsForTreeView();
            PopulateProteinList();

            var rgbColorMap = CoverageMapConfiguration.CreateProteaseColorMap(_analyzer.Proteases);
            ProteaseByColor = rgbColorMap.ToDictionary(kvp => kvp.Key, kvp => ToWpfColor(kvp.Value));
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            WireProteasePanel();
            this.Loaded += results_Loaded;
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Initialization

        private void WireProteasePanel()
        {
            _allProteaseVm = new DigestionConditionsSetupViewModel(null);
            ProteaseOptionsItemsControl.ItemsSource = _allProteaseVm.ProteaseSpecificParameters;
            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                vm.PropertyChanged += OnProteaseParameterChanged;
        }

        private void SetUpProteinsForTreeView()
        {
            foreach (var kvp in _analyzer.ProteinCoverageResults)
            {
                var protein = kvp.Key;
                var result = kvp.Value;
                var ptv = new ProteinForTreeView(protein, result.DisplayName,
                    result.AllPeptides, result.UniquePeptides, result.SharedPeptides);
                ProteinsForTreeView[protein] = ptv;
            }
        }

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

        #region Color Helpers

        private static Color ToWpfColor(RgbColor rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);
        private static SolidColorBrush ToWpfBrush(RgbColor rgb) => new SolidColorBrush(ToWpfColor(rgb));

        #endregion

        #region Search

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
            => SearchModifications.SetTimer();

        private void searchBox_TextChangedHandler(object sender, EventArgs e)
        {
            string userInput = SearchTextBox.Text;
            if (string.IsNullOrEmpty(userInput))
            {
                dataGridProteins.DataContext = proteinList;
                return;
            }

            filteredList.Clear();
            foreach (var protein in proteinList)
                if (protein.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    filteredList.Add(protein);

            dataGridProteins.Items.Clear();
            foreach (var entry in filteredList)
                dataGridProteins.Items.Add(entry);

            SearchModifications.Timer.Stop();
        }

        #endregion

        #region Protein Selection

        private void OnSelectionChanged()
        {
            if (dataGridProteins.SelectedItem == null)
            {
                SelectedProtein = null;
                maxCoverageMap.Children.Clear();
                maxCoverageLegend.Children.Clear();
                maxCoverageLegendGrid.Children.Clear();
                return;
            }

            string accession = dataGridProteins.SelectedItem.ToString();
            var match = ProteinsForTreeView.FirstOrDefault(p => p.Key.Accession == accession);
            if (match.Value == null) return;

            SelectedProtein = match.Value;
            RefreshMaxCoverage();
        }

        #endregion

        #region Reactive Digestion

        private void OnProteaseParameterChanged(object sender, PropertyChangedEventArgs e)
            => RefreshMaxCoverage();

        private void RefreshMaxCoverage()
        {
            if (SelectedProtein == null) return;

            var checkedProteases = _allProteaseVm.ProteaseSpecificParameters
                .Where(vm => vm.IsSelected)
                .ToList();

            maxCoverageMap.Children.Clear();
            maxCoverageLegend.Children.Clear();
            maxCoverageLegendGrid.Children.Clear();

            if (checkedProteases.Count == 0) return;

            // Rebuild color map for currently checked proteases
            var proteaseNames = checkedProteases.Select(vm => vm.DigestionAgentName).ToList();
            var rgbColorMap = CoverageMapConfiguration.CreateProteaseColorMap(proteaseNames);
            ProteaseByColor = rgbColorMap.ToDictionary(kvp => kvp.Key, kvp => ToWpfColor(kvp.Value));
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            var proteaseParams = checkedProteases.Select(vm => vm.ProteaseSpecificParams).ToList();
            var coverageDict = _seeker.CalculateCoverageByProtease(SelectedProtein.Protein, proteaseParams);

            SeekMaximumCoverage.CombinationResult result;
            if (greedyToggle.IsChecked == true)
            {
                var g = _seeker.GreedyMinimumProteaseSet(coverageDict);
                result = new SeekMaximumCoverage.CombinationResult(
                    g.SelectedProteases, g.CoveredResidues,
                    g.CoveredResidues.Count, g.CoverageFraction);
            }
            else if (singleToggle.IsChecked == true)
                result = _seeker.BestSingle(coverageDict);
            else if (pairToggle.IsChecked == true)
                result = _seeker.BestPair(coverageDict);
            else
                result = _seeker.BestTriplet(coverageDict);

            DrawMaxCoverageMap(SelectedProtein.Protein, result);
        }

        #endregion

        #region Max Coverage Map Drawing

        private void DrawMaxCoverageMap(Protein protein, SeekMaximumCoverage.CombinationResult result)
        {
            const int residuesPerLine = CoverageMapDataPreparer.DefaultResiduesPerLine;
            int height = 10;

            maxCoverageMap.Width = 0.90 * MaxCoverageGrid.ActualWidth;
            maxCoverageMap.Children.Clear();

            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(
                protein.BaseSequence, residuesPerLine);

            // Accession and protein name at the top of the white box
            string proteinName = protein.FullName ?? protein.Accession;
            SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(0, height),
                protein.Accession, Brushes.Black);
            height += 20;
            SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(0, height),
                proteinName, Brushes.Black);
            height += 30;

            // "Best coverage: trypsin + Asp-N  (87.3%)"
            string pct = SeekMaximumCoverage.CoveragePercentage(result.CoveredResidues, protein.Length);
            string proteaseList = result.Proteases.Count > 0
                ? string.Join(" + ", result.Proteases)
                : "none";
            SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(0, height),
                $"Best coverage: {proteaseList}  ({pct})", Brushes.Black);
            height += 30;

            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                int lineStartResidue = lineIndex * residuesPerLine + 1; // 1-based

                SequenceCoverageMap.txtDrawingLabel(
                    maxCoverageMap, new Point(0, height), lineStartResidue.ToString(), Brushes.Black);

                for (int r = 0; r < line.Length; r++)
                {
                    // CoveredResidues from SeekMaximumCoverage is 0-based
                    bool covered = result.CoveredResidues.Contains(lineStartResidue + r - 1);
                    string ch = line[r].ToString().ToUpper();
                    var pt = new Point(r * residuesPerLine + 65, height);

                    if (covered)
                        SequenceCoverageMap.txtDrawing(maxCoverageMap, pt, ch, Brushes.Black);
                    else
                        SequenceCoverageMap.txtDrawingUncovered(maxCoverageMap, pt, ch, Brushes.Black);
                }

                height += 100;
            }

            maxCoverageMap.Height = height + 20;

            // Draw legend showing only the winning proteases
            DrawMaxCoverageLegend(result.Proteases);
        }

        private void DrawMaxCoverageLegend(List<string> winningProteases)
        {
            maxCoverageLegend.Children.Clear();
            maxCoverageLegendGrid.Children.Clear();

            if (winningProteases.Count == 0) return;

            bool hasVariants = SelectedProtein?.Protein?.AppliedSequenceVariations?.Count > 0;

            // Only pass colors for the winning proteases
            var legendColors = ProteaseByColor
                .Where(kvp => winningProteases.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            SequenceCoverageMap.drawLegend(
                maxCoverageLegend, legendColors, winningProteases, maxCoverageLegendGrid, hasVariants);
        }

        #endregion

        #region Event Handlers

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e)
            => OnSelectionChanged();

        private void MaxCoverageMode_Changed(object sender, RoutedEventArgs e)
            => RefreshMaxCoverage();

        private void maxCoverageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            maxCoverageMapViewer.Height = 0.85 * MaxCoverageGrid.ActualHeight;
            maxCoverageMapViewer.Width = 0.99 * MaxCoverageGrid.ActualWidth;
        }

        private async void ExportSpectrumLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProtein == null)
            {
                ExportStatusLabel.Text = "Select a protein first.";
                return;
            }

            var checkedProteases = _allProteaseVm.ProteaseSpecificParameters
                .Where(vm => vm.IsSelected)
                .ToList();

            if (checkedProteases.Count == 0)
            {
                ExportStatusLabel.Text = "Select at least one protease first.";
                return;
            }

            // ── Read NCE from ComboBox ───────────────────────────────────────
            if (NceComboBox.SelectedItem is not ComboBoxItem nceItem ||
                !int.TryParse(nceItem.Content?.ToString(), out int nce))
            {
                ExportStatusLabel.Text = "Select a collision energy value.";
                return;
            }

            // ── Read charge states ───────────────────────────────────────────
            var chargeStates = new List<int>();
            if (chk1.IsChecked == true) chargeStates.Add(1);
            if (chk2.IsChecked == true) chargeStates.Add(2);
            if (chk3.IsChecked == true) chargeStates.Add(3);
            if (chk4.IsChecked == true) chargeStates.Add(4);
            if (chk5.IsChecked == true) chargeStates.Add(5);
            if (chk6.IsChecked == true) chargeStates.Add(6);
            if (chk7.IsChecked == true) chargeStates.Add(7);

            if (chargeStates.Count == 0)
            {
                ExportStatusLabel.Text = "Select at least one charge state.";
                return;
            }

            // ── Disable button during export ─────────────────────────────────
            ExportSpectrumLibraryButton.IsEnabled = false;
            _exportCts?.Cancel();
            _exportCts = new CancellationTokenSource();
            var ct = _exportCts.Token;

            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => ExportStatusLabel.Text = msg));

            try
            {
                var proteaseParams = checkedProteases.Select(vm => vm.ProteaseSpecificParams).ToList();

                string outputPath = await SpectrumLibraryExporter.ExportAsync(
                    protein: SelectedProtein.Protein,
                    proteaseParams: proteaseParams,
                    chargeStates: chargeStates,
                    nce: nce,
                    fastaPath: _fastaPath,
                    progress: progress,
                    cancellationToken: ct);

                ExportStatusLabel.Text = $"✓ Library saved: {System.IO.Path.GetFileName(outputPath)}";
            }
            catch (OperationCanceledException)
            {
                ExportStatusLabel.Text = "Export cancelled.";
            }
            catch (Exception ex)
            {
                ExportStatusLabel.Text = $"Export failed: {ex.Message}";
            }
            finally
            {
                ExportSpectrumLibraryButton.IsEnabled = true;
            }
        }

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;
        }

        void window_Closing(object sender, global::System.ComponentModel.CancelEventArgs e)
        {
            SearchModifications.Timer.Tick -= searchBox_TextChangedHandler;
            if (_allProteaseVm != null)
                foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                    vm.PropertyChanged -= OnProteaseParameterChanged;
        }

        #endregion
    }
}
