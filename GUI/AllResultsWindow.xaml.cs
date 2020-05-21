using GUI;
using OxyPlot;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
            proteinDBView = CollectionViewSource.GetDefaultView(listOfProteinDbs);
            dataGridProteinDBs.DataContext = proteinDBView;
            SummaryForTreeViewObservableCollection = new ObservableCollection<ProteaseSummaryForTreeView>();            
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
            

            // SUMMARY
            // match selected db with task results
            // change TotalNumberPeptides and TotalNumberDistinctPeptides
            // change data context for ProteaseSummaryForTreeView

            // dummy code for testing
            //if (db.FileName.Equals("02-15-17_YL-stnd_old-heat.mzML"))
            //{
            //    TotalNumberPeptides.Content = 100;
            //    TotalNumberDistinctPeptides.Content = 23;

            //    SummaryForTreeViewObservableCollection.Add(new ProteaseSummaryForTreeView("Trypsin"));
            //    SummaryForTreeViewObservableCollection.Add(new ProteaseSummaryForTreeView("Asp-N"));
            //    SummaryForTreeViewObservableCollection.Add(new ProteaseSummaryForTreeView("Arg-C"));

            //    foreach (var protease in SummaryForTreeViewObservableCollection)
            //    {
            //        protease.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: 30"));
            //        protease.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: 30"));
            //        protease.Summary.Add(new SummaryForTreeView("Average Protein Sequence Coverage: 0.609"));
            //    }

            //    ProteaseSummaryTreeView.DataContext = SummaryForTreeViewObservableCollection;
            //}

            // TODO PEPTIDE LENGTH DISTRIBUTION
            // TODO PROTEIN COVERAGE DISTRIBUTION
        }
        

        private async void PlotSelected(object sender, SelectionChangedEventArgs e)
        {
            ObservableCollection<InSilicoPeptide> peptides = new ObservableCollection<InSilicoPeptide>();
            Dictionary<string, ObservableCollection<InSilicoPeptide>> peptidesByProtease = new Dictionary<string, ObservableCollection<InSilicoPeptide>>();

            var selectedPlot = HistogramComboBox.SelectedItem;
            var objectName = selectedPlot.ToString().Split(':');
            var plotName = objectName[1];

            //var comboBox = HistogramComboBox as ComboBox;
            //var plotName = comboBox.SelectedItem as string;

            foreach (var db in DBSelected)
            {
                var PeptidesForAllProteases = PeptideByFile[db];
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
            PlotModelStat plot = await Task.Run(() => new PlotModelStat(plotName, peptides, peptidesByProtease));
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
    }
}
