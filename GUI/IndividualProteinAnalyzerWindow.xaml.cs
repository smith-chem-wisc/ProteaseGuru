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
    public partial class IndividualProteinAnalyzerWindow : UserControl
    {
        #region Private Fields

        private readonly ProteinCoverageAnalyzer _analyzer;
        private ObservableCollection<string> proteinList;
        private ObservableCollection<string> filteredList;
        private Dictionary<Protein, ProteinForTreeView> ProteinsForTreeView;
        private Dictionary<InSilicoPep, (int, int)> partialPeptideMatches = new Dictionary<InSilicoPep, (int, int)>();
        private Dictionary<string, Color> ProteaseByColor;
        private Dictionary<string, SolidColorBrush> ModsByColor;
        private List<string> SelectedProteases;
        private ProteinForTreeView SelectedProtein;
        private bool MessageShow;

        /// <summary>User-specified digestion parameters</summary>
        private readonly RunParameters UserParams;

        private int ProteinExportCount = 1;

        #endregion

        #region Constructors

        public IndividualProteinAnalyzerWindow()
        {
        }

        /// <summary>
        /// Main constructor that initializes the protein results view with digestion data.
        /// </summary>
        public IndividualProteinAnalyzerWindow(
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
            SetUpColorDictionaries();

            this.Loaded += results_Loaded;

            SearchModifications.SetUp();
            SearchModifications.Timer.Tick += new EventHandler(searchBox_TextChangedHandler);
        }

        #endregion

        #region Initialization Methods

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

        private void PopulateProteinList()
        {
            foreach (var accession in _analyzer.ProteinAccessions)
            {
                proteinList.Add(accession);
                dataGridProteins.Items.Add(accession);
            }
            dataGridProteins.DataContext = proteinList;
        }

        private void SetUpColorDictionaries()
        {
            var rgbColorMap = CoverageMapConfiguration.CreateProteaseColorMap(_analyzer.Proteases);
            ProteaseByColor = rgbColorMap.ToDictionary(
                kvp => kvp.Key,
                kvp => ToWpfColor(kvp.Value));
            ModsByColor = new Dictionary<string, SolidColorBrush>();
        }

        #endregion

        #region Color Conversion Helpers

        private static Color ToWpfColor(RgbColor rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);
        private static SolidColorBrush ToWpfBrush(RgbColor rgb) => new SolidColorBrush(ToWpfColor(rgb));

        private SolidColorBrush GetPtmBrush(double mass)
        {
            var ptmName = CoverageMapConfiguration.GetPtmName(mass);
            var rgbColor = CoverageMapConfiguration.GetPtmColor(ptmName ?? "Other");
            var displayName = ptmName ?? "Other";
            if (!ModsByColor.ContainsKey(displayName))
                ModsByColor[displayName] = ToWpfBrush(rgbColor);
            return ToWpfBrush(rgbColor);
        }

        #endregion

        #region Search Functionality

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchModifications.SetTimer();
        }

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
                dataGridProteins.Items.Add(entry);
            SearchModifications.Timer.Stop();
        }

        private void searchProtein(string txt)
        {
            filteredList.Clear();
            foreach (var protein in proteinList)
                if (protein.Contains(txt, StringComparison.OrdinalIgnoreCase))
                    filteredList.Add(protein);
        }

        #endregion

        #region Protein Selection

        private void OnSelectionChanged()
        {
            if (MessageShow)
            {
                string message = _analyzer.IsMultiDatabase
                    ? "Note: More than one protein database was analyzed. Unique peptides are defined as being unique to a single protein in all analyzed databases."
                    : "Note: One protein database was analyzed. Unique peptides are defined as being unique to a single protein in the analyzed database.";
                MessageBox.Show(message);
                MessageShow = false;
            }

            if (dataGridProteins.SelectedItem != null)
            {
                string proteinName = dataGridProteins.SelectedItem.ToString();
                var protein = ProteinsForTreeView.FirstOrDefault(p => p.Key.Accession == proteinName).Value;
                if (protein != null)
                    SelectedProtein = protein;
            }
            else
            {
                SelectedProtein = ProteinsForTreeView.FirstOrDefault().Value;
            }

            if (SelectedProtein == null) return;
            DrawSequenceCoverageMap(SelectedProtein, SelectedProteases);
        }

        #endregion

        #region Sequence Coverage Map Drawing

        private void DrawSequenceCoverageMap(ProteinForTreeView protein, List<string> proteases)
        {
            const int residuesPerLine = CoverageMapDataPreparer.DefaultResiduesPerLine;
            int height = 10;
            int totalHeight = 0;
            int accumIndex = 0;

            map.Width = 0.90 * ResultsGrid.ActualWidth;

            string seqCoverage = protein.Protein.BaseSequence;
            var mods = protein.Protein.OneBasedPossibleLocalizedModifications;
            var variants = protein.Protein.AppliedSequenceVariations;

            var splitSeq = CoverageMapDataPreparer.SplitSequenceIntoLines(seqCoverage, residuesPerLine);
            var modsSplitByLine = mods.Count > 0
                ? CoverageMapDataPreparer.SplitModificationsByLine(mods, protein.Protein.Length, residuesPerLine)
                : new List<Dictionary<int, List<Modification>>>();
            var variantsByLine = variants.Count > 0
                ? CoverageMapDataPreparer.SplitVariantsByLine(variants, protein.Protein.Length, residuesPerLine)
                : new List<List<int>>();

            map.Children.Clear();
            legendGrid.Children.Clear();
            ModsByColor.Clear();

            var peptidesToDraw = new List<InSilicoPep>();
            foreach (var protease in proteases)
                peptidesToDraw.AddRange(_analyzer.GetPeptidesForProteinAndProtease(protein.Protein, protease));
            peptidesToDraw = peptidesToDraw.Distinct().ToList();

            var allPeptidesForProtein = _analyzer.GetAllPeptidesForProtein(protein.Protein);
            var (uniqueCovered, sharedOnlyCovered) = CalculateCoveredResiduesByType(allPeptidesForProtein);

            var indices = new Dictionary<int, List<int>>();
            SequenceCoverageMap.txtDrawing(map, new Point(0, height), $"Sequence Coverage Map of {protein.Protein.Accession}:", Brushes.Black);
            height += 30;
            int totalAddedSpace = 0;

            for (int lineIndex = 0; lineIndex < splitSeq.Count; lineIndex++)
            {
                var line = splitSeq[lineIndex];
                indices.Clear();
                var lineLabel = (lineIndex * residuesPerLine) + 1;

                SequenceCoverageMap.txtDrawingLabel(map, new Point(0, height), lineLabel.ToString(), Brushes.Black);

                int lineStartResidue = lineIndex * residuesPerLine + 1;
                DrawSequenceCharacters(line, lineIndex, variantsByLine, height, residuesPerLine, uniqueCovered, sharedOnlyCovered, lineStartResidue);

                if (mods.Count > 0 && lineIndex < modsSplitByLine.Count)
                    DrawModifications(modsSplitByLine[lineIndex], height, residuesPerLine);

                ProcessPartialPeptides(line, accumIndex, height, indices);
                DrawPeptideHighlights(line, accumIndex, height, indices, peptidesToDraw);

                int addedSpace = indices.Count > 7 ? (indices.Count - 7) * 10 : 0;
                totalAddedSpace += addedSpace;
                height += 100 + addedSpace;
                accumIndex += line.Length;
            }

            totalHeight = (splitSeq.Count * 100) + totalAddedSpace;
            map.Height = totalHeight + 100;

            if (mods.Count > 0)
                SequenceCoverageMap.drawLegendMods(legend, ProteaseByColor, ModsByColor, proteases, legendGrid, variants.Count > 0);
            else
                SequenceCoverageMap.drawLegend(legend, ProteaseByColor, proteases, legendGrid, variants.Count > 0);
        }

        private (HashSet<int> uniqueCovered, HashSet<int> sharedOnlyCovered) CalculateCoveredResiduesByType(List<InSilicoPep> peptides)
        {
            var uniqueCovered = new HashSet<int>();
            var sharedCovered = new HashSet<int>();

            foreach (var peptide in peptides)
            {
                bool isUnique = _analyzer.IsMultiDatabase ? peptide.UniqueAllDbs : peptide.Unique;
                for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                {
                    if (isUnique) uniqueCovered.Add(i);
                    else sharedCovered.Add(i);
                }
            }

            return (uniqueCovered, new HashSet<int>(sharedCovered.Except(uniqueCovered)));
        }

        private void DrawSequenceCharacters(string line, int lineIndex, List<List<int>> variantsByLine,
            int height, int spacing, HashSet<int> uniqueCovered, HashSet<int> sharedOnlyCovered, int lineStartResidue)
        {
            bool hasVariants = variantsByLine.Count > lineIndex && variantsByLine[lineIndex].Count > 0;

            for (int r = 0; r < line.Length; r++)
            {
                int residuePosition = lineStartResidue + r;
                bool isVariant = hasVariants && variantsByLine[lineIndex].Contains(r + 1);
                var brush = isVariant ? Brushes.Red : Brushes.Black;
                string character = line[r].ToString().ToUpper();

                if (uniqueCovered.Contains(residuePosition))
                    SequenceCoverageMap.txtDrawing(map, new Point(r * spacing + 65, height), character, brush);
                else if (sharedOnlyCovered.Contains(residuePosition))
                    SequenceCoverageMap.txtDrawingShared(map, new Point(r * spacing + 65, height), character, brush);
                else
                    SequenceCoverageMap.txtDrawingUncovered(map, new Point(r * spacing + 65, height), character, brush);
            }
        }

        private void DrawModifications(Dictionary<int, List<Modification>> modsForLine, int height, int spacing)
        {
            foreach (var mod in modsForLine)
            {
                if (mod.Value.Count > 1)
                {
                    var colors = mod.Value.Select(m => GetPtmBrush(Convert.ToDouble(m.MonoisotopicMass))).ToList();
                    SequenceCoverageMap.stackedCircledTxtDraw(map, new Point(mod.Key * spacing + 38, height), colors);
                }
                else
                {
                    var brush = GetPtmBrush(Convert.ToDouble(mod.Value.First().MonoisotopicMass));
                    SequenceCoverageMap.circledTxtDraw(map, new Point(mod.Key * spacing + 38, height), brush);
                }
            }
        }

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
                        partialPeptideMatches.Add(peptide, (partialIndex, highlightIndex));
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

        private void maxCoverageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            maxCoverageMapViewer.Height = .8 * MaxCoverageGrid.ActualHeight;
            maxCoverageMapViewer.Width = .99 * MaxCoverageGrid.ActualWidth;
        }

        private void MaxCoverageMode_Changed(object sender, RoutedEventArgs e)
        {
            if (SelectedProtein == null) return;
            // TODO: implement greedy / best-pair / best-triplet rendering
        }

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
            if (SelectedProtein == null)
            {
                MessageBox.Show("Please select a protein before exporting.");
                return;
            }

            var fileDirectory = UserParams.OutputFolder + @"\ProteaseGuruDigestionResults";
            string proteinName = SelectedProtein.DisplayName;
            string subFolder = Path.Combine(fileDirectory, proteinName);

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
                dc.DrawRectangle(new VisualBrush(mapGrid), null, new Rect(new Point(), bounds.Size));
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

            var allPeptides = _analyzer.GetAllPeptidesForProtein(SelectedProtein.Protein);
            var uniquePeptides = allPeptides.Where(p => _analyzer.IsMultiDatabase ? p.UniqueAllDbs : p.Unique).ToList();

            SaveMetadata(subFolder, proteinName, SelectedProtein.Protein, allPeptides);

            string header = BuildPeptideHeader();
            WritePeptidesToTsv(allPeptides, subFolder, proteinName, header, "ProteaseGuruPeptides");
            if (uniquePeptides.Count > 0)
                WritePeptidesToTsv(uniquePeptides, subFolder, proteinName, header, "ProteaseGuruUniquePeptides");

            File.WriteAllLines(Path.Combine(subFolder, resultsFile), results);

            if (MessageBox.Show($"Files created at {subFolder}! Copy paths to clipboard?", "", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Clipboard.SetText($"Coverage Map: {filePath}\r\nResults: {Path.Combine(subFolder, resultsFile)}");
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
                metaData.Add($"{variant.OneBasedBeginPosition}{tab}{variant.OneBasedEndPosition}{tab}{variant.OriginalSequence}{tab}{variant.VariantSequence}");

            metaData.Add("Post-Translational Modifications");
            metaData.Add("Residue\tModifications");
            foreach (var mod in protein.OneBasedPossibleLocalizedModifications)
                metaData.Add($"{mod.Key}{tab}{string.Join(",", mod.Value.Select(m => m.IdWithMotif))}");

            metaData.Add("All Peptides");
            metaData.Add("Start Residue\tEnd Residue\tProtease\tUnique");
            foreach (var peptide in allPeptides.Select(p => $"{p.StartResidue}{tab}{p.EndResidue}{tab}{p.Protease}{tab}{p.UniqueAllDbs}").Distinct())
                metaData.Add(peptide);

            File.WriteAllLines(Path.Combine(subFolder, $"{proteinName}_MapMetaData.txt"), metaData);
        }

        private static string BuildPeptideHeader() => string.Join("\t",
            "Database", "Protease", "Base Sequence", "Full Sequence", "Previous Amino Acid",
            "Next Amino Acid", "Start Residue", "End Residue", "Length", "Molecular Weight",
            "Protein Accession", "Protein Name", "Unique Peptide (in this database)",
            "Unique Peptide (in all databases)", "Peptide sequence exclusive to this Database",
            "Hydrophobicity", "Electrophoretic Mobility");

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
                        output.WriteLine(line);
                    inFile++;
                }
                fileCount++;
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
        }

        #endregion
    }
}
