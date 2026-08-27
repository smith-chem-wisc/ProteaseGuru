using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using Engine;
using Omics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using ProteaseGuruGuiFunctions;
using Tasks;

namespace ProteaseGuruGui
{
    // code for histogram generation
    public class PlotModelStat : INotifyPropertyChanged, IPlotModel
    {
        private PlotModel privateModel;
        private readonly List<InSilicoPep> AllPeptides = new();
        public Dictionary<string, List<InSilicoPep>> PeptidesByProtease = new();
        public Dictionary<string, Dictionary<IBioPolymer, (double, double)>> SequenceCoverageByProtease_Return = new();
        private readonly Dictionary<string, List<double>> SequenceCoverageByProtease = new();
        private readonly Dictionary<string, List<double>> SequenceCoverageUniqueByProtease = new();
        private readonly Dictionary<string, List<double>> UniquePeptidesPerProtein = new();
        List<string> Proteases = new();
        //access series stuff here
        public Dictionary<string, Dictionary<string, string>> DataTable = new();

        private static List<OxyColor> columnColors = new()
        {
           OxyColor.FromRgb(130, 88, 159), OxyColor.FromRgb(0, 148, 50), OxyColor.FromRgb(181, 52, 113), OxyColor.FromRgb(52, 152, 219), OxyColor.FromRgb(230, 126, 34), OxyColor.FromRgb(27, 20, 100), OxyColor.FromRgb(253, 167, 223),
           OxyColor.FromRgb(99, 110, 114), OxyColor.FromRgb(255, 221, 89), OxyColor.FromRgb(162, 155, 254), OxyColor.FromRgb(58, 227, 116), OxyColor.FromRgb(252, 66, 123),
           OxyColor.FromRgb(126, 214, 223), OxyColor.FromRgb(249, 127, 81), OxyColor.FromRgb(189, 195, 199), OxyColor.FromRgb(241, 196, 15), OxyColor.FromRgb(0, 98, 102), OxyColor.FromRgb(142, 68, 173),
           OxyColor.FromRgb(225, 112, 85), OxyColor.FromRgb(255, 184, 184), OxyColor.FromRgb(61, 193, 211), OxyColor.FromRgb(224, 86, 253), OxyColor.FromRgb(196, 229, 56), OxyColor.FromRgb(255, 71, 87),
           OxyColor.FromRgb(88, 177, 159), OxyColor.FromRgb(111, 30, 81), OxyColor.FromRgb(129, 236, 236), OxyColor.FromRgb(179, 57, 57), OxyColor.FromRgb(232, 67, 147)
        };

        public PlotModel Model
        {
            get
            {
                return privateModel;
            }
            private set
            {
                privateModel = value;
                NotifyPropertyChanged("Model");
            }
        }

