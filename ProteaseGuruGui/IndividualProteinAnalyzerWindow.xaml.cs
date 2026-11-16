using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ProteaseGuru.Engine;
using Omics;
using ProteaseGuru.GuiFunctions;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Tasks;
using ProteaseGuru.Tasks.CoverageMapConfiguration;
using Transcriptomics.Digestion;

namespace ProteaseGuru.Gui
{
    public partial class IndividualProteinAnalyzerWindow : UserControl
    {
        #region Private Fields

        private readonly ProteinCoverageAnalyzer _analyzer;
        private ObservableCollection<string> proteinList;
        private Dictionary<IBioPolymer, ProteinForTreeView> ProteinsForTreeView;
        private Dictionary<string, SolidColorBrush> ModsByColor;
        private ProteinForTreeView SelectedProtein;
        private readonly RunParameters UserParams;
        private DigestionConditionsSetupViewModel _allProteaseVm;
        private readonly SeekMaximumCoverage _seeker = new SeekMaximumCoverage();

        private readonly Dictionary<string, Color> _stableProteaseColors;
        private readonly Dictionary<string, SolidColorBrush> _stableProteaseBrushes;

        // ── Display mode toggle ──────────────────────────────────────────────
        private CoverageMapDisplayMode _displayMode = CoverageMapDisplayMode.ProteaseLane;
        private CoverageMapDisplayMode _lastCoverageMode = CoverageMapDisplayMode.ProteaseLane;

        private MaxCoverageMode? _maxCoverageMode;
        private int _bestN = 2;

        // Serves (coverage, intervals) per protease for the selected protein, re-digesting only
        // the proteases whose settings changed. Algorithm- and view-toggle refreshes hit it
        // entirely and skip digestion. Created on first use so every constructor gets one.
        private ProteaseDigestCache? _digestCache;
        private ProteaseDigestCache DigestCache => _digestCache ??= new ProteaseDigestCache(_seeker);

        // Coalesces bursts of refresh triggers (checkbox/param spam, mode/view toggles,
        // selection changes) into a single recompute.
        private DispatcherTimer? _refreshTimer;

        #endregion

        #region Constructors

        public IndividualProteinAnalyzerWindow() { }

        public IndividualProteinAnalyzerWindow(List<IBioPolymer> proteins)
        {
            InitializeComponent();

            var emptyPeptideByFile = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>();
            var emptySeqCov = new Dictionary<string, Dictionary<IBioPolymer, (double, double)>>();
            _analyzer = new ProteinCoverageAnalyzer(emptyPeptideByFile, emptySeqCov);

            SelectedProtein = null;
            proteinList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<IBioPolymer, ProteinForTreeView>();
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            (_stableProteaseColors, _stableProteaseBrushes) = SequenceCoverageMap.BuildStableColorMaps();

            foreach (var protein in proteins)
            {
                var ptv = new ProteinForTreeView(protein, protein.Accession,
                    new List<InSilicoPep>(), new List<InSilicoPep>(), new List<InSilicoPep>());
                ProteinsForTreeView[protein] = ptv;
                proteinList.Add(protein.Accession);
            }
            dataGridProteins.ItemsSource = proteinList;

            WireProteasePanel();
            this.Loaded += results_Loaded;
            this.Unloaded += results_Unloaded;
        }

        public IndividualProteinAnalyzerWindow(
            Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile,
            RunParameters userParams,
            Dictionary<string, Dictionary<IBioPolymer, (double, double)>> sequenceCoverageByProtease)
        {
            InitializeComponent();
            _analyzer = new ProteinCoverageAnalyzer(peptideByFile, sequenceCoverageByProtease);
            UserParams = userParams;

            SelectedProtein = null;
            proteinList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<IBioPolymer, ProteinForTreeView>();
            ModsByColor = new Dictionary<string, SolidColorBrush>();

            (_stableProteaseColors, _stableProteaseBrushes) = SequenceCoverageMap.BuildStableColorMaps();

            SetUpProteinsForTreeView();
            PopulateProteinList();

            WireProteasePanel();
            this.Loaded += results_Loaded;
            this.Unloaded += results_Unloaded;
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
        }

