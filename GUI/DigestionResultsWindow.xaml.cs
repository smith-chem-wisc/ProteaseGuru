using GUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ProteaseGuruGUI
{
    /// <summary>
    /// Interaction logic for DigestionResultsWindow.xaml
    /// </summary>
    public partial class DigestionResultsWindow : UserControl
    {
        private readonly ObservableCollection<ProteaseSummaryForTreeView> SummaryForTreeViewObservableCollection;
        private readonly ObservableCollection<ProteinDbForDataGrid> listOfProteinDbs; // for now, use ProteinDBForDataGrid
        ICollectionView proteinDBView;

        public DigestionResultsWindow() // change constructor to receive analysis information
        {
            InitializeComponent();
            listOfProteinDbs = new ObservableCollection<ProteinDbForDataGrid>();
            proteinDBView = CollectionViewSource.GetDefaultView(listOfProteinDbs);
            dataGridProteinDBs.DataContext = proteinDBView;
            SummaryForTreeViewObservableCollection = new ObservableCollection<ProteaseSummaryForTreeView>();

            SetUpDictionaries();
        }

        private void SetUpDictionaries()
        {
            // populate list of protein DBs
            // populate summary for tree view for each db?

            var pd = new ProteinDbForDataGrid("C:/Users/khair/Downloads/ProteinML/02-15-17_YL-stnd_old-heat.mzML");
            listOfProteinDbs.Add(pd);
        }

        private void summaryProteinDB_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            var db = (ProteinDbForDataGrid) dataGridProteinDBs.SelectedItem;

            // match selected db with task results
            // change TotalNumberPeptides and TotalNumberDistinctPeptides
            // change data context for ProteaseSummaryForTreeView

            // dummy code for testing
            if (db.FileName.Equals("02-15-17_YL-stnd_old-heat.mzML"))
            {
                TotalNumberPeptides.Content = 100;
                TotalNumberDistinctPeptides.Content = 23;

                SummaryForTreeViewObservableCollection.Add(new ProteaseSummaryForTreeView("Trypsin"));
                SummaryForTreeViewObservableCollection.Add(new ProteaseSummaryForTreeView("Asp-N"));
                SummaryForTreeViewObservableCollection.Add(new ProteaseSummaryForTreeView("Arg-C"));

                foreach (var protease in SummaryForTreeViewObservableCollection)
                {
                    protease.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: 30"));
                    protease.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: 30"));
                    protease.Summary.Add(new SummaryForTreeView("Average Protein Sequence Coverage: 0.609"));
                }

                ProteaseSummaryTreeView.DataContext = SummaryForTreeViewObservableCollection;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }

    public class ProteaseSummaryForTreeView
    {
        public ProteaseSummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
            Summary = new ObservableCollection<SummaryForTreeView>();
            Expanded = true;
        }

        public string DisplayName { get; }

        public ObservableCollection<SummaryForTreeView> Summary { get; }

        public bool Expanded { get; set; }

    }

    public class SummaryForTreeView
    {
        public SummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