        public OxyColor Background => OxyColors.White;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        public PlotModelStat(string plotName, List<string> dbSelected, Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile, RunParameters userParams, Dictionary<string, Dictionary<IBioPolymer, (double, double)>> sequenceCoverageByProtease, bool detectableOnly = false)
        {
            privateModel = new PlotModel { Title = (string)ProteinRnaTerminologyConverter.Instance.Convert(plotName, GetType(), null, CultureInfo.InvariantCulture), DefaultFontSize = 12 };

            Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>> databasePeptides = new();

            if (dbSelected.Count > 1)
            {
                NotificationService.Instance.AddNotification("Note: More than one protein database has been selected. Unique peptides are defined as being unique to a single protein in all selected databases.", NotificationType.Information);

                List<InSilicoPep> allPeptides = new();

                foreach (var db in dbSelected)
                {
                    var pep = peptideByFile[db];
                    foreach (var entry in pep)
                    {
                        foreach (var protein in entry.Value)
                        {
                            allPeptides.AddRange(protein.Value);
                        }
                    }
                }

                Dictionary<string, List<InSilicoPep>> peptidesToProteins = new();

                if (plotName == " Protein Sequence Coverage (Unique Peptides Only)" || plotName == " Number of Unique Peptides per Protein")
                {
                    if (userParams.TreatModifiedPeptidesAsDifferent)
                    {
                        peptidesToProteins = allPeptides.GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    else
                    {
                        peptidesToProteins = allPeptides.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    var unique = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1 && p.Value.Select(p => p.Database).Distinct().Count() == 1).ToDictionary(group => group.Key, group => group.Value);
                    var shared = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() > 1).ToDictionary(group => group.Key, group => group.Value);

                    foreach (var db in dbSelected)
                    {
                        var pep = peptideByFile[db];
                        foreach (var entry in pep)
                        {

                            if (databasePeptides.ContainsKey(entry.Key))
                            {
                                foreach (var prot in pep[entry.Key])
                                {
                                    if (databasePeptides[entry.Key].ContainsKey(prot.Key))
                                    {
                                        List<InSilicoPep> proteinSpecificPeptides = new();
                                        foreach (var peptide in prot.Value)
                                        {
                                            if (userParams.TreatModifiedPeptidesAsDifferent)
                                            {
                                                if (unique.ContainsKey(peptide.FullSequence))
                                                {
                                                    peptide.Unique = true;
                                                }
                                                else
                                                {
                                                    peptide.Unique = false;
                                                }
                                            }
                                            else
                                            {
                                                if (unique.ContainsKey(peptide.BaseSequence))
                                                {
                                                    peptide.Unique = true;
                                                }
                                                else
                                                {
                                                    peptide.Unique = false;
                                                }
                                            }

                                            proteinSpecificPeptides.Add(peptide);
                                        }

                                        databasePeptides[entry.Key][prot.Key].AddRange(proteinSpecificPeptides);
                                    }
                                    else
                                    {
                                        List<InSilicoPep> proteinSpecificPeptides = new();
                                        foreach (var peptide in prot.Value)
                                        {
                                            if (userParams.TreatModifiedPeptidesAsDifferent)
                                            {
                                                if (unique.ContainsKey(peptide.FullSequence))
                                                {
                                                    peptide.Unique = true;
                                                }
                                                else
                                                {
                                                    peptide.Unique = false;
                                                }
                                            }
                                            else
                                            {
                                                if (unique.ContainsKey(peptide.BaseSequence))
                                                {
                                                    peptide.Unique = true;
                                                }
                                                else
                                                {
                                                    peptide.Unique = false;
                                                }
                                            }
                                            proteinSpecificPeptides.Add(peptide);

                                        }
                                        databasePeptides[entry.Key].Add(prot.Key, proteinSpecificPeptides);
                                    }
                                }
                            }
                            else
                            {
                                Dictionary<IBioPolymer, List<InSilicoPep>> proteinDic = new();
                                foreach (var prot in entry.Value)
                                {
                                    List<InSilicoPep> proteinSpecificPeptides = new();
                                    foreach (var peptide in prot.Value)
                                    {
                                        if (userParams.TreatModifiedPeptidesAsDifferent)
                                        {
                                            if (unique.ContainsKey(peptide.FullSequence))
                                            {
                                                peptide.Unique = true;
                                            }
                                            else
                                            {
                                                peptide.Unique = false;
                                            }
                                        }
                                        else
                                        {
                                            if (unique.ContainsKey(peptide.BaseSequence))
                                            {
                                                peptide.Unique = true;
                                            }
                                            else
                                            {
                                                peptide.Unique = false;
                                            }
                                        }
                                        proteinSpecificPeptides.Add(peptide);

                                    }
                                    proteinDic.Add(prot.Key, proteinSpecificPeptides);
                                }
                                databasePeptides.Add(entry.Key, proteinDic);
                            }
                        }

                    }
                    if (!detectableOnly)
                    {
                        SequenceCoverageByProtease_Return = CalculateProteinSequenceCoverage(databasePeptides);
                    }
                }
                else
                {
                    foreach (var db in dbSelected)
                    {
                        var pep = peptideByFile[db];
                        foreach (var entry in pep)
                        {

                            if (databasePeptides.ContainsKey(entry.Key))
                            {
                                foreach (var prot in pep[entry.Key])
                                {
                                    if (databasePeptides[entry.Key].ContainsKey(prot.Key))
                                    {
                                        databasePeptides[entry.Key][prot.Key].AddRange(prot.Value);
                                    }
                                    else
                                    {
                                        // Copy the source list rather than aliasing it, so a later
                                        // AddRange can never mutate the shared peptideByFile data.
                                        databasePeptides[entry.Key].Add(prot.Key, new List<InSilicoPep>(prot.Value));
                                    }
                                }
                            }
                            else
                            {
                                Dictionary<IBioPolymer, List<InSilicoPep>> proteinDic = new();
                                foreach (var prot in entry.Value)
                                {
                                    // Copy the source list rather than aliasing it, so a later
                                    // AddRange can never mutate the shared peptideByFile data.
                                    proteinDic.Add(prot.Key, new List<InSilicoPep>(prot.Value));
                                }
                                databasePeptides.Add(entry.Key, proteinDic);
                            }
                        }

                    }
                }
                if (!detectableOnly)
                {
                    SequenceCoverageByProtease_Return = CalculateProteinSequenceCoverage(databasePeptides);
                }
            }
            else
            {
                NotificationService.Instance.AddNotification("Note: One protein database has been selected. Unique peptides are defined as being unique to a single protein in this database.", NotificationType.Information);
                databasePeptides = peptideByFile[dbSelected.FirstOrDefault()];
                // Reuse the pre-computed coverage result when available; only recalculate if the
                // caller did not supply one (e.g. first run before any coverage has been computed).
                if (!detectableOnly)
                {
                    SequenceCoverageByProtease_Return = sequenceCoverageByProtease.Count > 0
                        ? sequenceCoverageByProtease
                        : CalculateProteinSequenceCoverage(databasePeptides);
                }
            }

            // Detectable-only toggle: restrict every protein's peptide list to PFly-detectable
            // peptides, then recompute coverage from that filtered set so every downstream
            // histogram (counts, coverage, unique-per-protein, etc.) reflects detectable counts.
            if (detectableOnly)
            {
                databasePeptides = databasePeptides.ToDictionary(
                    protease => protease.Key,
                    protease => protease.Value.ToDictionary(
                        protein => protein.Key,
                        protein => protein.Value.Where(p => p.PflyDetectability == true).ToList()));
                SequenceCoverageByProtease_Return = CalculateProteinSequenceCoverage(databasePeptides);
            }

            List<InSilicoPep> peptides = new();
            Dictionary<string, List<InSilicoPep>> peptidesByProtease = new();


            foreach (var protease in databasePeptides)
            {
                List<InSilicoPep> proteasePeptides = new();
                foreach (var protein in protease.Value)
                {
                    proteasePeptides.AddRange(protein.Value);
                    peptides.AddRange(protein.Value);
                }
                if (peptidesByProtease.ContainsKey(protease.Key))
                {
                    peptidesByProtease[protease.Key] = proteasePeptides;
                }
                else
                {
                    peptidesByProtease.Add(protease.Key, proteasePeptides);
                }
            }

            AllPeptides = peptides;
            this.PeptidesByProtease = peptidesByProtease;


            foreach (var protease in SequenceCoverageByProtease_Return)
            {
                List<double> coverages = new List<double>();
                List<double> uniqueCoverages = new List<double>();
                foreach (var protein in protease.Value)
                {
                    coverages.Add(protein.Value.Item1);
                    uniqueCoverages.Add(protein.Value.Item2);
                }
                SequenceCoverageByProtease.Add(protease.Key, coverages);
                SequenceCoverageUniqueByProtease.Add(protease.Key, uniqueCoverages);
            }
            foreach (var protease in peptidesByProtease)
            {
                List<double> uniquePeptides = new List<double>();
                foreach (var proteinGroup in protease.Value.GroupBy(pep => pep.Protein))
                {
                    uniquePeptides.Add(proteinGroup.Count(pep => pep.Unique));
                }
                UniquePeptidesPerProtein.Add(protease.Key, uniquePeptides);
            }
            createPlot(plotName);
            privateModel.DefaultColors = columnColors;
        }

