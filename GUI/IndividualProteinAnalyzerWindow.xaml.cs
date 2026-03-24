using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GuiFunctions;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;
using Tasks.CoverageMapConfiguration;
using TorchSharp.Modules;

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

        private Dictionary<string, Color> ProteaseByColor => _stableProteaseColors;

        private const int ResidueSpacing = 22;
        private const int SeqTextHeight = 20;
        private const int BarHeight = 6;
        private const int BarRowGap = 4;
        private const int BarTopMargin = 6;
        private const int BottomLineGap = 14;

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

            (_stableProteaseColors, _stableProteaseBrushes) = BuildStableColorMaps();

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

            (_stableProteaseColors, _stableProteaseBrushes) = BuildStableColorMaps();

            SetUpProteinsForTreeView();
            PopulateProteinList();

            WireProteasePanel();
            this.Loaded += results_Loaded;
            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Stable Color Assignment

        private static (Dictionary<string, Color> colors, Dictionary<string, SolidColorBrush> brushes)
            BuildStableColorMaps()
        {
            var allNames = ProteaseDictionary.Dictionary.Keys.ToList();
            var rgbMap = CoverageMapConfiguration.CreateProteaseColorMap(allNames);

            var colors = new Dictionary<string, Color>();
            var brushes = new Dictionary<string, SolidColorBrush>();

            foreach (var kvp in rgbMap)
            {
                var wpfColor = Color.FromRgb(kvp.Value.R, kvp.Value.G, kvp.Value.B);
                var brush = new SolidColorBrush(wpfColor);
                brush.Freeze();
                colors[kvp.Key] = wpfColor;
                brushes[kvp.Key] = brush;
            }

            return (colors, brushes);
        }

        private SolidColorBrush GetProteaseBrush(string proteaseName)
        {
            if (_stableProteaseBrushes.TryGetValue(proteaseName, out var brush))
                return brush;
            var fb = new SolidColorBrush(Colors.DimGray);
            fb.Freeze();
            return fb;
        }

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
            const int residuesPerLine = CoverageMapDataPreparer.DefaultResiduesPerLine;

            maxCoverageMap.Children.Clear();

            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(
                protein.BaseSequence, residuesPerLine);

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

            int proteaseCount = orderedCheckedProteases.Count;
            int barZoneHeight = proteaseCount > 0
                ? BarTopMargin + proteaseCount * (BarHeight + BarRowGap)
                : 0;
            int lineStride = SeqTextHeight + barZoneHeight + BottomLineGap;

            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                int lineStartRes = lineIndex * residuesPerLine + 1;
                int lineEndRes = lineStartRes + line.Length - 1;

                SequenceCoverageMap.txtDrawingLabel(
                    maxCoverageMap, new Point(0, height), lineStartRes.ToString(), Brushes.Black);

                for (int r = 0; r < line.Length; r++)
                {
                    string ch = line[r].ToString().ToUpper();
                    var pt = new Point(r * ResidueSpacing + 65, height);
                    SequenceCoverageMap.txtDrawing(maxCoverageMap, pt, ch, Brushes.Black);
                }

                int barBaseY = height + SeqTextHeight + BarTopMargin;

                for (int pi = 0; pi < orderedCheckedProteases.Count; pi++)
                {
                    string proteaseName = orderedCheckedProteases[pi];
                    var brush = GetProteaseBrush(proteaseName);
                    int laneY = barBaseY + pi * (BarHeight + BarRowGap);

                    if (!pepsByProtease.TryGetValue(proteaseName, out var intervals))
                        continue;

                    foreach (var (pepStart, pepEnd) in intervals)
                    {
                        if (pepEnd < lineStartRes || pepStart > lineEndRes)
                            continue;

                        int visStart = Math.Max(pepStart, lineStartRes);
                        int visEnd = Math.Min(pepEnd, lineEndRes);
                        int colStart = visStart - lineStartRes;
                        int colEnd = visEnd - lineStartRes;

                        double x1 = colStart * ResidueSpacing + 65;
                        double x2 = colEnd * ResidueSpacing + 65 + (ResidueSpacing - 4);

                        var bar = new Rectangle
                        {
                            Fill = brush,
                            Width = Math.Max(x2 - x1, 2),
                            Height = BarHeight,
                            RadiusX = 2,
                            RadiusY = 2
                        };
                        Canvas.SetLeft(bar, x1);
                        Canvas.SetTop(bar, laneY);
                        Panel.SetZIndex(bar, 1);
                        maxCoverageMap.Children.Add(bar);

                        if (pepStart >= lineStartRes)
                            DrawEndCap(x1, laneY, brush);
                        if (pepEnd <= lineEndRes)
                            DrawEndCap(x2, laneY, brush);
                    }
                }

                height += lineStride;
            }

            maxCoverageMap.Height = height + 20;

            DrawMaxCoverageLegend(orderedCheckedProteases);
        }

        private void DrawEndCap(double x, double laneY, SolidColorBrush brush)
        {
            var cap = new Line
            {
                X1 = x,
                Y1 = laneY - 1,
                X2 = x,
                Y2 = laneY + BarHeight + 1,
                Stroke = brush,
                StrokeThickness = 2
            };
            Panel.SetZIndex(cap, 2);
            maxCoverageMap.Children.Add(cap);
        }

        private void DrawMaxCoverageLegend(List<string> proteases)
        {
            maxCoverageLegend.Children.Clear();
            maxCoverageLegendGrid.Children.Clear();

            if (proteases.Count == 0) return;

            const double swatchW = 28;
            const double swatchH = 12;
            const double entryH = 20;
            const double startX = 4;
            const double startY = 4;
            const double colWidth = 190;
            const int cols = 3;

            for (int i = 0; i < proteases.Count; i++)
            {
                string name = proteases[i];
                var brush = GetProteaseBrush(name);
                int col = i % cols;
                int row = i / cols;

                double entryX = startX + col * colWidth;
                double entryY = startY + row * entryH;

                var swatch = new Rectangle
                {
                    Fill = brush,
                    Width = swatchW,
                    Height = swatchH,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(swatch, entryX);
                Canvas.SetTop(swatch, entryY + (entryH - swatchH) / 2.0);
                maxCoverageLegend.Children.Add(swatch);

                var tb = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(tb, entryX + swatchW + 4);
                Canvas.SetTop(tb, entryY + 3);
                maxCoverageLegend.Children.Add(tb);
            }

            int rows = (int)Math.Ceiling(proteases.Count / (double)cols);
            maxCoverageLegend.Height = startY + rows * entryH + 8;
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

            const double sequenceContentWidth = 25 * ResidueSpacing + 65 + 20;
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
