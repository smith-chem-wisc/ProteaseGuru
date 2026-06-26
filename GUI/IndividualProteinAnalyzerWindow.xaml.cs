using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Engine;
using GuiFunctions;
using Omics;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;
using Tasks.CoverageMapConfiguration;
using Transcriptomics.Digestion;

namespace GUI
{
    public partial class IndividualProteinAnalyzerWindow : UserControl
    {
        #region Private Fields

        private readonly ProteinCoverageAnalyzer _analyzer;
        private ObservableCollection<string> proteinList;
        private ObservableCollection<string> filteredList;
        private Dictionary<IBioPolymer, ProteinForTreeView> ProteinsForTreeView;
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

        // ── Per-protease digest cache ────────────────────────────────────────
        // Caches (coverage, intervals) for each protease keyed on a value snapshot of its
        // digestion settings, scoped to the selected protein. Algorithm- and view-toggle
        // refreshes reuse this instead of re-digesting; only proteases whose settings
        // actually changed are recomputed.
        private readonly record struct DigestCacheKey(
            string Agent, int MinLength, int MaxLength, int MaxMissedCleavages, string ModsSignature);

        private readonly Dictionary<DigestCacheKey, (HashSet<int> Coverage, List<(int Start, int End)> Intervals)> _digestCache = new();
        private IBioPolymer? _cacheProtein;

        // Coalesces bursts of refresh triggers (checkbox/param spam, mode/view toggles,
        // selection changes) into a single recompute.
        private DispatcherTimer? _refreshTimer;

        #endregion

        #region Constructors

        public IndividualProteinAnalyzerWindow() { }

