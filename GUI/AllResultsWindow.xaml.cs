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
        private readonly Dictionary<string, Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>> PeptideByFile;
        List<string> DBSelected;       

        public AllResultsWindow()
        {
        }

        public AllResultsWindow(Dictionary<string, Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>> peptideByFile) // change constructor to receive analysis information
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
                Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>> allDatabasePeptidesByProtease = new Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>();                
                foreach (var database in PeptideByFile)
                {
                    foreach (var protease in database.Value)
                    {                        
                        if (allDatabasePeptidesByProtease.ContainsKey(protease.Key))
                        {
                            foreach (var protein in protease.Value)
                            {
                                if (allDatabasePeptidesByProtease[protease.Key].ContainsKey(protein.Key))
                                {
                                    allDatabasePeptidesByProtease[protease.Key][protein.Key].AddRange(protein.Value);
                                }
                                else 
                                {
                                    allDatabasePeptidesByProtease[protease.Key].Add(protein.Key, protein.Value);
                                }
                            }
                        }
                        else
                        {
                            allDatabasePeptidesByProtease.Add(protease.Key, protease.Value);
                        }                       
                    }                        
                }

                foreach (var protease in allDatabasePeptidesByProtease)
                {
                    string prot = protease.Key.Name;
                    DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");

                    Dictionary<string, (List<InSilicoPeptide>, HashSet<Protein>)> peptidesToProteins = new Dictionary<string, (List<InSilicoPeptide>, HashSet<Protein>)>();
                    List<InSilicoPeptide> allPeptides = new List<InSilicoPeptide>();                   
                    HashSet<string> distintPeptides = new HashSet<string>();
                    foreach (var protein in protease.Value)
                    {
                        foreach (var peptide in protein.Value)
                        {
                            if (peptidesToProteins.ContainsKey(peptide.BaseSequence))
                            {
                                peptidesToProteins[peptide.BaseSequence].Item1.Add(peptide);
                                peptidesToProteins[peptide.BaseSequence].Item2.Add(protein.Key);
                            }
                            else
                            {
                                peptidesToProteins.Add(peptide.BaseSequence, (new List<InSilicoPeptide>() { peptide }, new HashSet<Protein>() { protein.Key }));
                            }
                            allPeptides.Add(peptide);
                            distintPeptides.Add(peptide.BaseSequence);
                        }
                    }
                    var sharedPeptides = peptidesToProteins.Select(p => p.Value).Where(p => p.Item2.Count >= 2).Select(p => p.Item1).SelectMany(p => p).ToList();
                    var uniquePeptides = peptidesToProteins.Select(p => p.Value).Where(p => p.Item2.Count == 1).Select(p => p.Item1).SelectMany(p => p).ToList();

                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + distintPeptides.Count));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Length: " + Math.Round(allPeptides.Select(p => p.Length).ToList().Average(),2)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Hydrophobicity: " + Math.Round(allPeptides.Select(p => p.GetHydrophobicity()).ToList().Average(),2)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Electrophoretic Mobility: " + Math.Round(allPeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(),3)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + uniquePeptides.Count));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Length of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.Length).ToList().Average(),2)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Hydrophobicity of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.GetHydrophobicity()).ToList().Average(),2)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Electrophoretic Mobility of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(),3)));
                    HashSet<string> sp = new HashSet<string>();
                    foreach (var pep in sharedPeptides)
                    {
                        sp.Add(pep.BaseSequence);
                    }
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + sp.Count()));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Length of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.Length).ToList().Average(),2)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Hydrophobicity of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.GetHydrophobicity()).ToList().Average(),2)));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Average Electrophoretic Mobility of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(),3)));
                    allDatabases.Summary.Add(thisDigestion);

                }

                SummaryForTreeViewObservableCollection.Add(allDatabases);
                foreach (var database in PeptideByFile)
                {
                    ProteaseSummaryForTreeView thisProtease = new ProteaseSummaryForTreeView(database.Key+ " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key.Name;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                        List<InSilicoPeptide> allPeptides = new List<InSilicoPeptide>();
                        HashSet<string> distinctPeptides = new HashSet<string>();
                        List<InSilicoPeptide> sharedPeptides = new List<InSilicoPeptide>();
                        List<InSilicoPeptide> uniquePeptides = new List<InSilicoPeptide>();
                        foreach (var protein in protease.Value)
                        {
                            foreach (var peptide in protein.Value)
                            {
                                allPeptides.Add(peptide);
                                distinctPeptides.Add(peptide.BaseSequence);
                                if (peptide.GetUniquePeptide() == true)
                                {
                                    uniquePeptides.Add(peptide);
                                }
                                else 
                                {
                                    sharedPeptides.Add(peptide);
                                }
                            }
                        }
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + distinctPeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Length: " + Math.Round(allPeptides.Select(p => p.Length).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Hydrophobicity: " + Math.Round(allPeptides.Select(p => p.GetHydrophobicity()).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Electrophoretic Mobility: " + Math.Round(allPeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(), 3)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + uniquePeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Length of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.Length).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Hydrophobicity of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.GetHydrophobicity()).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Electrophoretic Mobility of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(), 3)));
                        HashSet<string> sp = new HashSet<string>();
                        foreach (var pep in sharedPeptides)
                        {
                            sp.Add(pep.BaseSequence);
                        }
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + sp.Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Length of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.Length).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Hydrophobicity of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.GetHydrophobicity()).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Electrophoretic Mobility of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(), 3)));
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
                        string prot = protease.Key.Name;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView( prot + " Results:");
                        List<InSilicoPeptide> allPeptides = new List<InSilicoPeptide>();
                        HashSet<string> distinctPeptides = new HashSet<string>();
                        List<InSilicoPeptide> sharedPeptides = new List<InSilicoPeptide>();
                        List<InSilicoPeptide> uniquePeptides = new List<InSilicoPeptide>();
                        foreach (var protein in protease.Value)
                        {
                            foreach (var peptide in protein.Value)
                            {
                                allPeptides.Add(peptide);
                                distinctPeptides.Add(peptide.BaseSequence);
                                if (peptide.GetUniquePeptide() == true)
                                {
                                    uniquePeptides.Add(peptide);
                                }
                                else
                                {
                                    sharedPeptides.Add(peptide);
                                }
                            }
                        }
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + distinctPeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Length: " + Math.Round(allPeptides.Select(p => p.Length).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Hydrophobicity: " + Math.Round(allPeptides.Select(p => p.GetHydrophobicity()).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Peptide Electrophoretic Mobility: " + Math.Round(allPeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(), 3)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + uniquePeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Length of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.Length).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Hydrophobicity of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.GetHydrophobicity()).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Electrophoretic Mobility of Unique Peptides: " + Math.Round(uniquePeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(), 3)));
                        HashSet<string> sp = new HashSet<string>();
                        foreach (var pep in sharedPeptides)
                        {
                            sp.Add(pep.BaseSequence);
                        }
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + sp.Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Length of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.Length).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Hydrophobicity of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.GetHydrophobicity()).ToList().Average(), 2)));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Average Electrophoretic Mobility of Shared Peptides: " + Math.Round(sharedPeptides.Select(p => p.GetElectrophoreticMobility()).ToList().Average(), 3)));
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
            ObservableCollection<InSilicoPeptide> peptides = new ObservableCollection<InSilicoPeptide>();
            Dictionary<string, ObservableCollection<InSilicoPeptide>> peptidesByProtease = new Dictionary<string, ObservableCollection<InSilicoPeptide>>();
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
                    ObservableCollection<InSilicoPeptide> proteasePeptides = new ObservableCollection<InSilicoPeptide>();
                    if (peptidesByProtease.ContainsKey(protease.Key.Name))
                    {
                        foreach (var protein in protease.Value)
                        {
                            foreach (var peptide in protein.Value)
                            {
                                proteasePeptides.Add(peptide);
                                peptides.Add(peptide);
                            }
                        }
                        peptidesByProtease[protease.Key.Name] = proteasePeptides;
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
                        peptidesByProtease.Add(protease.Key.Name, proteasePeptides);
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

        private Dictionary<string, Dictionary<Protein, double>> CalculateProteinSequenceCoverage( Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>> peptidesByProtease)
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
                        for (int i = peptide.OneBasedStartResidueInProtein; i <= peptide.OneBasedEndResidueInProtein; i++)
                        {
                            coveredOneBasesResidues.Add(i);
                        }
                    }

                    double seqCoverageFract = (double)coveredOneBasesResidues.Count / protein.Key.Length;
                    if (seqCoverageFract > 1)
                    {
                        bool stop = true;
                    }

                    sequenceCoverages.Add(protein.Key, seqCoverageFract);
                }
                proteinSequenceCoverageByProtease.Add(protease.Key.Name, sequenceCoverages);
            }            

            return proteinSequenceCoverageByProtease;
        }
    }
}
