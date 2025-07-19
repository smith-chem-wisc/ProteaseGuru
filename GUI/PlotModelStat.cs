using Engine;
using Tasks;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using Proteomics;
//using Easy.Common.Extensions;
using System.Windows;

namespace GUI
{
    // code for histogram generation
    public class PlotModelStat : INotifyPropertyChanged, IPlotModel
    {
        private PlotModel privateModel;
        private readonly List<InSilicoPep> AllPeptides = new List<InSilicoPep>();
        public Dictionary<string, List<InSilicoPep>> PeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
        public Dictionary<string, Dictionary<Protein, (double, double)>> SequenceCoverageByProtease_Return = new Dictionary<string, Dictionary<Protein, (double, double)>>();
        private readonly Dictionary<string, List<double>> SequenceCoverageByProtease = new Dictionary<string, List<double>>();
        private readonly Dictionary<string, List<double>> SequenceCoverageUniqueByProtease = new Dictionary<string, List<double>>();
        private readonly Dictionary<string, List<double>> UniquePeptidesPerProtein = new Dictionary<string, List<double>>();
        List<string> Proteases = new List<string>();
        //access series stuff here
        public Dictionary<string, Dictionary<string, string>> DataTable = new Dictionary<string, Dictionary<string, string>>();

        private static List<OxyColor> columnColors = new List<OxyColor>
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

