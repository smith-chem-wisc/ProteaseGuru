using Engine;
using Tasks;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Proteomics.Fragmentation;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Globalization;

namespace GUI
{
    public class PlotModelStat : INotifyPropertyChanged, IPlotModel
    {
        private PlotModel privateModel;
        private readonly ObservableCollection<InSilicoPeptide> allPeptides;
        private readonly Dictionary<string, ObservableCollection<InSilicoPeptide>> PeptidesByProtease;

        private static List<OxyColor> columnColors = new List<OxyColor>
        {
            OxyColors.Teal, OxyColors.CadetBlue, OxyColors.LightSeaGreen, OxyColors.DarkTurquoise, OxyColors.LightSkyBlue,
            OxyColors.LightBlue, OxyColors.Aquamarine, OxyColors.PaleGreen, OxyColors.MediumAquamarine, OxyColors.DarkSeaGreen,
            OxyColors.MediumSeaGreen, OxyColors.SeaGreen, OxyColors.DarkSlateGray, OxyColors.Gray, OxyColors.Gainsboro

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

        public PlotModelStat(string plotName, ObservableCollection<InSilicoPeptide> peptides, Dictionary<string, ObservableCollection<InSilicoPeptide>> peptidesByProtease)
        {
            privateModel = new PlotModel { Title = plotName, DefaultFontSize = 14 };
            allPeptides = peptides;
            this.PeptidesByProtease = peptidesByProtease;
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
            else if (plotType.Equals(" Predicted Peptide Hydrophobicity"))
            {
                histogramPlot(3);
            }
            else if (plotType.Equals(" Predicted Peptide Electrophoretic Mobility"))
            {
                histogramPlot(4);
            }            
        }
        // returns a bin index of number relative to 0, midpoints are rounded towards zero
        private static int roundToBin(double number, double binSize)
        {
            int sign = number < 0 ? -1 : 1;
            double d = number * sign;
            double remainder = d % binSize;
            int i = remainder < 0.5 * binSize ? (int)(d / binSize + 0.001) : (int)(d / binSize + 1.001);
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
        private void histogramPlot(int plotType)
        {
            privateModel.LegendTitle = "Protease";
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
                    binSize = 0.1;
                    //more complicated implementation
                    break;
                case 3: // Predicted Peptide Hydrophobicity
                    xAxisTitle = "Predicted Peptide Hydrophobicity";
                    binSize = 0.5;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => p.GetHydrophobicity()));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case 4: // Predicted Peptide Electrophoretic Mobility
                    xAxisTitle = "Predicted Peptide Electrophoretic Mobility";
                    binSize = 0.5;
                    foreach (string key in PeptidesByProtease.Keys)
                    {
                        numbersByProtease.Add(key, PeptidesByProtease[key].Select(p => p.GetElectrophoreticMobility()));
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
            int minBinLabels = 22;  // the number of labeled bins will be between minBinLabels and 2 * minBinLabels
            int skipBinLabel = numBins < minBinLabels ? 1 : numBins / minBinLabels;

            // assign axis labels, skip labels based on skipBinLabel, calculate bin totals across all files
            category = new string[numBins];
            totalCounts = new int[numBins];
            for (int i = start; i <= end; i++)
            {
                if (i % skipBinLabel == 0)
                {
                    category[i - start] = Math.Round((i * binSize), 2).ToString(CultureInfo.InvariantCulture);
                }
                foreach (Dictionary<string, int> dict in dictsByProtease.Values)
                {
                    totalCounts[i - start] += dict.ContainsKey(i.ToString(CultureInfo.InvariantCulture)) ? dict[i.ToString(CultureInfo.InvariantCulture)] : 0;
                }
            }

                // add a column series for each file
            foreach (string key in dictsByProtease.Keys)
            {
                 var column = new ColumnSeries { ColumnWidth = 200, IsStacked = true, Title = key, TrackerFormatString = "Bin: {bin}\n{0}: {2}\nTotal: {total}" };
                foreach (var d in dictsByProtease[key])
                {
                    int bin = int.Parse(d.Key);
                    column.Items.Add(new HistItem(d.Value, bin - start, (bin * binSize).ToString(CultureInfo.InvariantCulture), totalCounts[bin - start]));
                }
                privateModel.Series.Add(column);
            }            

            // add axes
            privateModel.Axes.Add(new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = category,
                Title = xAxisTitle,
                GapWidth = 0.3,
                Angle = labelAngle,
            });
            privateModel.Axes.Add(new LinearAxis { Title = yAxisTitle, Position = AxisPosition.Left, AbsoluteMinimum = 0 });
        }
        //unused interface methods
        public void Update(bool updateData) { }
        public void Render(IRenderContext rc, double width, double height) { }
        public void AttachPlotView(IPlotView plotView) { }
    }

}
