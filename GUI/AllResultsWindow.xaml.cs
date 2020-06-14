using GUI;
using OxyPlot;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Tasks;


namespace ProteaseGuruGUI
{
    /// <summary>
    /// Interaction logic for AllResultsWindow.xaml
    /// </summary>
    public partial class AllResultsWindow : UserControl
    {
        private readonly ObservableCollection<ProteaseSummaryForTreeView> SummaryForTreeViewObservableCollection;
        private readonly ObservableCollection<string> listOfProteinDbs; 
        ICollectionView proteinDBView;
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;
        List<string> DBSelected;       

        public AllResultsWindow()
        {
        }

        public AllResultsWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile) // change constructor to receive analysis information
        {
            InitializeComponent();
            PeptideByFile = peptideByFile;
            listOfProteinDbs = new ObservableCollection<string>();
            DBSelected = new List<string>() { };
            SetUpDictionaries();
            SummaryForTreeViewObservableCollection = new ObservableCollection<ProteaseSummaryForTreeView>();
            GenerateResultsSummary();
            proteinDBView = CollectionViewSource.GetDefaultView(listOfProteinDbs);
            dataGridProteinDBs.DataContext = proteinDBView;
                      
        }

        private void SetUpDictionaries()
        {
            // populate list of protein DBs
            // populate summary for tree view for each db?

            foreach (var db in PeptideByFile.Keys)
            {
                listOfProteinDbs.Add(db);
            }

            // populate list of proteins
        }

        private void summaryProteinDB_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            OnDBSelectionChanged(); // update all results tabs
        }

        private void GenerateResultsSummary()
        {
            if (PeptideByFile.Count > 1) // if there is more than one database then we need to do all database summary 
            {
                ProteaseSummaryForTreeView allDatabases = new ProteaseSummaryForTreeView("Cumulative Database Results:");                
                Dictionary<string, List<InSilicoPep>> allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();              
                foreach (var database in PeptideByFile)
                {
                    foreach (var protease in database.Value)
                    {                        
                        if (allDatabasePeptidesByProtease.ContainsKey(protease.Key))
                        {
                            foreach (var protein in protease.Value)
                            {
                                allDatabasePeptidesByProtease[protease.Key].AddRange(protein.Value);
                            }
                        }
                        else
                        {                            
                            allDatabasePeptidesByProtease.Add(protease.Key, protease.Value.SelectMany(p=>p.Value).ToList());
                        }                       
                    }                        
                }

                foreach (var protease in allDatabasePeptidesByProtease)
                {
                    string prot = protease.Key;
                    DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                    var peptidesToProteins = protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                    List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();   
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count()));
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1).Count()));                  
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() > 1).Count()));                    
                    allDatabases.Summary.Add(thisDigestion);

                }

                SummaryForTreeViewObservableCollection.Add(allDatabases);
                foreach (var database in PeptideByFile)
                {
                    ProteaseSummaryForTreeView thisProtease = new ProteaseSummaryForTreeView(database.Key+ " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                        var allPeptides = protease.Value.SelectMany(p => p.Value);
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.BaseSequence).Distinct().Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.BaseSequence).Distinct().Count()));
                        thisProtease.Summary.Add(thisDigestion);
                    }
                    SummaryForTreeViewObservableCollection.Add(thisProtease);
                }
                
            }
            else // if there is only one database then is results and all database results are the same thing
            {
                foreach (var database in PeptideByFile)
                {
                    ProteaseSummaryForTreeView thisProtease = new ProteaseSummaryForTreeView(database.Key + " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView( prot + " Results:");                        
                        var  allPeptides = protease.Value.SelectMany(p => p.Value);                        
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p=>p.BaseSequence).Distinct().Count()));                        
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + allPeptides.Where(pep=>pep.Unique==true).Select(p=>p.BaseSequence).Distinct().Count()));                      
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.BaseSequence).Distinct().Count()));                        
                        thisProtease.Summary.Add(thisDigestion);
                    }
                    SummaryForTreeViewObservableCollection.Add(thisProtease);
                }
            }
            ProteaseSummaryTreeView.DataContext = SummaryForTreeViewObservableCollection;          
        }

        private void OnDBSelectionChanged()
        {
            DBSelected.Clear();
            var dbs = dataGridProteinDBs.SelectedItems;
            foreach (var db in dbs)
            {
                DBSelected.Add(db.ToString());
            }

            foreach (var db in DBSelected)
            {
                var databasePeptides = PeptideByFile[db];                
            }
            
            
        }
        

        private async void PlotSelected(object sender, SelectionChangedEventArgs e)
        {
            ObservableCollection<InSilicoPep> peptides = new ObservableCollection<InSilicoPep>();
            Dictionary<string, ObservableCollection<InSilicoPep>> peptidesByProtease = new Dictionary<string, ObservableCollection<InSilicoPep>>();
            Dictionary<string, Dictionary<Protein, double>> sequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, double>>();
            var selectedPlot = HistogramComboBox.SelectedItem;
            var objectName = selectedPlot.ToString().Split(':');
            var plotName = objectName[1];

            //var comboBox = HistogramComboBox as ComboBox;
            //var plotName = comboBox.SelectedItem as string;

            foreach (var db in DBSelected)
            {
                var PeptidesForAllProteases = PeptideByFile[db];
                sequenceCoverageByProtease = CalculateProteinSequenceCoverage(PeptidesForAllProteases);
                foreach (var protease in PeptidesForAllProteases)
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
            }            
            PlotModelStat plot = await Task.Run(() => new PlotModelStat(plotName, peptides, peptidesByProtease, sequenceCoverageByProtease));
            plotViewStat.DataContext = plot;            
        }


        private void CreatePlotPdf_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = HistogramComboBox.SelectedItem;

            if (selectedItem == null)
            {
                MessageBox.Show("Select a plot type to export!");
                return;
            }

            var plotName = selectedItem as string;
            //var fileDirectory = Directory.GetParent(tsvResultsFilePath).ToString();
            var fileDirectory = "";
            var fileName = String.Concat(plotName, ".pdf");

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

        private Dictionary<string, Dictionary<Protein, double>> CalculateProteinSequenceCoverage( Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> peptidesByProtease)
        {
            Dictionary<string, Dictionary<Protein, double>> proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, double>>();
            foreach (var protease in peptidesByProtease)
            {
                Dictionary<Protein, double> sequenceCoverages = new Dictionary<Protein, double>();
                foreach (var protein in protease.Value)
                {
                    HashSet<int> coveredOneBasesResidues = new HashSet<int>();
                    foreach (var peptide in protein.Value)
                    {
                        for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                        {
                            coveredOneBasesResidues.Add(i);
                        }
                    }

                    double seqCoverageFract = (double)coveredOneBasesResidues.Count / protein.Key.Length;
                    
                    sequenceCoverages.Add(protein.Key, seqCoverageFract);
                }
                proteinSequenceCoverageByProtease.Add(protease.Key, sequenceCoverages);
            }            

            return proteinSequenceCoverageByProtease;
        }
    }
}