        private static string NormalizePlotName(string plotType)
        {
            // Map RNA terminology back to protein terminology for consistent plot handling
            return plotType.Trim() switch
            {
                "Oligo Length" => " Peptide Length",
                "Transcript Sequence Coverage" => " Protein Sequence Coverage",
                "Transcript Sequence Coverage (Unique Oligos Only)" => " Protein Sequence Coverage (Unique Peptides Only)",
                "Number of Unique Oligos per Transcript" => " Number of Unique Peptides per Protein",
                "Predicted Oligo Hydrophobicity" => " Predicted Peptide Hydrophobicity",
                "Predicted Oligo Electrophoretic Mobility" => " Predicted Peptide Electrophoretic Mobility",
                "Nucleic Acid Distribution" => " Amino Acid Distribution",
                _ => plotType
            };
        }

        private void createPlot(string plotType)
        {
            // Normalize plot name to handle both protein and RNA terminology
            string normalizedPlotType = NormalizePlotName(plotType);

            if (normalizedPlotType.Equals(" Peptide Length"))
            {
                histogramPlot(1);
            }
            else if (normalizedPlotType.Equals(" Protein Sequence Coverage"))
            {
                histogramPlot(2);
            }
            else if (normalizedPlotType.Equals(" Protein Sequence Coverage (Unique Peptides Only)"))
            {
                histogramPlot(3);
            }
            else if (normalizedPlotType.Equals(" Number of Unique Peptides per Protein"))
            {
                histogramPlot(4);
            }
            else if (normalizedPlotType.Equals(" Predicted Peptide Hydrophobicity"))
            {
                histogramPlot(5);
            }
            else if (normalizedPlotType.Equals(" Predicted Peptide Electrophoretic Mobility"))
            {
                histogramPlot(6);
            }
            else if (normalizedPlotType.Equals(" Chronologer Predicted Retention Time"))
            {
                histogramPlot(7);
            }
            else if (normalizedPlotType.Equals(" Amino Acid Distribution"))
            {
                columnPlot();
            }
        }
        // returns a bin index of number relative to 0, midpoints are rounded towards zero
        private static int roundToBin(double number, double binSize)
        {
            int sign = number < 0 ? -1 : 1;
            double d = number * sign;
            double remainder = (d / binSize) - Math.Floor(d / binSize);
            int i = remainder != 0 ? (int)(Math.Ceiling(d / binSize)) : (int)(d / binSize);
            return i * sign;
        }