        private void OnGlobalParamsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GuiGlobalParamsViewModel.IsRnaMode))
                ApplyRnaMode();
        }

        private void ApplyRnaMode()
        {
            var hiddenTicked = _allProteaseVm.ProteaseSpecificParameters.Where(p => !p.IsVisible && p.IsSelected).ToList();
            foreach (var vm in hiddenTicked)
                vm.IsSelected = false;
            ApplyDigestionAgentLabels();
            if (hiddenTicked.Count > 0)
                ScheduleRefresh();
        }

        private void ApplyDigestionAgentLabels()
        {
            string agent = GlobalVariables.AnalyteType.GetDigestionAgentNoun();
            pickModeHint.Text = $"Tick {agent}s, then pick a mode to run the analysis";
            bestNTextBox.ToolTip = $"Number of {agent}s to combine. Capped at the Maximum Coverage Set size.";
            bestNToggle.ToolTip = $"Up to N ticked {agent}s that together cover the most residues";
            maxCoverageSetToggle.ToolTip = $"The fewest ticked {agent}s that reach the coverage of all of them";
            allToggle.ToolTip = $"Every ticked {agent}";
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
            }
            dataGridProteins.ItemsSource = proteinList;
        }

        #endregion

        #region Search

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = SearchTextBox.Text;
            dataGridProteins.Items.Filter = item => TextSearchMatcher.Matches(item as string, searchText);
        }

        #endregion

        #region Protein Selection

        private void OnSelectionChanged()
        {
            // Nothing is selected (filtered out or deselected); keep showing the last protein
            if (dataGridProteins.SelectedItem == null) return;

            string accession = dataGridProteins.SelectedItem.ToString();
            var match = ProteinsForTreeView.FirstOrDefault(p => p.Key.Accession == accession);
            if (match.Value == null) return;

            SelectedProtein = match.Value;
            ClearCoverageMap();
            ScheduleRefresh();
        }

        private void ClearCoverageMap()
        {
            maxCoverageMap.Children.Clear();
            maxCoverageLegend.Children.Clear();
            maxCoverageLegendGrid.Children.Clear();
        }

        #endregion

        #region Reactive Digestion

        // Any edit to the protease panel invalidates the last analysis. Clearing the mode instead of
        // rerunning it keeps ticking through proteases cheap: nothing digests until a mode is picked.
        private void OnProteaseParameterChanged(object sender, PropertyChangedEventArgs e)
            => ClearMode();

        private void ClearMode()
        {
            _maxCoverageMode = null;
            foreach (var toggle in new[] { bestNToggle, maxCoverageSetToggle, allToggle })
                toggle.IsChecked = false;
            ScheduleRefresh();
        }

        private void RefreshMaxCoverage()
        {
            if (SelectedProtein == null) return;

            var checkedProteases = _allProteaseVm.ProteaseSpecificParameters
                .Where(vm => vm.IsSelected && vm.IsVisible)
                .ToList();

            ClearCoverageMap();
            bestNCapHint.Visibility = Visibility.Collapsed;

            bool ready = _maxCoverageMode != null && checkedProteases.Count > 0;
            pickModeHint.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            if (!ready) return;

            var proteaseParams = checkedProteases.Select(vm => vm.ProteaseSpecificParams).ToList();

            // Pull coverage/intervals from the per-protease cache, digesting only the
            // proteases whose settings changed. Algorithm- and view-toggle refreshes hit
            // the cache entirely and skip digestion.
            var (coverageDict, allIntervalsDict) = DigestCache.GetCoverageAndIntervals(
                SelectedProtein.Protein, proteaseParams);

            // Coverage fractions are reported against the whole protein, so the uncovered tail of
            // a protein whose C-terminus yields no detectable peptide still counts against it.
            int totalResidues = SelectedProtein.Protein.Length;

            var result = _seeker.ForMode(_maxCoverageMode.Value, coverageDict, totalResidues, _bestN);

            int reached = result.Proteases.Count;
            if (_maxCoverageMode == MaxCoverageMode.BestN && result.ReachedMaximumCoverage)
            {
                bestNCapHint.Text = $"Maximum coverage reached with {reached} {GlobalVariables.AnalyteType.GetDigestionAgentNoun()}{(reached == 1 ? "" : "s")}";
                bestNCapHint.Visibility = Visibility.Visible;
            }

            DrawMaxCoverageMap(SelectedProtein.Protein, result, allIntervalsDict,
                maxCoverageMap, maxCoverageLegend, maxCoverageLegendGrid);
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
            Dictionary<string, List<(int Start, int End)>> allIntervals,
            Canvas map, Canvas legend, Grid legendGrid,
            string headingLabel = "Best coverage")
        {
            var pepsByProtease = result.Proteases
                .Where(allIntervals.ContainsKey)
                .ToDictionary(name => name, name => allIntervals[name]);
            var orderedCheckedProteases = result.Proteases
                .OrderBy(GetStableColorIndex)
                .ToList();

            string pct = SeekMaximumCoverage.CoveragePercentage(result.CoveredResidues, protein.Length);
            string proteaseStr = result.Proteases.Count > 0
                ? string.Join(" + ", result.Proteases)
                : "none";
            string coverageHeader = $"{headingLabel}: {proteaseStr}  ({pct})";

            if (_displayMode == CoverageMapDisplayMode.ProteaseLane)
                DrawLaneViewCoverageMap(protein, pepsByProtease, orderedCheckedProteases, coverageHeader, map, legend, legendGrid);
            else
                DrawPeptideViewCoverageMap(protein, pepsByProtease, orderedCheckedProteases, coverageHeader, map, legend, legendGrid);
        }

        private void DrawLaneViewCoverageMap(
            IBioPolymer protein,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases,
            string coverageHeader,
            Canvas map, Canvas legend, Grid legendGrid)
        {
            SequenceCoverageMap.DrawLaneViewMap(
                map, legend, legendGrid,
                protein.Accession,
                protein.FullName,
                protein.BaseSequence,
                orderedCheckedProteases,
                pepsByProtease,
                name => SequenceCoverageMap.GetProteaseBrush(_stableProteaseBrushes, name),
                coverageHeader,
                residueSpacing: 22,
                seqLeftOffset: 65);
        }

        private void DrawPeptideViewCoverageMap(
            IBioPolymer protein,
            Dictionary<string, List<(int Start, int End)>> pepsByProtease,
            List<string> orderedCheckedProteases,
            string coverageHeader,
            Canvas map, Canvas legend, Grid legendGrid)
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

            SequenceCoverageMap.DrawPeptidePerBarIntervalMap(
                map,
                legend,
                legendGrid,
                protein.Accession,
                protein.BaseSequence,
                orderedCheckedProteases,
                _stableProteaseColors,
                allIntervals,
                allCovered,
                new HashSet<int>(),
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
        {
            _maxCoverageMode = (MaxCoverageMode)((RadioButton)sender).Tag;
            ScheduleRefresh();
        }

        // Empty or partly typed text keeps the last N.
        private void BestNTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!int.TryParse(((TextBox)sender).Text, out int n) || n < 1 || n == _bestN) return;

            _bestN = n;
            if (_maxCoverageMode == MaxCoverageMode.BestN)
                ScheduleRefresh();
        }

        private void BestNTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!int.TryParse(bestNTextBox.Text, out _))
                bestNTextBox.Text = _bestN.ToString();
        }

        private void maxCoverageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // The map/legend canvases size themselves to their intrinsic content width; here we only
            // size the scroll viewport to the grid, which scrolls when the map is wider than the window.
            maxCoverageMapViewer.Height = maxCoverageMapRow.ActualHeight;
            maxCoverageMapViewer.Width = MaxCoverageGrid.ActualWidth;
        }

        private void ExportMaxCoverage_Click(object sender, RoutedEventArgs e)
        {
            var checkedProteases = _allProteaseVm.ProteaseSpecificParameters
                .Where(vm => vm.IsSelected && vm.IsVisible)
                .Select(vm => vm.ProteaseSpecificParams)
                .ToList();

            if (SelectedProtein == null || checkedProteases.Count == 0)
            {
                NotificationService.Instance.AddNotification(
                    "Select a protein and at least one protease before exporting a coverage analysis.", NotificationType.Error);
                return;
            }

            var protein = SelectedProtein.Protein;
            var dialog = new OpenFolderDialog
            {
                Title = $"Choose where to save the coverage analysis for {protein.Accession}",
                InitialDirectory = Path.GetDirectoryName(protein.DatabaseFilePath)
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string folder = MaxCoverageReport.NewExportFolder(dialog.FolderName, protein.Accession);

            // Rasterizing the maps at 600 dpi takes seconds, and minutes for proteins of several thousand residues.
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                WriteMaxCoverageExport(protein, checkedProteases, folder);
                NotificationService.Instance.AddNotification(
                    $"Exported the coverage analysis to {folder}", NotificationType.Success);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NotificationService.Instance.AddNotification(
                    $"Could not export the coverage analysis: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Writes the summary and parameter tables and one map PDF per result, in the view on screen, into
        /// <paramref name="folder"/>, creating it if needed. The folder's name is the stem of every file.
        /// </summary>
        private void WriteMaxCoverageExport(IBioPolymer protein,
            List<ProteaseSpecificParameters> checkedProteases, string folder)
        {
            var (coverage, intervals) = DigestCache.GetCoverageAndIntervals(protein, checkedProteases);
            var report = MaxCoverageReport.Compute(_seeker, protein, checkedProteases, coverage);

            string stem = Path.GetFileName(folder);
            Directory.CreateDirectory(folder);

            using (var writer = new StreamWriter(Path.Combine(folder, $"{stem}_Summary.tsv")))
                report.WriteSummary(writer);
            using (var writer = new StreamWriter(Path.Combine(folder, $"{stem}_Parameters.tsv")))
                report.WriteParameters(writer);

            foreach (var (labeled, fileName, copyOf, modes) in report.MapFiles(stem))
            {
                string pdfPath = Path.Combine(folder, fileName);
                if (copyOf != null)
                {
                    File.Copy(Path.Combine(folder, copyOf), pdfPath);
                    continue;
                }

                var (page, map, legend, legendGrid) = SequenceCoverageMap.CreateExportPage();
                DrawMaxCoverageMap(protein, labeled.Result, intervals, map, legend, legendGrid, string.Join(", ", modes));
                SequenceCoverageMap.SaveRasterPdf(page, pdfPath);
            }
        }

        private void ExportSpectrumLibrary_Click(object sender, RoutedEventArgs e)
        {
            var checkedProteases = _allProteaseVm.ProteaseSpecificParameters
                .Where(vm => vm.IsSelected && vm.IsVisible)
                // Cloned: the dialog is non-modal, so the panel behind it stays editable and a
                // live reference would let edits change what a running export digests.
                .Select(vm => vm.ProteaseSpecificParams.Clone())
                .ToList();

            if (checkedProteases.Count == 0)
            {
                NotificationService.Instance.AddNotification(
                    "Select at least one protease before exporting a spectrum library.", NotificationType.Error);
                return;
            }

            // Digested on demand against the parameters checked in this window, which need not match
            // any completed run -- this tab is usable before one has happened.
            var source = new OnDemandDigestPeptideSource(ProteinsForTreeView.Keys.ToList(), checkedProteases);

            new SpectralLibraryOptionsWindow(
                source,
                checkedProteases.Select(p => p.DigestionAgentName).Distinct().ToList())
            {
                Owner = Window.GetWindow(this)
            }.Show();
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
            GuiGlobalParamsViewModel.Instance.PropertyChanged -= OnGlobalParamsChanged;
            GuiGlobalParamsViewModel.Instance.PropertyChanged += OnGlobalParamsChanged;

            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
            {
                vm.PropertyChanged -= OnProteaseParameterChanged;
                vm.PropertyChanged += OnProteaseParameterChanged;
            }

            // After subscribing: a ticked protease hidden while this tab was away must clear the mode.
            ApplyRnaMode();

            if (SelectedProtein == null && dataGridProteins.Items.Count > 0)
                dataGridProteins.SelectedIndex = 0;

            UpdateToggleButtonStyle();
        }

        void results_Unloaded(object sender, RoutedEventArgs e)
        {
            GuiGlobalParamsViewModel.Instance.PropertyChanged -= OnGlobalParamsChanged;
            foreach (var vm in _allProteaseVm.ProteaseSpecificParameters)
                vm.PropertyChanged -= OnProteaseParameterChanged;
        }

        #endregion
    }
}
