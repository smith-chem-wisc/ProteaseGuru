using GUI;
using OxyPlot;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
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

        public HistogramWindow()
        {
        }

        public HistogramWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile, Parameters userParams) // change constructor to receive analysis information
        {
            InitializeComponent();
            PeptideByFile = peptideByFile;
            UserParams = userParams;
            listOfProteinDbs = new ObservableCollection<string>();
            DBSelected = new List<string>() { };
            SetUpDictionaries();                      
            proteinDBView = CollectionViewSource.GetDefaultView(listOfProteinDbs);
            dataGridProteinDBs.DataContext = proteinDBView;
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
            if (dataGridProteinDBs.SelectedItems == null)
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
            Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> databasePeptides = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
            //figure out which proteases should be used to make the plot
            if (dataGridProteinDBs.SelectedItems == null)
            {
                DBSelected.Add(listOfProteinDbs.First());
            }
            foreach (var db in DBSelected)
            {
                var pep = PeptideByFile[db];
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
                        databasePeptides.Add(entry.Key, entry.Value);
                    }
                }

            }

            ObservableCollection<InSilicoPep> peptides = new ObservableCollection<InSilicoPep>();
            Dictionary<string, ObservableCollection<InSilicoPep>> peptidesByProtease = new Dictionary<string, ObservableCollection<InSilicoPep>>();
            Dictionary<string, Dictionary<Protein, (double,double)>> sequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double,double)>>();
            //parse the GUI selection for interpretation here
            var selectedPlot = HistogramComboBox.SelectedItem;
            var objectName = selectedPlot.ToString().Split(':');
            var plotName = objectName[1];
            
            sequenceCoverageByProtease = CalculateProteinSequenceCoverage(databasePeptides);
            foreach (var protease in databasePeptides)
            {
                ObservableCollection<InSilicoPep> proteasePeptides = new ObservableCollection<InSilicoPep>();
                if (peptidesByProtease.ContainsKey(protease.Key))
                {
                    foreach (var protein in protease.Value)
                    {
                        foreach (var peptide in protein.Value)
                        {
                            proteasePeptides.Add(peptide);
                            peptides.Add(peptide);
                        }
                    }
                    peptidesByProtease[protease.Key] = proteasePeptides;
                }
                else
                {
                    foreach (var protein in protease.Value)
                    {
                        foreach (var peptide in protein.Value)
                        {
                            proteasePeptides.Add(peptide);
                            peptides.Add(peptide);
                        }
                    }
                    peptidesByProtease.Add(protease.Key, proteasePeptides);
                }
            }
            ProgressBar progressBar = new ProgressBar();
            progressBar.Orientation = Orientation.Horizontal;
            progressBar.Width = 200;
            progressBar.Height = 30;
            progressBar.IsIndeterminate = true;
            HistogramLoading.Items.Add(progressBar);
            //make the plot       
            PlotModelStat plot = await Task.Run(() => new PlotModelStat(plotName, peptides, peptidesByProtease, sequenceCoverageByProtease));
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
            Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> databasePeptides = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();

            if (dataGridProteinDBs.SelectedItems == null)
            {
                DBSelected.Add(listOfProteinDbs.First());
            }

            foreach (var db in DBSelected)
            {
                var pep = PeptideByFile[db.ToString()];
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
                        databasePeptides.Add(entry.Key, entry.Value);
                    }
                }

            }
            ObservableCollection<InSilicoPep> peptides = new ObservableCollection<InSilicoPep>();
            Dictionary<string, ObservableCollection<InSilicoPep>> peptidesByProtease = new Dictionary<string, ObservableCollection<InSilicoPep>>();
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double, double)>>();
            var selectedPlot = HistogramComboBox.SelectedItem;
            var objectName = selectedPlot.ToString().Split(':');
            var plotName = objectName[1];

            sequenceCoverageByProtease = CalculateProteinSequenceCoverage(databasePeptides);
            foreach (var protease in databasePeptides)
            {
                ObservableCollection<InSilicoPep> proteasePeptides = new ObservableCollection<InSilicoPep>();
                if (peptidesByProtease.ContainsKey(protease.Key))
                {
                    foreach (var protein in protease.Value)
                    {
                        foreach (var peptide in protein.Value)
                        {
                            proteasePeptides.Add(peptide);
                            peptides.Add(peptide);
                        }
                    }
                    peptidesByProtease[protease.Key] = proteasePeptides;
                }
                else
                {
                    foreach (var protein in protease.Value)
                    {
                        foreach (var peptide in protein.Value)
                        {
                            proteasePeptides.Add(peptide);
                            peptides.Add(peptide);
                        }
                    }
                    peptidesByProtease.Add(protease.Key, proteasePeptides);
                }
            }

            ProgressBar progressBar = new ProgressBar();
            progressBar.Orientation = Orientation.Horizontal;
            progressBar.Width = 200;
            progressBar.Height = 30;
            progressBar.IsIndeterminate = true;
            HistogramLoading.Items.Add(progressBar);
            //make the plot       
            PlotModelStat plot = await Task.Run(() => new PlotModelStat(plotName, peptides, peptidesByProtease, sequenceCoverageByProtease));
            progressBar.IsIndeterminate = false;
            //send the plot to GUI
            plotViewStat.DataContext = plot;
            //send the data table with plot info to GUI for export if desired
            HistogramDataTable = plot.DataTable;
            HistogramLoading.Items.Clear();
        }
        //create a data table with all of the information formt he histogram so useres can make their own plots using proteaseguru calculaitons
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
            var fileName = String.Concat(plotName[1],"_DataTable", ".csv");
            File.WriteAllText(Path.Combine(fileDirectory, fileName), dataTable);
            MessageBox.Show("Data table Created at " + Path.Combine(fileDirectory, fileName) + "!");

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
            MessageBox.Show("PDF Created at " + Path.Combine(fileDirectory, fileName) + "!");
        }
        //calculate the protein seqeunce coverage of each protein based on its digested peptides (for all peptides and unique peptides)
        private Dictionary<string, Dictionary<Protein, (double,double)>> CalculateProteinSequenceCoverage( Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> peptidesByProtease)
        {
            Dictionary<string, Dictionary<Protein, (double,double)>> proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double,double)>>();
            foreach (var protease in peptidesByProtease)
            {
                Dictionary<Protein, (double,double)> sequenceCoverages = new Dictionary<Protein, (double,double)>();
                foreach (var protein in protease.Value)
                {
                    //count which residues are covered at least one time by a peptide
                    HashSet<int> coveredOneBasesResidues = new HashSet<int>();
                    HashSet<int> coveredOneBasesResiduesUnique = new HashSet<int>();
                    foreach (var peptide in protein.Value)
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
                    double seqCoverageFract = (double)coveredOneBasesResidues.Count / protein.Key.Length;
                    double seqCoverageFractUnique = (double)coveredOneBasesResiduesUnique.Count / protein.Key.Length;

                    sequenceCoverages.Add(protein.Key, (seqCoverageFract,seqCoverageFractUnique));
                }
                proteinSequenceCoverageByProtease.Add(protease.Key, sequenceCoverages);
            }            

            return proteinSequenceCoverageByProtease;
        }
    }
}