        // used by histogram plots, gives additional properties for the tracker to display
        // OxyPlot 2.2: ColumnItem renamed to BarItem in OxyPlot.Series
        private class HistItem : OxyPlot.Series.BarItem
        {
            public int total { get; set; }
            public string bin { get; set; }
            public HistItem(double value, int categoryIndex, string bin, int total) : base(value, categoryIndex)
            {
                this.total = total;
                this.bin = bin;
            }
        }

        private class CustomBarItem : OxyPlot.Series.BarItem
        {
            public int total { get; set; }
            public string label { get; set; }
            public CustomBarItem(double value, int categoryIndex, string label, int total) : base(value, categoryIndex)
            {
                this.total = total;
                this.label = label;
            }
        }

        private void columnPlot()
        {
            // OxyPlot 2.2: Legend properties moved to separate Legend object
            var legend = new Legend
            {
                LegendTitle = "Protease",
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.BottomLeft,
                LegendItemAlignment = OxyPlot.HorizontalAlignment.Left,
                LegendFontSize = 12,
                LegendOrientation = LegendOrientation.Horizontal
            };
            privateModel.Legends.Add(legend);
            privateModel.TitleFontSize = 15;

            string yAxisTitle = "Count";
            string xAxisTitle = GuiGlobalParamsViewModel.Instance.IsRnaMode ? "Nucleotide" : "Amino Acid";
            Dictionary<string, Dictionary<char, int>> dictsByProtease = new();
            List<char> aminoAcids = PeptidesByProtease.Values.SelectMany(p => p.SelectMany(peptide => peptide.BaseSequence)).Distinct().OrderBy(aa => aa).ToList();
            foreach (var protease in PeptidesByProtease)
            {
                // Pre-populate the count dictionary with zero for every known amino acid.
                Dictionary<char, int> aminoAcidCount = aminoAcids.ToDictionary(aa => aa, _ => 0);
                foreach (var peptide in protease.Value)
                {
                    // Single O(N) pass over the sequence instead of O(N×A) nested loops.
                    foreach (char c in peptide.BaseSequence)
                    {
                        if (aminoAcidCount.ContainsKey(c))
                            aminoAcidCount[c]++;
                    }
                }
                dictsByProtease.Add(protease.Key, aminoAcidCount);
            }

            // OxyPlot 2.2: BarSeries requires explicit axis keys
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                Title = xAxisTitle,
                GapWidth = 0.1,
                Key = "CategoryAxis"
            };
            foreach (var aa in aminoAcids)
            {
                categoryAxis.Labels.Add(aa.ToString());
            }

