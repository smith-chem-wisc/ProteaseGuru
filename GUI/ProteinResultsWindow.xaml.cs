using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
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

        /// <summary>Analyzer that organizes and calculates protein coverage data</summary>
        private readonly ProteinCoverageAnalyzer _analyzer;

        /// <summary>Complete list of protein accessions from all databases</summary>
        private ObservableCollection<string> proteinList;

        /// <summary>Filtered list of proteins based on user search input</summary>
        private ObservableCollection<string> filteredList;

        /// <summary>Maps Protein objects to their tree view representation (GUI-specific)</summary>
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;

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
        // Assigned once at construction from the full ProteaseDictionary so that
        // a protease always maps to the same color regardless of which subset is
        // currently selected — identical palette to IndividualProteinAnalyzerWindow.
        private readonly Dictionary<string, Color> _stableProteaseColors;
        private readonly Dictionary<string, SolidColorBrush> _stableProteaseBrushes;

        // ── Bar geometry constants ────────────────────────────────────────────
        private const int ResidueSpacing = 25;  // px between residue X-positions
        private const int SeqLeftOffset = 45;  // px left margin before first residue
        private const int SeqTextHeight = 20;  // px for the amino-acid text row
        private const int BarHeight = 6;   // px thickness of each coloured peptide bar
        private const int BarRowGap = 3;   // px gap between stacked protease bars
        private const int BarTopMargin = 6;   // px gap between amino-acid text and first bar row
        private const int BottomLineGap = 12;  // px below the last bar before the next sequence line

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
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile,
            RunParameters userParams,
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease)
        {
            InitializeComponent();

            _analyzer = new ProteinCoverageAnalyzer(peptideByFile, sequenceCoverageByProtease);
            UserParams = userParams;
            SelectedProteases = new List<string>();
            SelectedProtein = null;
            MessageShow = true;

            proteinList = new ObservableCollection<string>();
            filteredList = new ObservableCollection<string>();
            ProteinsForTreeView = new Dictionary<Protein, ProteinForTreeView>();

            SetUpProteinsForTreeView();
            PopulateProteinList();

            // Build stable color map from the full ProteaseDictionary — same palette
            // as IndividualProteinAnalyzerWindow so colors are identical across both views.
            (_stableProteaseColors, _stableProteaseBrushes) = BuildStableColorMaps();

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

        #region Stable Color Map

        /// <summary>
        /// Assigns a color to every protease in ProteaseDictionary in stable order,
        /// once at construction time. Identical logic to IndividualProteinAnalyzerWindow.
        /// </summary>
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

        /// <summary>Returns the stable-palette brush for a protease, with a grey fallback.</summary>
        private SolidColorBrush GetProteaseBrush(string proteaseName)
        {
            if (_stableProteaseBrushes.TryGetValue(proteaseName, out var brush))
                return brush;
            var fb = new SolidColorBrush(Colors.DimGray);
            fb.Freeze();
            return fb;
        }

        /// <summary>Returns the stable-palette Color for a protease, with a grey fallback.</summary>
        private Color GetProteaseColor(string proteaseName)
        {
            return _stableProteaseColors.TryGetValue(proteaseName, out var c) ? c : Colors.DimGray;
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

            // Update the header label
            proteinSummaryHeader.Content = $"Digestion Results for {coverageResult.DisplayName}";

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

        /// <summary>
        /// Main method for drawing the protein sequence coverage map.
        /// Each selected protease gets its own swim-lane of coloured bars beneath
        /// each sequence line — identical visual style to IndividualProteinAnalyzerWindow.
        /// Unique peptides are fully opaque; shared peptides are semi-transparent.
        /// Row height is dynamically adjusted to accommodate all protease lanes.
        /// </summary>
        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            const int residuesPerLine = CoverageMapDataPreparer.DefaultResiduesPerLine;

            // Cap canvas width to actual sequence content so there is no large right margin.
            // 25 residues × ResidueSpacing + SeqLeftOffset left margin + 20px right padding.
            const double sequenceContentWidth = 25 * ResidueSpacing + SeqLeftOffset + 20;
            double availableWidth = ResultsGrid.ActualWidth > 0 ? ResultsGrid.ActualWidth - 20 : sequenceContentWidth;
            map.Width = Math.Min(availableWidth, sequenceContentWidth);
            map.Children.Clear();
            legendGrid.Children.Clear();

            string seq = protein.Protein.BaseSequence;
            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(seq, residuesPerLine);

            // ── Title ────────────────────────────────────────────────────────
            int height = 10;
            SequenceCoverageMap.txtDrawing(map, new Point(0, height),
                $"Sequence Coverage Map of {protein.Protein.Accession}:", Brushes.Black);
            height += 30;

            // ── Per-protease peptide intervals ───────────────────────────────
            // Group peptides by protease; each protease gets its own ordered lane.
            var pepsByProtease = new Dictionary<string, List<InSilicoPep>>();
            foreach (var proteaseName in proteases)
            {
                var peps = _analyzer.GetPeptidesForProteinAndProtease(protein.Protein, proteaseName)
                    .Distinct()
                    .OrderBy(p => p.StartResidue)
                    .ToList();
                pepsByProtease[proteaseName] = peps;
            }

            // ── Dynamic line stride ──────────────────────────────────────────
            int laneCount = proteases.Count;
            int barZoneH = laneCount > 0
                ? BarTopMargin + laneCount * (BarHeight + BarRowGap)
                : 0;
            int lineStride = SeqTextHeight + barZoneH + BottomLineGap;

            // ── Sequence lines ───────────────────────────────────────────────
            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                int lineStartRes = lineIndex * residuesPerLine + 1; // 1-based
                int lineEndRes = lineStartRes + line.Length - 1;

                // Line-number label
                SequenceCoverageMap.txtDrawingLabel(
                    map, new Point(0, height), lineStartRes.ToString(), Brushes.Black);

                // Amino-acid characters — all uniform; coverage is shown by bars
                for (int r = 0; r < line.Length; r++)
                {
                    string ch = line[r].ToString().ToUpper();
                    SequenceCoverageMap.txtDrawing(
                        map, new Point(r * ResidueSpacing + SeqLeftOffset, height), ch, Brushes.Black);
                }

                // ── Peptide bars ─────────────────────────────────────────────
                int barBaseY = height + SeqTextHeight + BarTopMargin;

                for (int pi = 0; pi < proteases.Count; pi++)
                {
                    string proteaseName = proteases[pi];
                    var brush = GetProteaseBrush(proteaseName);
                    int laneY = barBaseY + pi * (BarHeight + BarRowGap);

                    if (!pepsByProtease.TryGetValue(proteaseName, out var peps)) continue;

                    foreach (var pep in peps)
                    {
                        if (pep.EndResidue < lineStartRes || pep.StartResidue > lineEndRes) continue;

                        int visStart = Math.Max(pep.StartResidue, lineStartRes);
                        int visEnd = Math.Min(pep.EndResidue, lineEndRes);
                        int colStart = visStart - lineStartRes;
                        int colEnd = visEnd - lineStartRes;

                        double x1 = colStart * ResidueSpacing + SeqLeftOffset;
                        double x2 = colEnd * ResidueSpacing + SeqLeftOffset + (ResidueSpacing - 4);

                        bool isUnique = _analyzer.IsMultiDatabase ? pep.UniqueAllDbs : pep.Unique;

                        // Unique: full opacity; shared: visibly translucent but easy to see
                        var barBrush = isUnique
                            ? brush
                            : new SolidColorBrush(brush.Color) { Opacity = 0.35 };

                        var bar = new System.Windows.Shapes.Rectangle
                        {
                            Fill = barBrush,
                            Width = Math.Max(x2 - x1, 2),
                            Height = BarHeight,
                            RadiusX = 2,
                            RadiusY = 2
                        };
                        Canvas.SetLeft(bar, x1);
                        Canvas.SetTop(bar, laneY);
                        Panel.SetZIndex(bar, 1);
                        map.Children.Add(bar);

                        // End-caps where peptide actually starts/ends on this line
                        if (pep.StartResidue >= lineStartRes)
                            DrawBarEndCap(x1, laneY, brush, isUnique);
                        if (pep.EndResidue <= lineEndRes)
                            DrawBarEndCap(x2, laneY, brush, isUnique);
                    }
                }

                height += lineStride;
            }

            map.Height = height + 20;

            // ── Legend ───────────────────────────────────────────────────────
            DrawCoverageMapLegend(proteases);
        }

        /// <summary>Draws a vertical end-cap line at the start or end of a peptide bar.</summary>
        private void DrawBarEndCap(double x, double laneY, SolidColorBrush brush, bool isUnique)
        {
            var cap = new System.Windows.Shapes.Line
            {
                X1 = x,
                Y1 = laneY - 1,
                X2 = x,
                Y2 = laneY + BarHeight + 1,
                Stroke = brush,
                StrokeThickness = 2,
                Opacity = isUnique ? 1.0 : 0.35
            };
            Panel.SetZIndex(cap, 2);
            map.Children.Add(cap);
        }

        /// <summary>
        /// Draws the legend: one coloured swatch per selected protease plus
        /// a unique/shared opacity example row — matching IndividualProteinAnalyzerWindow style.
        /// </summary>
        private void DrawCoverageMapLegend(List<string> proteases)
        {
            legendGrid.Children.Clear();
            legend.Children.Clear();

            if (proteases.Count == 0) return;

            const double swatchW = 28;
            const double swatchH = 12;
            const double entryH = 20;
            const double startX = SeqLeftOffset;
            const double startY = 4;
            const double colWidth = 190;
            const int cols = 3;

            // ── Protease colour swatches ─────────────────────────────────────
            for (int i = 0; i < proteases.Count; i++)
            {
                string name = proteases[i];
                var brush = GetProteaseBrush(name);
                int col = i % cols;
                int row = i / cols;

                double entryX = startX + col * colWidth;
                double entryY = startY + row * entryH;

                var swatch = new System.Windows.Shapes.Rectangle
                {
                    Fill = brush,
                    Width = swatchW,
                    Height = swatchH,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(swatch, entryX);
                Canvas.SetTop(swatch, entryY + (entryH - swatchH) / 2.0);
                legend.Children.Add(swatch);

                var tb = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(tb, entryX + swatchW + 4);
                Canvas.SetTop(tb, entryY + 3);
                legend.Children.Add(tb);
            }

            // ── Unique vs shared opacity key ─────────────────────────────────
            int proteaseRows = (int)Math.Ceiling(proteases.Count / (double)cols);
            double keyY = startY + proteaseRows * entryH + 6;

            // Solid swatch = unique
            var solidSwatch = new System.Windows.Shapes.Rectangle
            {
                Fill = Brushes.Gray,
                Width = swatchW,
                Height = swatchH,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(solidSwatch, startX);
            Canvas.SetTop(solidSwatch, keyY + (entryH - swatchH) / 2.0);
            legend.Children.Add(solidSwatch);

            var uniqueLabel = new TextBlock
            {
                Text = "Unique peptide",
                FontSize = 11,
                Foreground = Brushes.Black
            };
            Canvas.SetLeft(uniqueLabel, startX + swatchW + 4);
            Canvas.SetTop(uniqueLabel, keyY + 3);
            legend.Children.Add(uniqueLabel);

            // Translucent swatch = shared
            var sharedSwatch = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Colors.Gray) { Opacity = 0.35 },
                Width = swatchW,
                Height = swatchH,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(sharedSwatch, startX + colWidth);
            Canvas.SetTop(sharedSwatch, keyY + (entryH - swatchH) / 2.0);
            legend.Children.Add(sharedSwatch);

            var sharedLabel = new TextBlock
            {
                Text = "Shared peptide (translucent)",
                FontSize = 11,
                Foreground = Brushes.Black
            };
            Canvas.SetLeft(sharedLabel, startX + colWidth + swatchW + 4);
            Canvas.SetTop(sharedLabel, keyY + 3);
            legend.Children.Add(sharedLabel);

            legend.Height = keyY + entryH + 8;
        }

        #endregion

        #region Event Handlers

        private void proteins_SelectedCellsChanged(object sender, SelectionChangedEventArgs e) => OnSelectionChanged();

        private void proteaseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnSelectionChanged();

        private void resultsSizeChanged(object sender, SizeChangedEventArgs e) => ChangeMapScrollViewSize();

        private void ChangeMapScrollViewSize()
        {
            mapViewer.Height = .8 * ResultsGrid.ActualHeight;
            mapViewer.Width = ResultsGrid.ActualWidth;
        }

        void results_Loaded(object sender, RoutedEventArgs e)
        {
            Window window = Window.GetWindow(this);
            window.Closing += window_Closing;

            // Auto-select the first protein so the coverage map is populated on load
            if (dataGridProteins.Items.Count > 0)
            {
                dataGridProteins.SelectedIndex = 0;
                dataGridProteins.ScrollIntoView(dataGridProteins.Items[0]);
            }
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
