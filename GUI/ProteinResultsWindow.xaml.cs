using Proteomics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ProteaseGuruGUI
{
    /// <summary>
    /// Interaction logic for ProteinResultsWindow.xaml
    /// </summary>
    public partial class ProteinResultsWindow : UserControl
    {
        private readonly ObservableCollection<ProteinForTreeView> ProteinsForTreeViewObservableCollection;
        private readonly ObservableCollection<Protein> listOfProteins;
        ICollectionView proteinView;
        private Dictionary<string, string> proteinGroups; // <accession, seq coverage>

        public ProteinResultsWindow()
        {
            InitializeComponent();

            listOfProteins = new ObservableCollection<Protein>();
            proteinView = CollectionViewSource.GetDefaultView(listOfProteins);
            dataGridProteins.DataContext = proteinView;
            ProteinsForTreeViewObservableCollection = new ObservableCollection<ProteinForTreeView>();
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            string txt = (sender as TextBox).Text;
            if (txt == "")
            {
                proteinView.Filter = null;
            }
            else
            {
                proteinView.Filter = obj =>
                {
                    ProteinForTreeView prot = obj as ProteinForTreeView;
                    return ((prot.Accession.ToString()).StartsWith(txt) || prot.DisplayName.ToUpper().Contains(txt.ToUpper()));
                };
            }
        }

        private void proteins_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            OnSelectionChanged();
        }

        
        private void OnSelectionChanged()
        {
            var protein = (ProteinForTreeView)dataGridProteins.SelectedItem;

            // draw sequence coverage map
            DrawSequenceCoverageMap(protein);

        }

        private void DrawSequenceCoverageMap(ProteinForTreeView protein) //string accession, Dictionary<string, PeptideForTreeView> uniquePeptides, Dictionary<string, PeptideForTreeView> sharedPeptides)
        {
            // will need to borrow from metadraw
        }
    }
}