        public IndividualProteinAnalyzerWindow(List<IBioPolymer> proteins, string? fastaPath = null)
        {
            InitializeComponent();
            _fastaPath = fastaPath;

            var emptyPeptideByFile = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>();
            var emptySeqCov = new Dictionary<string, Dictionary<IBioPolymer, (double, double)>>();
            _analyzer = new ProteinCoverageAnalyzer(emptyPeptideByFile, emptySeqCov);

            SelectedProteases = new List<string>();
            SelectedProtein = null;
            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<IBioPolymer, ProteinForTreeView>();
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
            Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile,
            RunParameters userParams,
            Dictionary<string, Dictionary<IBioPolymer, (double, double)>> sequenceCoverageByProtease,
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
            ProteinsForTreeView = new Dictionary<IBioPolymer, ProteinForTreeView>();
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
            foreach (var key in RnaseDictionary.Dictionary.Keys)
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

            // When mode switches, deselect all wrong-mode entries and refresh the map
            GuiGlobalParamsViewModel.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(GuiGlobalParamsViewModel.IsRnaMode)) return;
                foreach (var vm in _allProteaseVm.ProteaseSpecificParameters.Where(p => !p.IsVisible))
                    vm.IsSelected = false;
                ScheduleRefresh();
            };
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
            ScheduleRefresh();
        }

        #endregion

        #region Reactive Digestion

        private void OnProteaseParameterChanged(object sender, PropertyChangedEventArgs e)
            => ScheduleRefresh();

        private void RefreshMaxCoverage()
        {
            if (SelectedProtein == null) return;

            var checkedProteases = _allProteaseVm.ProteaseSpecificParameters
                .Where(vm => vm.IsSelected && vm.IsVisible)
                .ToList();

            maxCoverageMap.Children.Clear();
            maxCoverageLegend.Children.Clear();
            maxCoverageLegendGrid.Children.Clear();

            if (checkedProteases.Count == 0) return;

            var proteaseParams = checkedProteases.Select(vm => vm.ProteaseSpecificParams).ToList();

            // Pull coverage/intervals from the per-protease cache, digesting only the
            // proteases whose settings changed. Algorithm- and view-toggle refreshes hit
            // the cache entirely and skip digestion.
            var (coverageDict, allIntervalsDict) = GetCoverageAndIntervals(SelectedProtein.Protein, proteaseParams);

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

            // Re-use the already-computed interval dict; filter to the winning proteases only.
            var pepsByProtease = result.Proteases
                .Where(allIntervalsDict.ContainsKey)
                .ToDictionary(name => name, name => allIntervalsDict[name]);

            var orderedChecked = result.Proteases
                .OrderBy(GetStableColorIndex)
                .ToList();

            DrawMaxCoverageMap(SelectedProtein.Protein, result, pepsByProtease, orderedChecked);
        }

        /// <summary>
        /// Returns coverage sets and peptide intervals for the given proteases, serving cached
        /// results where possible and digesting only the proteases whose settings aren't cached.
        /// The cache is scoped to a single protein and reset when the selection changes.
        /// </summary>
        private (Dictionary<string, HashSet<int>> Coverage,
                 Dictionary<string, List<(int Start, int End)>> Intervals)
            GetCoverageAndIntervals(IBioPolymer protein, List<ProteaseSpecificParameters> proteaseParams)
        {
            if (!ReferenceEquals(protein, _cacheProtein))
            {
                _digestCache.Clear();
                _cacheProtein = protein;
            }

            var keys = new DigestCacheKey[proteaseParams.Count];
            for (int i = 0; i < proteaseParams.Count; i++)
                keys[i] = BuildDigestCacheKey(proteaseParams[i]);

            var misses = new List<int>();
            for (int i = 0; i < proteaseParams.Count; i++)
                if (!_digestCache.ContainsKey(keys[i]))
                    misses.Add(i);

            if (misses.Count > 0)
            {
                int degree = Math.Max(1, GlobalVariables.MaxThreads);
                var computed = new (HashSet<int> Coverage, List<(int Start, int End)> Intervals)[misses.Count];

                // DigestSingle only reads the protein and writes to its own locals, so the
                // cache-miss proteases can be digested concurrently for the same protein.
                Parallel.For(0, misses.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = degree },
                    m => computed[m] = _seeker.DigestSingle(protein, proteaseParams[misses[m]]));

                for (int m = 0; m < misses.Count; m++)
                    _digestCache[keys[misses[m]]] = computed[m];
            }

            var coverage = new Dictionary<string, HashSet<int>>(proteaseParams.Count);
            var intervals = new Dictionary<string, List<(int Start, int End)>>(proteaseParams.Count);
            for (int i = 0; i < proteaseParams.Count; i++)
            {
                var entry = _digestCache[keys[i]];
                string name = proteaseParams[i].DigestionAgentName;
                coverage[name] = entry.Coverage;
                intervals[name] = entry.Intervals;
            }
            return (coverage, intervals);
        }

        private static DigestCacheKey BuildDigestCacheKey(ProteaseSpecificParameters p)
        {
            var dp = p.DigestionParams;
            string mods = string.Join(",", p.FixedMods.Select(m => m.IdWithMotif))
                          + "|" + string.Join(",", p.VariableMods.Select(m => m.IdWithMotif));
            return new DigestCacheKey(
                dp.DigestionAgent.Name, dp.MinLength, dp.MaxLength, dp.MaxMissedCleavages, mods);
        }

        /// <summary>
        /// Restarts the debounce timer so a burst of triggers collapses into one recompute.
        /// </summary>
        private void ScheduleRefresh()
        {
            if (_refreshTimer == null)
            {
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _refreshTimer.Tick += (s, e) =>
                {
                    _refreshTimer!.Stop();
                    RefreshMaxCoverage();
                };
            }

            _refreshTimer.Stop();
            _refreshTimer.Start();
        }

        #endregion

        #region Max Coverage Map Drawing

        private void DrawMaxCoverageMap(
            IBioPolymer protein,
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
            IBioPolymer protein,
            SeekMaximumCoverage.CombinationResult result,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases)
        {
            const double sequenceContentWidth = 25 * 22 + 65 + 20;
            double availableWidth = MaxCoverageGrid.ActualWidth - 18;
            string pct = SeekMaximumCoverage.CoveragePercentage(result.CoveredResidues, protein.Length);
            string proteaseStr = result.Proteases.Count > 0
                ? string.Join(" + ", result.Proteases)
                : "none";
            string coverageHeader = $"Best coverage: {proteaseStr}  ({pct})";

            SequenceCoverageMap.DrawLaneViewMap(
                maxCoverageMap, maxCoverageLegend, maxCoverageLegendGrid,
                protein.Accession,
                protein.FullName,
                protein.BaseSequence,
                orderedCheckedProteases,
                pepsByProtease,
                name => SequenceCoverageMap.GetProteaseBrush(_stableProteaseBrushes, name),
                Math.Min(availableWidth, sequenceContentWidth),
                coverageHeader,
                residueSpacing: 22,
                seqLeftOffset: 65);
        }

        private void DrawPeptideViewCoverageMap(
            IBioPolymer protein,
            SeekMaximumCoverage.CombinationResult result,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases)
        {
            var allCovered = new HashSet<int>();
            foreach (var kvp in pepsByProtease)
                foreach (var (start, end) in kvp.Value)
                    for (int i = start; i <= end; i++)
                        allCovered.Add(i);

            var allIntervals = new List<(int Start, int End, string Protease)>();
            foreach (var kvp in pepsByProtease)
                foreach (var (start, end) in kvp.Value)
                    allIntervals.Add((start, end, kvp.Key));

            const double sequenceContentWidth = 25 * 22 + 65 + 20;
            double availableWidth = MaxCoverageGrid.ActualWidth > 0 ? MaxCoverageGrid.ActualWidth - 18 : sequenceContentWidth;
            double canvasWidth = Math.Max(Math.Min(availableWidth, sequenceContentWidth), 200);

            string pct = SeekMaximumCoverage.CoveragePercentage(result.CoveredResidues, protein.Length);
            string proteaseStr = result.Proteases.Count > 0
                ? string.Join(" + ", result.Proteases)
                : "none";
            string coverageHeader = $"Best coverage: {proteaseStr}  ({pct})";

            SequenceCoverageMap.DrawPeptidePerBarIntervalMap(
                maxCoverageMap,
                maxCoverageLegend,
                maxCoverageLegendGrid,
                protein.Accession,
                protein.BaseSequence,
                orderedCheckedProteases,
                _stableProteaseColors,
                allIntervals,
                allCovered,
                new HashSet<int>(),
                canvasWidth,
                protein.FullName,
                coverageHeader,
                residueSpacing: 22,
                seqLeftOffset: 65);
        }

        #endregion

        #region Event Handlers

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e)
            => OnSelectionChanged();

        private void MaxCoverageMode_Changed(object sender, RoutedEventArgs e)
            => ScheduleRefresh();

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
                .Where(vm => vm.IsSelected && vm.IsVisible)
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
            ScheduleRefresh();
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

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;

            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                vm.PropertyChanged -= OnProteaseParameterChanged;

            // Select a sensible default based on current mode
            string defaultAgent = GuiGlobalParamsViewModel.Instance.IsRnaMode ? "RNase T1" : "trypsin|P";
            var defaultVm = _allProteaseVm.ProteaseSpecificParameters
                .FirstOrDefault(vm => vm.DigestionAgentName == defaultAgent);
            if (defaultVm != null)
                defaultVm.IsSelected = true;

            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                vm.PropertyChanged += OnProteaseParameterChanged;

            if (dataGridProteins.Items.Count > 0)
                dataGridProteins.SelectedIndex = 0;

            UpdateToggleButtonStyle();
        }

        void window_Closing(object sender, global::System.ComponentModel.CancelEventArgs e)
        {
            _refreshTimer?.Stop();
            SearchModifications.Timer.Tick -= searchBox_TextChangedHandler;
            if (_allProteaseVm != null)
                foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                    vm.PropertyChanged -= OnProteaseParameterChanged;
        }

        #endregion
    }
}
