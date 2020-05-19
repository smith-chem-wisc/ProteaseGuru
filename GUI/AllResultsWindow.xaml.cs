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
        private readonly ObservableCollection<string> listOfProteinDbs; // for now, use ProteinDBForDataGrid
        ICollectionView proteinDBView;
        private readonly Dictionary<string, Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>> PeptideByFile;

        public AllResultsWindow()
        {
        }

        public AllResultsWindow(Dictionary<string, Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>> peptideByFile) // change constructor to receive analysis information
        {
            InitializeComponent();
            PeptideByFile = peptideByFile;

            listOfProteinDbs = new ObservableCollection<string>();         
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
            OnSelectionChanged(); // update all results tabs
        }

        private void OnSelectionChanged()
        {
            //var db = (ProteinDbForDataGrid) dataGridProteinDBs.SelectedItem;

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
            var listview = sender as ListView;
            var plotName = listview.SelectedItem as string;
            
            // get psms from selected source files
            ObservableCollection<InSilicoPeptide> peptides = new ObservableCollection<InSilicoPeptide>();
            Dictionary<string, ObservableCollection<InSilicoPeptide>> peptidesByProtease = new Dictionary<string, ObservableCollection<InSilicoPeptide>>();

            //MM code for this section
            //need to update to get information from our task results to populate peptides

            //foreach (string fileName in selectSourceFileListBox.SelectedItems)
            //{
            //    psmsBSF.Add(fileName, psmsBySourceFile[fileName]);
            //    foreach (PsmFromTsv psm in psmsBySourceFile[fileName])
            //    {
            //        psms.Add(psm);
            //    }
            //}
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
