using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuiFunctions;
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
        private Dictionary<string, SolidColorBrush> ModsByColor;
        private List<string> SelectedProteases;
        private ProteinForTreeView SelectedProtein;
        private readonly RunParameters UserParams;
        private DigestionConditionsSetupViewModel _allProteaseVm;
        private readonly SeekMaximumCoverage _seeker = new SeekMaximumCoverage();

        private string? _fastaPath;
        private CancellationTokenSource? _exportCts;

        private readonly Dictionary<string, Color> _stableProteaseColors;
        private readonly Dictionary<string, SolidColorBrush> _stableProteaseBrushes;

        // ── Display mode toggle ──────────────────────────────────────────────
        private CoverageMapDisplayMode _displayMode = CoverageMapDisplayMode.ProteaseLane;
        private CoverageMapDisplayMode _lastCoverageMode = CoverageMapDisplayMode.ProteaseLane;

        #endregion

        #region Constructors

        public IndividualProteinAnalyzerWindow() { }

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
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            (_stableProteaseColors, _stableProteaseBrushes) = SequenceCoverageMap.BuildStableColorMaps();

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
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            (_stableProteaseColors, _stableProteaseBrushes) = SequenceCoverageMap.BuildStableColorMaps();

            SetUpProteinsForTreeView();
            PopulateProteinList();

            WireProteasePanel();
            this.Loaded += results_Loaded;
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Stable Color Ordering

        private static int GetStableColorIndex(string proteaseName)
        {
            int i = 0;
            foreach (var key in ProteaseDictionary.Dictionary.Keys)
            {
                if (key == proteaseName) return i;
                i++;
            }
            return int.MaxValue;
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
            else if (tripletToggle.IsChecked == true)
                result = _seeker.BestTriplet(coverageDict);
            else if (allToggle.IsChecked == true)
            {
                // All: show every checked protease — union of all covered residues
                var allCovered = new HashSet<int>();
                foreach (var kvp in coverageDict)
                    allCovered.UnionWith(kvp.Value);
                result = new SeekMaximumCoverage.CombinationResult(
                    checkedProteases.Select(vm => vm.DigestionAgentName).ToList(),
                    allCovered, allCovered.Count,
                    SeekMaximumCoverage.CoverageFraction(allCovered, SelectedProtein.Protein.Length));
            }
            else
                result = _seeker.BestTriplet(coverageDict);

            var winningParams = proteaseParams
                .Where(p => result.Proteases.Contains(p.DigestionAgentName))
                .ToList();
            var pepsByProtease = BuildPeptidesByProtease(SelectedProtein.Protein, winningParams);

            var orderedChecked = result.Proteases
                .OrderBy(GetStableColorIndex)
                .ToList();

            DrawMaxCoverageMap(SelectedProtein.Protein, result, pepsByProtease, orderedChecked);
        }

        private Dictionary<string, List<(int Start, int End)>> BuildPeptidesByProtease(
            Protein protein,
            IEnumerable<ProteaseSpecificParameters> allParams)
        {
            return _seeker.GetDetectablePeptideIntervals(protein, allParams);
        }

        #endregion

        #region Max Coverage Map Drawing

        private void DrawMaxCoverageMap(
            Protein protein,
            SeekMaximumCoverage.CombinationResult result,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases)
        {
            if (_displayMode == CoverageMapDisplayMode.ProteaseLane)
                DrawLaneViewCoverageMap(protein, result, pepsByProtease, orderedCheckedProteases);
            else
                DrawPeptideViewCoverageMap(protein, result, pepsByProtease, orderedCheckedProteases);
        }

        private void DrawLaneViewCoverageMap(
            Protein protein,
            SeekMaximumCoverage.CombinationResult result,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases)
        {
            const double sequenceContentWidth = 25 * 22 + 65 + 20;
            double availableWidth = MaxCoverageGrid.ActualWidth - 18;

            SequenceCoverageMap.DrawLaneViewMap(
                maxCoverageMap, maxCoverageLegend, maxCoverageLegendGrid,
                protein.Accession,
                protein.FullName,
                protein.BaseSequence,
                orderedCheckedProteases,
                pepsByProtease,
                name => SequenceCoverageMap.GetProteaseBrush(_stableProteaseBrushes, name),
                Math.Min(availableWidth, sequenceContentWidth),
                residueSpacing: 22,
                seqLeftOffset: 65);
        }

        private void DrawPeptideViewCoverageMap(
            Protein protein,
            SeekMaximumCoverage.CombinationResult result,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases)
        {
            // Calculate covered residues for character styling
            var allCovered = new HashSet<int>();
            foreach (var kvp in pepsByProtease)
                foreach (var (start, end) in kvp.Value)
                    for (int i = start; i <= end; i++)
                        allCovered.Add(i);

            // Build combined interval list with protease names
            var allIntervals = new List<(int Start, int End, string Protease)>();
            foreach (var kvp in pepsByProtease)
                foreach (var (start, end) in kvp.Value)
                    allIntervals.Add((start, end, kvp.Key));

            const double sequenceContentWidth = 25 * 22 + 65 + 20;
            double availableWidth = MaxCoverageGrid.ActualWidth > 0 ? MaxCoverageGrid.ActualWidth - 18 : sequenceContentWidth;
            double canvasWidth = Math.Max(Math.Min(availableWidth, sequenceContentWidth), 200);

            maxCoverageMap.Children.Clear();
            maxCoverageLegendGrid.Children.Clear();
            maxCoverageLegend.Children.Clear();

            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(
                protein.BaseSequence, CoverageMapDataPreparer.DefaultResiduesPerLine);

            int height = 10;

            string proteinName = protein.FullName ?? protein.Accession;
            SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(0, height),
                protein.Accession, Brushes.Black);
            height += 20;
            SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(0, height),
                proteinName, Brushes.Black);
            height += 30;

            string pct = SeekMaximumCoverage.CoveragePercentage(result.CoveredResidues, protein.Length);
            string proteaseStr = result.Proteases.Count > 0
                ? string.Join(" + ", result.Proteases)
                : "none";
            SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(0, height),
                $"Best coverage: {proteaseStr}  ({pct})", Brushes.Black);
            height += 30;

            maxCoverageMap.Width = canvasWidth;

            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                int lineStartRes = lineIndex * CoverageMapDataPreparer.DefaultResiduesPerLine + 1;

                SequenceCoverageMap.txtDrawingLabel(maxCoverageMap, new Point(0, height),
                    lineStartRes.ToString(), Brushes.Black);

                for (int r = 0; r < line.Length; r++)
                {
                    int residuePos = lineStartRes + r;
                    string ch = line[r].ToString().ToUpper();

                    if (allCovered.Contains(residuePos))
                        SequenceCoverageMap.txtDrawing(maxCoverageMap, new Point(r * 22 + 65, height), ch, Brushes.Black);
                    else
                        SequenceCoverageMap.txtDrawingUncovered(maxCoverageMap, new Point(r * 22 + 65, height), ch, Brushes.Black);
                }

                var indices = new Dictionary<int, List<int>>();
                var intervalsOnLine = allIntervals
                    .Where(i => i.Start <= lineStartRes + line.Length - 1 && i.End >= lineStartRes)
                    .OrderBy(i => i.Start)
                    .ToList();

                foreach (var (pepStart, pepEnd, proteaseName) in intervalsOnLine)
                {
                    int start = Math.Max(pepStart, lineStartRes) - lineStartRes;
                    int end = Math.Min(pepEnd, lineStartRes + line.Length - 1) - lineStartRes;
                    var color = _stableProteaseColors.TryGetValue(proteaseName, out var c) ? c : Colors.DimGray;

                    SequenceCoverageMap.Highlight(start, end, maxCoverageMap, indices, height, color, true, true, true);
                }

                int addedSpace = indices.Count > 7 ? (indices.Count - 7) * 10 : 0;
                height += 100 + addedSpace;
            }

            maxCoverageMap.Height = height + 20;

            SequenceCoverageMap.drawLegend(maxCoverageLegend, _stableProteaseColors, orderedCheckedProteases, maxCoverageLegendGrid, false);
        }

        #endregion

        #region Event Handlers

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e)
            => OnSelectionChanged();

        private void MaxCoverageMode_Changed(object sender, RoutedEventArgs e)
            => RefreshMaxCoverage();

        private void maxCoverageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double availableWidth = MaxCoverageGrid.ActualWidth;
            maxCoverageMapViewer.Height = 0.85 * MaxCoverageGrid.ActualHeight;
            maxCoverageMapViewer.Width = availableWidth;

            const double sequenceContentWidth = 25 * 22 + 65 + 20;
            double canvasWidth = Math.Min(availableWidth - 18, sequenceContentWidth);
            canvasWidth = Math.Max(canvasWidth, 200);
            maxCoverageMap.Width = canvasWidth;
            maxCoverageLegend.Width = canvasWidth;
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

            if (NceComboBox.SelectedItem is not ComboBoxItem nceItem ||
                !int.TryParse(nceItem.Content?.ToString(), out int nce))
            {
                ExportStatusLabel.Text = "Select a collision energy value.";
                return;
            }

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

        private void CoverageViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _displayMode = _displayMode == CoverageMapDisplayMode.PeptidePerBar
                ? CoverageMapDisplayMode.ProteaseLane
                : CoverageMapDisplayMode.PeptidePerBar;

            _lastCoverageMode = _displayMode;
            UpdateToggleButtonStyle();
            RefreshMaxCoverage();
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
                coverageViewToggleButton.Content = "Peptide View";
            }
        }

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;

            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                vm.PropertyChanged -= OnProteaseParameterChanged;

            var trypsin = _allProteaseVm.ProteaseSpecificParameters
                .FirstOrDefault(vm => vm.DigestionAgentName == "trypsin|P");
            if (trypsin != null)
                trypsin.IsSelected = true;

            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                vm.PropertyChanged += OnProteaseParameterChanged;

            if (dataGridProteins.Items.Count > 0)
                dataGridProteins.SelectedIndex = 0;

            UpdateToggleButtonStyle();
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
