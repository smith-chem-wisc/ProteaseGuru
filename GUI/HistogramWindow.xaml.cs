using GUI;
using OxyPlot;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Tasks;


namespace ProteaseGuruGUI
{
    /// <summary>
    /// Interaction logic for HistogramWindow.xaml
    /// Users can interact witht heir data using histograms
    /// </summary>
    public partial class HistogramWindow : UserControl
    {        
        private readonly ObservableCollection<string> listOfProteinDbs; 
        ICollectionView proteinDBView;
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;
        List<string> DBSelected;
        Parameters UserParams;
        public Dictionary<string, Dictionary<string, string>> HistogramDataTable = new Dictionary<string, Dictionary<string, string>>();
        public string SelectedPlot;
        private Dictionary<string, List<InSilicoPep>> PeptidesByProtease;
        private Dictionary<string, Dictionary<Protein,(double,double)>> SequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double, double)>>();
        
        public HistogramWindow()
        {
        }

        public HistogramWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile, Parameters userParams, Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease) // change constructor to receive analysis information
        {
            InitializeComponent();
            PeptideByFile = peptideByFile;
            UserParams = userParams;
            listOfProteinDbs = new ObservableCollection<string>();
            DBSelected = new List<string>() { };
            SetUpDictionaries();                      
            proteinDBView = CollectionViewSource.GetDefaultView(listOfProteinDbs);
            dataGridProteinDBs.DataContext = proteinDBView;
            SequenceCoverageByProtease = sequenceCoverageByProtease;
        }

        //populate the database options for the user
        private void SetUpDictionaries()
        {
            // populate list of protein DBs
            foreach (var db in PeptideByFile.Keys)
            {
                listOfProteinDbs.Add(db);
            }
        }
        
        //saves the database selection of the user and gives that information to the code for plot generation
        private void ProteinDBSelected_Click(object sender, RoutedEventArgs e)
        {
            DBSelected.Clear();
            if (dataGridProteinDBs.SelectedItems.Count == 0)
            {
                DBSelected.Add(listOfProteinDbs.First());                
            }
            else 
            {
                var dbs = dataGridProteinDBs.SelectedItems;
                foreach (var db in dbs)
                {
                    DBSelected.Add(db.ToString());
                }
                if (HistogramComboBox.SelectedItem != null)
                {
                    RefreshPlot();
                }
            }          

        }

        private void DatabaseSelectionChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            DBSelected.Clear();
            if (dataGridProteinDBs.SelectedItems.Count == 0)
            {
                DBSelected.Add(listOfProteinDbs.First());
            }
            else
            {
                var dbs = dataGridProteinDBs.SelectedItems;
                foreach (var db in dbs)
                {
                    DBSelected.Add(db.ToString());
                }
                if (HistogramComboBox.SelectedItem != null)
                {
                    RefreshPlot();
                }
            }

        }
        
        //determine which histogram the user wants to make and what peptides should be used to make it
        private async void PlotSelected(object sender, SelectionChangedEventArgs e)
        {
            //clear the exportable data table when a new plot is selected
            HistogramDataTable.Clear();            
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease = SequenceCoverageByProtease;
            //figure out which proteases should be used to make the plot
            if (dataGridProteinDBs.SelectedItems.Count == 0)
            {
                DBSelected.Add(listOfProteinDbs.First());
            }

            //parse the GUI selection for interpretation here
            var selectedPlot = HistogramComboBox.SelectedItem;
            var objectName = selectedPlot.ToString().Split(':');
            var plotName = objectName[1];
            
            ProgressBar progressBar = new ProgressBar();
            progressBar.Orientation = Orientation.Horizontal;
            progressBar.Width = 200;
            progressBar.Height = 30;
            progressBar.IsIndeterminate = true;
            HistogramLoading.Items.Add(progressBar);            
           
            //make the plot       
            PlotModelStat plot = await Task.Run(() => new PlotModelStat(plotName, DBSelected, PeptideByFile,UserParams, sequenceCoverageByProtease));
            SelectedPlot = plotName;
            PeptidesByProtease = plot.PeptidesByProtease;
            SequenceCoverageByProtease = plot.SequenceCoverageByProtease_Return;
            progressBar.IsIndeterminate = false;
            //send the plot to GUI
            plotViewStat.DataContext = plot;
            //send the data table with plot info to GUI for export if desired
            HistogramDataTable = plot.DataTable;
            HistogramLoading.Items.Clear();
        }

        //if database selection is changed, refresh what is shown in the plot if that is not changed, similar code to PlotSelected(), but isnt triggered by user
        private async void RefreshPlot()
        {
            HistogramDataTable.Clear();
            var selectedPlot = HistogramComboBox.SelectedItem;
            var objectName = selectedPlot.ToString().Split(':');
            var plotName = objectName[1];            
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease = SequenceCoverageByProtease;
            if (dataGridProteinDBs.SelectedItems == null)
            {
                DBSelected.Add(listOfProteinDbs.First());
            }  
            ProgressBar progressBar = new ProgressBar();
            progressBar.Orientation = Orientation.Horizontal;
            progressBar.Width = 200;
            progressBar.Height = 30;
            progressBar.IsIndeterminate = true;
            HistogramLoading.Items.Add(progressBar);
            //make the plot       
            PlotModelStat plot = await Task.Run(() => new PlotModelStat(plotName, DBSelected, PeptideByFile, UserParams, sequenceCoverageByProtease));
            PeptidesByProtease = plot.PeptidesByProtease;
            SequenceCoverageByProtease = plot.SequenceCoverageByProtease_Return;
            SelectedPlot = plotName;
            progressBar.IsIndeterminate = false;
            //send the plot to GUI
            plotViewStat.DataContext = plot;
            //send the data table with plot info to GUI for export if desired
            HistogramDataTable = plot.DataTable;
            HistogramLoading.Items.Clear();
        }
        //create a data table with all of the information from the histogram so useres can make their own plots using proteaseguru calculaitons
        private void CreateTable_Click(object sender, RoutedEventArgs e)
        {
            DataTable table = new DataTable();
            table.Columns.Add("Bin Value", typeof(string));
            var proteaseList = HistogramDataTable.First().Value.Keys.ToList();
            foreach (var protease in proteaseList)
            {
                table.Columns.Add(protease, typeof(string));
            }
            foreach (var entry in HistogramDataTable)
            {
                string[] row = new string[proteaseList.Count()+1];
                int j = 0;
                row[j] = entry.Key;                
                foreach (var subentry in entry.Value)
                {
                    j++;
                    row[j] = subentry.Value;
                                        
                }
                table.Rows.Add(row);
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < table.Columns.Count; i++)
            {
                sb.Append(table.Columns[i]);
                if (i < table.Columns.Count - 1)
                    sb.Append(',');
            }
            sb.AppendLine();
            foreach (DataRow dr in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    sb.Append(dr[i].ToString());

                    if (i < table.Columns.Count - 1)
                        sb.Append(',');
                }
                sb.AppendLine();
            }

            var dataTable = sb.ToString();
            var plotName = HistogramComboBox.SelectedItem.ToString().Split(':');
            var fileDirectory = UserParams.OutputFolder;
            var fileName = String.Concat(plotName[1],"_HistogramDataTable", ".csv");
            File.WriteAllText(Path.Combine(fileDirectory, fileName), dataTable);            
            string message = "Data table Created at " + Path.Combine(fileDirectory, fileName) + "! Would you like to copy the file path?";
            var messageBox = MessageBox.Show(message, "", MessageBoxButton.YesNo);
            if (messageBox == MessageBoxResult.Yes)
            {
                Clipboard.SetText(Path.Combine(fileDirectory, fileName));
            }

            Dictionary<string, IEnumerable<double>> numbersByProtease = new Dictionary<string, IEnumerable<double>>();    // key is protease name, value is data from that protease
            Dictionary<string, Dictionary<string, int>> dictsByProtease = new Dictionary<string, Dictionary<string, int>>();   // key is protease name, value is dictionary of bins and their counts

            Dictionary<string, ObservableCollection<double>> UniquePeptidesPerProtein = new Dictionary<string, ObservableCollection<double>>();


            foreach (var protease in PeptidesByProtease)
            {
                ObservableCollection<double> uniquePeptides = new ObservableCollection<double>();
                foreach (var protein in protease.Value.GroupBy(pep => pep.Protein).ToDictionary(group => group.Key, group => group.ToList()))
                {
                    uniquePeptides.Add(protein.Value.Where(pep => pep.Unique).Count());
                }
                UniquePeptidesPerProtein.Add(protease.Key, uniquePeptides);
            }
            double binSize = 0;
            switch (SelectedPlot)
            {               
                case " Protein Sequence Coverage": // Protein Sequence Coverage                    
                    binSize = 0.01;
                    Dictionary<string, ObservableCollection<double>> sequenceCoverageByProtease = new Dictionary<string, ObservableCollection<double>>();
                    foreach (var protease in SequenceCoverageByProtease)
                    {
                            ObservableCollection<double> coverages = new ObservableCollection<double>();
                            ObservableCollection<double> uniqueCoverages = new ObservableCollection<double>();
                            foreach (var protein in protease.Value)
                            {
                                coverages.Add(protein.Value.Item1);
                                uniqueCoverages.Add(protein.Value.Item2);
                            }
                            sequenceCoverageByProtease.Add(protease.Key, coverages);                            
                    }
                    foreach (string key in sequenceCoverageByProtease.Keys)
                    {
                        numbersByProtease.Add(key, sequenceCoverageByProtease[key].Select(p => p));
                        var testList = numbersByProtease[key].Select(p => roundToBin(p, binSize)).ToList();
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p).ToList();
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case " Protein Sequence Coverage (Unique Peptides Only)": // Protein Sequence Coverage (unique peptides)                    
                    binSize = 0.01;
                    Dictionary<string, ObservableCollection<double>> sequenceCoverageUniqueByProtease = new Dictionary<string, ObservableCollection<double>>();
                    foreach (var protease in SequenceCoverageByProtease)
                    {
                            ObservableCollection<double> coverages = new ObservableCollection<double>();
                            ObservableCollection<double> uniqueCoverages = new ObservableCollection<double>();
                            foreach (var protein in protease.Value)
                            {
                                coverages.Add(protein.Value.Item1);
                                uniqueCoverages.Add(protein.Value.Item2);
                            }                            
                            sequenceCoverageUniqueByProtease.Add(protease.Key, uniqueCoverages);
                    }
                        foreach (string key in sequenceCoverageUniqueByProtease.Keys)
                    {
                        numbersByProtease.Add(key, sequenceCoverageUniqueByProtease[key].Select(p => p));
                        var testList = numbersByProtease[key].Select(p => roundToBin(p, binSize)).ToList();
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p).ToList();
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                case " Number of Unique Peptides per Protein":                    
                    binSize = 1;
                    foreach (string key in UniquePeptidesPerProtein.Keys)
                    {
                        numbersByProtease.Add(key, UniquePeptidesPerProtein[key].Select(p => p));
                        var results = numbersByProtease[key].GroupBy(p => roundToBin(p, binSize)).OrderBy(p => p.Key).Select(p => p);
                        dictsByProtease.Add(key, results.ToDictionary(p => p.Key.ToString(), v => v.Count()));
                    }
                    break;
                default:
                    break;
            }
            if (dictsByProtease.Count() != 0)
            {
                Dictionary<string, Dictionary<string, string>> detailedTable = new Dictionary<string, Dictionary<string, string>>();
                foreach (string key in dictsByProtease.Keys)
                {
                    foreach (var d in dictsByProtease[key])
                    {
                        int bin = int.Parse(d.Key);
                        var histBin = (bin * binSize).ToString(CultureInfo.InvariantCulture);
                        var histValue = d.Value.ToString();
                        if (detailedTable.ContainsKey(histBin))
                        {
                            if (detailedTable[histBin].ContainsKey(key))
                            {
                                detailedTable[histBin][key] = histValue;
                            }
                            else
                            {
                                detailedTable[histBin].Add(key, histValue);
                            }
                        }
                        else
                        {
                            var data = new Dictionary<string, string>();
                            foreach (var protease in dictsByProtease.Keys)
                            {
                                if (protease == key)
                                {
                                    data.Add(key, histValue);
                                }
                                else
                                {
                                    data.Add(protease, "0");
                                }
                            }

                            detailedTable.Add(histBin, data);
                        }

                    }
                }
                DataTable table2 = new DataTable();
                table2.Columns.Add("Bin Value", typeof(string));                
                foreach (var protease in proteaseList)
                {
                    table2.Columns.Add(protease, typeof(string));
                }
                foreach (var entry in detailedTable)
                {
                    string[] row = new string[proteaseList.Count() + 1];
                    int j = 0;
                    row[j] = entry.Key;
                    foreach (var subentry in entry.Value)
                    {
                        j++;
                        row[j] = subentry.Value;

                    }
                    table2.Rows.Add(row);
                }

                StringBuilder sb2 = new StringBuilder();
                for (int i = 0; i < table2.Columns.Count; i++)
                {
                    string columnName = table2.Columns[i].ColumnName;
                    sb2.Append(columnName);
                    if (i < table2.Columns.Count - 1)
                        sb2.Append(',');
                }
                sb2.AppendLine();
                foreach (DataRow dr in table2.Rows)
                {
                    for (int i = 0; i < table2.Columns.Count; i++)
                    {
                        sb2.Append(dr[i].ToString());

                        if (i < table2.Columns.Count - 1)
                            sb2.Append(',');
                    }
                    sb2.AppendLine();
                }

                var dataTable2 = sb2.ToString();
                var plotName2 = HistogramComboBox.SelectedItem.ToString().Split(':');
                var fileDirectory2 = UserParams.OutputFolder;
                var fileName2 = String.Concat(plotName[1], "_MetaData", ".csv");
                File.WriteAllText(Path.Combine(fileDirectory2, fileName2), dataTable2);
            }
           
        }
               
        private static int roundToBin(double number, double binSize)
        {
            int sign = number < 0 ? -1 : 1;
            double d = number * sign;
            double remainder = (d / binSize) - Math.Floor(d / binSize);
            int i = remainder != 0 ? (int)(Math.Ceiling(d / binSize)) : (int)(d / binSize);
            return i * sign;
        }

        //be able to expot the plots made as pdf files
        private void CreatePlotPdf_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = HistogramComboBox.SelectedItem;

            if (selectedItem == null)
            {
                MessageBox.Show("Select a plot type to export!");
                return;
            }

            var plotName = HistogramComboBox.SelectedItem.ToString().Split(':');
            var fileDirectory = UserParams.OutputFolder;            
            var fileName = String.Concat(plotName[1], ".pdf");

            // update font sizes to exported PDF's size
            double tmpW = plotViewStat.Width;
            double tmpH = plotViewStat.Height;
            plotViewStat.Width = 1000;
            plotViewStat.Height = 700;
            plotViewStat.UpdateLayout();            

            using (Stream writePDF = File.Create(Path.Combine(fileDirectory, fileName)))
            {
                PdfExporter.Export(plotViewStat.Model, writePDF, 1000, 700);
            }
            plotViewStat.Width = tmpW;
            plotViewStat.Height = tmpH;
            string message = "PDF Created at " + Path.Combine(fileDirectory, fileName) + "! Would you like to copy the file path?";
            var messageBox = MessageBox.Show(message, "", MessageBoxButton.YesNo);
            if (messageBox == MessageBoxResult.Yes)
            {
                Clipboard.SetText(Path.Combine(fileDirectory, fileName));
            }            
        }
       
    }
}