            var valueAxis = new LinearAxis
            {
                Title = yAxisTitle,
                Position = AxisPosition.Bottom,
                AbsoluteMinimum = 0,
                MinimumPadding = 0,
                Key = "ValueAxis"
            };

            // add axes
            privateModel.Axes.Add(categoryAxis);
            privateModel.Axes.Add(valueAxis);

            foreach (string key in dictsByProtease.Keys)
            {
                // OxyPlot 2.2: Must set axis keys on BarSeries
                var columns = new BarSeries
                {
                    BarWidth = 200,
                    IsStacked = false,
                    Title = key,
                    XAxisKey = "ValueAxis",
                    YAxisKey = "CategoryAxis"
                };

                foreach (var d in dictsByProtease[key])
                {
                    var column = new OxyPlot.Series.BarItem(d.Value);

                    columns.Items.Add(column);
                    if (DataTable.ContainsKey(d.Key.ToString()))
                    {
                        if (DataTable[d.Key.ToString()].ContainsKey(key))
                        {
                            DataTable[d.Key.ToString()][key] = d.Value.ToString();
                        }
                        else
                        {
                            DataTable[d.Key.ToString()].Add(key, d.Value.ToString());
                        }
                    }
                    else
                    {
                        var data = new Dictionary<string, string>();
                        foreach (var protease in dictsByProtease.Keys)
                        {
                            if (protease == key)
                            {
                                data.Add(key, d.Value.ToString());
                            }
                            else
                            {
                                data.Add(protease, "0");
                            }
                        }

                        DataTable.Add(d.Key.ToString(), data);
                    }
                }
                privateModel.Series.Add(columns);
            }
        }
        private void histogramPlot(int plotType)
        {
            // OxyPlot 2.2: Legend properties moved to separate Legend object
            var legend = new Legend
            {
                LegendTitle = $"{GlobalVariables.AnalyteType.GetDigestionAgentLabel()}",
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.BottomLeft,
                LegendFontSize = 12,
                LegendOrientation = LegendOrientation.Horizontal
            };
            privateModel.Legends.Add(legend);
            privateModel.TitleFontSize = 15;

            string yAxisTitle = "Count";
            string xAxisTitle = "";
            double binSize = -1;
            double labelAngle = 0;
            SortedList<double, double> numCategory = new SortedList<double, double>();
            Dictionary<string, IEnumerable<double>> numbersByProtease = new();
            // Keyed by int bin index to avoid repeated ToString/Parse round-trips.
            Dictionary<string, Dictionary<int, int>> dictsByProtease = new();

            switch (plotType)
            {
                case 1: // Peptide Length
                    xAxisTitle = $"{GlobalVariables.AnalyteType.GetUniqueFormLabel()} Length";
                    binSize = 1;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => Convert.ToDouble(p.Length)));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                    }
                    break;
                case 2: // Protein Sequence Coverage
                    xAxisTitle = $"{GlobalVariables.AnalyteType.GetBioPolymerLabel()} Sequence Coverage (%)";
                    binSize = 10;
                    foreach (string key in SequenceCoverageByProtease.Keys)
                    {
                        numbersByProtease.Add(key, SequenceCoverageByProtease[key].Select(p => p));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                    }
                    break;
                case 3: // Protein Sequence Coverage (unique peptides)
                    // The plot title already says "(Unique …s Only)"; repeating it here only overruns the axis.
                    xAxisTitle = $"{GlobalVariables.AnalyteType.GetBioPolymerLabel()} Sequence Coverage (%)";
                    binSize = 10;
                    foreach (string key in SequenceCoverageUniqueByProtease.Keys)
                    {
                        numbersByProtease.Add(key, SequenceCoverageUniqueByProtease[key].Select(p => p));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                    }
                    break;
                case 4: // Number of Unique Peptides per Protein
                    xAxisTitle = $"Number of Unique {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s per Protein";
                    binSize = 10;
                    double maxValue = 0;
                    double minValue = 0;
                    foreach (string key in UniquePeptidesPerProtein.Keys)
                    {
                        // DefaultIfEmpty guards against a protease with no (detectable) peptides.
                        double proteaseMax = UniquePeptidesPerProtein[key].DefaultIfEmpty(0).Max();
                        double proteaseMin = UniquePeptidesPerProtein[key].DefaultIfEmpty(0).Min();
                        if (maxValue < proteaseMax)
                        {
                            maxValue = proteaseMax;
                        }
                        if (minValue > proteaseMin)
                        {
                            minValue = proteaseMin;
                        }
                    }
                    binSize = Math.Max(1, Math.Round((maxValue - minValue) / 50, 0));

                    foreach (string key in UniquePeptidesPerProtein.Keys)
                    {
                        numbersByProtease.Add(key, UniquePeptidesPerProtein[key].Select(p => p));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                    }
                    break;
                case 5: // Predicted Peptide Hydrophobicity
                    xAxisTitle = $"Predicted {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Hydrophobicity";
                    binSize = 5;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => p.Hydrophobicity));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                    }
                    break;
                case 6: // Predicted Peptide Electrophoretic Mobility
                    xAxisTitle = $"Predicted {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Electrophoretic Mobility";
                    binSize = 0.005;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => p.ElectrophoreticMobility));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                    }
                    break;
                case 7: // Chronologer Predicted Retention Time
                    xAxisTitle = "Chronologer Predicted Retention Time";
                    binSize = 5;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        // Filter out failed predictions (value of -1)
                        var validPredictions = PeptidesByProtease[key]
                            .Where(p => p.ChronologerRetentionTime >= 0)
                            .Select(p => p.ChronologerRetentionTime);

                        if (validPredictions.Any())
                        {
                            numbersByProtease.Add(key, validPredictions);
                            var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                            dictsByProtease.Add(key, results.ToDictionary(p => p.Key, v => v.Count()));
                        }
                    }
                    break;
            }

            String[] category;
            int[] totalCounts;

            IEnumerable<double> allNumbers = numbersByProtease.Values.SelectMany(x => x);

            // Filtering (e.g. the detectable-only toggle) can leave nothing to plot; bail out
            // gracefully instead of throwing on Max()/Min() over an empty sequence.
            if (!allNumbers.Any())
            {
                NotificationService.Instance.AddNotification(
                    "No peptides match the current selection (the detectable-only filter removed all peptides).",
                    NotificationType.Information);
                return;
            }

            int end = roundToBin(allNumbers.Max(), binSize);
            int start = roundToBin(allNumbers.Min(), binSize);
            int numBins = end - start + 1;
            int minBinLabels = 15;
            int skipBinLabel = numBins < minBinLabels ? 1 : numBins / minBinLabels;

            var MaxValue = 0;
            category = new string[numBins];
            totalCounts = new int[numBins];
            for (int i = start; i <= end; i++)
            {
                if (i % skipBinLabel == 0)
                {
                    category[i - start] = Math.Round((i * binSize), 3).ToString(CultureInfo.InvariantCulture);
                }
                foreach (Dictionary<int, int> dict in dictsByProtease.Values)
                {
                    if (dict.TryGetValue(i, out int binCount))
                    {
                        totalCounts[i - start] += binCount;
                        if (totalCounts[i - start] > MaxValue)
                        {
                            MaxValue = totalCounts[i - start];
                        }
                    }
                }
            }

            // OxyPlot 2.2: BarSeries requires explicit axis keys
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                ItemsSource = category,
                Title = xAxisTitle,
                GapWidth = 0.1,
                Angle = labelAngle,
                Key = "CategoryAxis"
            };

            var valueAxis = new LinearAxis
            {
                Title = yAxisTitle,
                Position = AxisPosition.Bottom,
                AbsoluteMinimum = 0,
                Minimum = 0,
                Key = "ValueAxis"
            };

            privateModel.Axes.Add(categoryAxis);
            privateModel.Axes.Add(valueAxis);

            foreach (string key in dictsByProtease.Keys)
            {
                var column = new BarSeries
                {
                    BarWidth = 200,
                    IsStacked = false,
                    Title = key,
                    TrackerFormatString = "Bin: {bin}\n{0}: {2}\nTotal: {total}",
                    XAxisKey = "ValueAxis",
                    YAxisKey = "CategoryAxis"
                };
                foreach (var d in dictsByProtease[key])
                {
                    int bin = d.Key;
                    string binLabel = (bin * binSize).ToString(CultureInfo.InvariantCulture);
                    var hist = new HistItem(d.Value, bin - start, binLabel, totalCounts[bin - start]);
                    column.Items.Add(hist);
                    if (DataTable.ContainsKey(hist.bin))
                    {
                        if (DataTable[hist.bin].ContainsKey(key))
                        {
                            DataTable[hist.bin][key] = hist.Value.ToString();
                        }
                        else
                        {
                            DataTable[hist.bin].Add(key, hist.Value.ToString());
                        }
                    }
                    else
                    {
                        var data = new Dictionary<string, string>();
                        foreach (var protease in dictsByProtease.Keys)
                        {
                            if (protease == key)
                            {
                                data.Add(key, hist.Value.ToString());
                            }
                            else
                            {
                                data.Add(protease, "0");
                            }
                        }

                        DataTable.Add(hist.bin, data);
                    }
                }
                privateModel.Series.Add(column);
            }
        }

        //calculate the protein sequence coverage of each protein based on its digested peptides (for all peptides and unique peptides)
        private Dictionary<string, Dictionary<IBioPolymer, (double, double)>> CalculateProteinSequenceCoverage(Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>> peptidesByProtease)
        {
            Dictionary<string, Dictionary<IBioPolymer, (double, double)>> proteinSequenceCoverageByProtease = new();
            foreach (var protease in peptidesByProtease)
            {
                Dictionary<IBioPolymer, (double, double)> sequenceCoverages = new();
                foreach (var protein in protease.Value)
                {
                    //count which residues are covered at least one time by a peptide                    
                    HashSet<int> coveredOneBasesResiduesUnique = new HashSet<int>();
                    HashSet<int> coveredOneBasesResidues = new HashSet<int>();
                    var minPeptideList = protein.Value.ToHashSet();

                    foreach (var peptide in minPeptideList)
                    {
                        for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                        {
                            coveredOneBasesResidues.Add(i);
                            if (peptide.Unique == true)
                            {
                                coveredOneBasesResiduesUnique.Add(i);
                            }

                        }

                    }
                    //percent of the whole protein, so this agrees with DigestionTask and the reload path;
                    //the same field is fed from either source depending on the database selection below
                    double seqCoveragePercentUnique = (double)coveredOneBasesResiduesUnique.Count / protein.Key.Length * 100.0;
                    double seqCoveragePercent = (double)coveredOneBasesResidues.Count / protein.Key.Length * 100.0;

                    sequenceCoverages.Add(protein.Key, (Math.Round(seqCoveragePercent, 2), Math.Round(seqCoveragePercentUnique, 2)));
                }
                proteinSequenceCoverageByProtease.Add(protease.Key, sequenceCoverages);
            }

            return proteinSequenceCoverageByProtease;
        }

        // IPlotModel interface methods - OxyPlot 2.2 changed Render signature
        public void Update(bool updateData) { }
        public void Render(IRenderContext rc, OxyRect rect) { }
        public void AttachPlotView(IPlotView plotView) { }
    }
}