        public PlotModelStat(string plotName, List<string> dbSelected, Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile, Parameters userParams, Dictionary<string, Dictionary<Protein, (double,double)>> sequenceCoverageByProtease)
        {
            privateModel = new PlotModel { Title = plotName, DefaultFontSize = 12 };

            Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> databasePeptides = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
            
            if (dbSelected.Count() > 1)
            {
                MessageBox.Show("Note: More than one protein database has been selected. Unique peptides are defined as being unique to a single protein in all selected databases.");

                List<InSilicoPep> allPeptides = new List<InSilicoPep>();

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

                Dictionary<string, List<InSilicoPep>> peptidesToProteins = new Dictionary<string, List<InSilicoPep>>();

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
                    var unique = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1 && p.Value.Select(p=>p.Database).Distinct().Count() ==1 ).ToDictionary(group => group.Key, group => group.Value);
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
                                        List<InSilicoPep> proteinSpecificPeptides = new List<InSilicoPep>();
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
                                        List<InSilicoPep> proteinSpecificPeptides = new List<InSilicoPep>();
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
                                Dictionary<Protein, List<InSilicoPep>> proteinDic = new Dictionary<Protein, List<InSilicoPep>>();
                                foreach (var prot in entry.Value)
                                {
                                    List<InSilicoPep> proteinSpecificPeptides = new List<InSilicoPep>();
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
                    SequenceCoverageByProtease_Return = CalculateProteinSequenceCoverage(databasePeptides);
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
                                        databasePeptides[entry.Key].Add(prot.Key, prot.Value);
                                    }
                                }
                            }
                            else
                            {
                                Dictionary<Protein, List<InSilicoPep>> proteinDic = new Dictionary<Protein, List<InSilicoPep>>();
                                foreach (var prot in entry.Value)
                                {
                                    List<InSilicoPep> proteinSpecificPeptides = new List<InSilicoPep>();                                    
                                    proteinDic.Add(prot.Key, prot.Value);
                                }
                                databasePeptides.Add(entry.Key, proteinDic);
                            }
                        }

                    }
                }
                SequenceCoverageByProtease_Return = CalculateProteinSequenceCoverage(databasePeptides);
            }
            else
            {
                MessageBox.Show("Note: One protein database has been selected. Unique peptides are defined as being unique to a single protein in this database.");
                databasePeptides = peptideByFile[dbSelected.FirstOrDefault()];
                SequenceCoverageByProtease_Return = CalculateProteinSequenceCoverage(databasePeptides);
            }

            List<InSilicoPep> peptides = new List<InSilicoPep>();
            Dictionary<string, List<InSilicoPep>> peptidesByProtease = new Dictionary<string, List<InSilicoPep>>();


            foreach (var protease in databasePeptides)
            {
                List<InSilicoPep> proteasePeptides = new List<InSilicoPep>();
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
                foreach (var protein in protease.Value.GroupBy(pep => pep.Protein).ToDictionary(group => group.Key, group => group.ToList()))
                {
                    uniquePeptides.Add(protein.Value.Where(pep => pep.Unique).Count());
                }
                UniquePeptidesPerProtein.Add(protease.Key, uniquePeptides);
            }
            createPlot(plotName);
            privateModel.DefaultColors = columnColors;
        }

        private void createPlot(string plotType)
        {
            if (plotType.Equals(" Peptide Length"))
            {
                histogramPlot(1);
            }
            else if (plotType.Equals(" Protein Sequence Coverage"))
            {
                histogramPlot(2);
            }
            else if (plotType.Equals(" Protein Sequence Coverage (Unique Peptides Only)"))
            {
                histogramPlot(3);
            }
            else if (plotType.Equals(" Number of Unique Peptides per Protein"))
            {
                histogramPlot(4);
            }
            else if (plotType.Equals(" Predicted Peptide Hydrophobicity"))
            {
                histogramPlot(5);
            }
            else if (plotType.Equals(" Predicted Peptide Electrophoretic Mobility"))
            {
                histogramPlot(6);
            }
            else if (plotType.Equals(" Amino Acid Distribution"))
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

        // used by histogram plots, gives additional properies for the tracker to display
        private class HistItem : ColumnItem
        {
            public int total { get; set; }
            public string bin { get; set; }
            public HistItem(double value, int categoryIndex, string bin, int total) : base(value, categoryIndex)
            {
                this.total = total;
                this.bin = bin;
            }
        }

        private class BarItem : ColumnItem
        {
            public int total { get; set; }
            public string label { get; set; }
            public BarItem(double value, int categoryIndex, string label, int total) : base(value, categoryIndex)
            {
                this.total = total;
                this.label = label;
            }
        }

        private void columnPlot()
        {
            privateModel.LegendTitle = "Protease";
            privateModel.LegendPlacement = LegendPlacement.Outside;
            privateModel.LegendPosition = LegendPosition.BottomLeft;
            privateModel.LegendItemAlignment = OxyPlot.HorizontalAlignment.Left;
            privateModel.LegendFontSize = 12;
            privateModel.TitleFontSize = 15;
            privateModel.LegendOrientation = LegendOrientation.Horizontal;

            string yAxisTitle = "Count";
            string xAxisTitle = "Amino Acid";
            Dictionary<string, Dictionary<char, int>> dictsByProtease = new Dictionary<string, Dictionary<char, int>>();
            List<char> aminoAcids = new List<char>() { 'A', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'K', 'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'V', 'W', 'Y' };
            foreach (var protease in PeptidesByProtease)
            {
                Dictionary<char, int> aminoAcidCount = new Dictionary<char, int>();
                foreach (var peptide in protease.Value)
                {
                    foreach (var aa in aminoAcids)
                    {
                        int aaCount = 0;
                        if (peptide.BaseSequence.Contains(aa))
                        {
                            aaCount = peptide.BaseSequence.Where(x => x == aa).Count();
                        }
                        if (aminoAcidCount.ContainsKey(aa))
                        {
                            aminoAcidCount[aa] += aaCount;
                        }
                        else
                        {
                            aminoAcidCount.Add(aa, aaCount);
                        }
                    }
                }
                dictsByProtease.Add(protease.Key, aminoAcidCount);
            }

            var categoryAxis = new CategoryAxis { Position = AxisPosition.Bottom, Title =xAxisTitle, GapWidth = .1 };
            foreach (var aa in aminoAcids)
            {
                categoryAxis.Labels.Add(aa.ToString());
            }

            // add axes
            privateModel.Axes.Add(categoryAxis);
            privateModel.Axes.Add(new LinearAxis { Title = yAxisTitle, Position = AxisPosition.Left, AbsoluteMinimum = 0 });
            privateModel.Axes[0].AbsoluteMaximum = privateModel.Axes[0].Maximum;            

            foreach (string key in dictsByProtease.Keys)
            {
                var columns = new ColumnSeries { ColumnWidth = 200, IsStacked = false, Title = key/*, TrackerFormatString = "Bin: {bin}\n{0}: {2}\nTotal: {total}" */};

                foreach (var d in dictsByProtease[key])
                {
                    var column = new ColumnItem(d.Value);
                    
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
            privateModel.LegendTitle = "Protease";
            privateModel.LegendPlacement = LegendPlacement.Outside;
            privateModel.LegendPosition = LegendPosition.BottomLeft;
            privateModel.LegendFontSize = 12;
            privateModel.TitleFontSize = 15;
            privateModel.LegendOrientation = LegendOrientation.Horizontal;
            string yAxisTitle = "Count";
            string xAxisTitle = "";
            double binSize = -1;
            double labelAngle = 0;
            SortedList<double, double> numCategory = new SortedList<double, double>();
            Dictionary<string, IEnumerable<double>> numbersByProtease = new Dictionary<string, IEnumerable<double>>();    // key is protease name, value is data from that protease
            Dictionary<string, Dictionary<string, int>> dictsByProtease = new Dictionary<string, Dictionary<string, int>>();   // key is protease name, value is dictionary of bins and their counts

            switch (plotType)
            {
                case 1: // Peptide Length
                    xAxisTitle = "Peptide Length";
                    binSize = 1;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => Convert.ToDouble(p.Length)));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case 2: // Protein Sequence Coverage
                    xAxisTitle = "Protein Sequence Coverage";
                    binSize = 0.10;
                    foreach (string key in SequenceCoverageByProtease.Keys)
                    {
                        numbersByProtease.Add(key, SequenceCoverageByProtease[key].Select(p => p));
                        var testList = numbersByProtease[key].Select(p => roundToBin(p, binSize)).ToList();                        
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p).ToList();
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case 3: // Protein Sequence Coverage (unique peptides)
                    xAxisTitle = "Protein Sequence Coverage (Unique Peptides Only)";
                    binSize = 0.10;
                    foreach (string key in SequenceCoverageUniqueByProtease.Keys)
                    {
                        numbersByProtease.Add(key, SequenceCoverageUniqueByProtease[key].Select(p => p));
                        var testList = numbersByProtease[key].Select(p => roundToBin(p, binSize)).ToList();
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p).ToList();
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case 4: // Predicted Peptide Hydrophobicity
                    xAxisTitle = "Number of Unique Peptides per Protein";
                    binSize = 10;
                    double maxValue = 0;
                    double minValue = 0;
                    foreach (string key in UniquePeptidesPerProtein.Keys)
                    {
                        if (maxValue < UniquePeptidesPerProtein[key].Max())
                        {
                            maxValue = UniquePeptidesPerProtein[key].Max();
                        }
                        if (minValue > UniquePeptidesPerProtein[key].Min())
                        {
                            minValue = UniquePeptidesPerProtein[key].Min();
                        }
                    }
                    binSize = Math.Round((maxValue - minValue) / 50, 0);

                    foreach (string key in UniquePeptidesPerProtein.Keys)
                    {
                        numbersByProtease.Add(key, UniquePeptidesPerProtein[key].Select(p => p));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case 5: // Predicted Peptide Hydrophobicity
                    xAxisTitle = "Predicted Peptide Hydrophobicity";
                    binSize = 5;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => p.Hydrophobicity));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case 6: // Predicted Peptide Electrophoretic Mobility
                    xAxisTitle = "Predicted Peptide Electrophoretic Mobility";
                    binSize = 0.005;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => p.ElectrophoreticMobility));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;               
            }

            String[] category;  // for labeling bottom axis
            int[] totalCounts;  // for having the tracker show total count across all files
            
            
            IEnumerable<double> allNumbers = numbersByProtease.Values.SelectMany(x => x);

            
            int end = roundToBin(allNumbers.Max(), binSize);  
            int start = roundToBin(allNumbers.Min(), binSize);                        
            int numBins = end - start + 1;
            int minBinLabels = 15;  // the number of labeled bins will be between minBinLabels and 2 * minBinLabels
            int skipBinLabel = numBins < minBinLabels ? 1 : numBins / minBinLabels;

            // assign axis labels, skip labels based on skipBinLabel, calculate bin totals across all files
            var MaxValue = 0;
            category = new string[numBins];
            totalCounts = new int[numBins];
            for (int i = start; i <= end; i++)
            {
                if (i % skipBinLabel == 0)
                {
                    category[i - start] = Math.Round((i * binSize), 3).ToString(CultureInfo.InvariantCulture);
                }
                foreach (Dictionary<string, int> dict in dictsByProtease.Values)
                {
                    totalCounts[i - start] += dict.ContainsKey(i.ToString(CultureInfo.InvariantCulture)) ? dict[i.ToString(CultureInfo.InvariantCulture)] : 0;
                    if (totalCounts[i - start] > MaxValue)
                    {
                        MaxValue = totalCounts[i - start];
                    }
                }
            }

            
                // add a column series for each protease
            foreach (string key in dictsByProtease.Keys)
            {
                var column = new ColumnSeries { ColumnWidth = 200, IsStacked = false, Title = key, TrackerFormatString = "Bin: {bin}\n{0}: {2}\nTotal: {total}" };
                foreach (var d in dictsByProtease[key])
                {                    
                    int bin = int.Parse(d.Key);
                    var hist = new HistItem(d.Value, bin - start, (bin * binSize).ToString(CultureInfo.InvariantCulture), totalCounts[bin - start]);
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

            // add axes
            privateModel.Axes.Add(new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = category,
                Title = xAxisTitle,
                GapWidth = .1,
                Angle = labelAngle,
            });
            privateModel.Axes.Add(new LinearAxis { Title = yAxisTitle, Position = AxisPosition.Left});
            privateModel.Axes[0].AbsoluteMaximum = privateModel.Axes[0].Maximum;
            privateModel.Axes[0].AbsoluteMinimum = privateModel.Axes[0].Minimum;
            privateModel.Axes[1].AbsoluteMaximum = MaxValue;
            privateModel.Axes[1].AbsoluteMinimum = 0;
            privateModel.Axes[1].Minimum = 0;
        }

        //calculate the protein seqeunce coverage of each protein based on its digested peptides (for all peptides and unique peptides)
        private Dictionary<string, Dictionary<Protein, (double, double)>> CalculateProteinSequenceCoverage(Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> peptidesByProtease )
        {
            Dictionary<string, Dictionary<Protein, (double, double)>> proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double, double)>>();
            foreach (var protease in peptidesByProtease)
            {
                Dictionary<Protein, (double, double)> sequenceCoverages = new Dictionary<Protein, (double, double)>();
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
                    //divide the number of covered residues by the total residues in the protein                    
                    double seqCoverageFractUnique = (double)coveredOneBasesResiduesUnique.Count / protein.Key.Length;
                    double seqCoverageFract = (double)coveredOneBasesResidues.Count / protein.Key.Length;

                    sequenceCoverages.Add(protein.Key, (Math.Round(seqCoverageFract, 3), Math.Round(seqCoverageFractUnique, 3)));
                }
                proteinSequenceCoverageByProtease.Add(protease.Key, sequenceCoverages);
            }

            return proteinSequenceCoverageByProtease;
        }       

        //unused interface methods
        public void Update(bool updateData) { }
        public void Render(IRenderContext rc, double width, double height) { }
        public void AttachPlotView(IPlotView plotView) { }
    }

}
