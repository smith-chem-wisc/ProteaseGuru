using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MzLibUtil;
using Tasks;

namespace GUI
{
    public partial class SpectralLibraryOptionsWindow : Window
    {
        public SpectralLibraryExportOptions ExportOptions { get; private set; }
        public bool DialogResultOk { get; private set; }

        private ObservableCollection<string> _allProteases;
        private ObservableCollection<string> _allProteins;
        private ObservableCollection<string> _filteredProteins;

        /// <summary>
        /// Constructor for spectral library options window
        /// </summary>
        /// <param name="availableProteases">List of proteases available in the digestion results</param>
        /// <param name="availableProteins">List of protein accessions available in the digestion results</param>
        /// <param name="currentlySelectedProteases">Currently selected proteases in ProteinResultsWindow (will be pre-selected)</param>
        /// <param name="currentlySelectedProtein">Currently selected protein in ProteinResultsWindow (will be pre-selected)</param>
        public SpectralLibraryOptionsWindow(
            List<string> availableProteases,
            List<string> availableProteins,
            List<string> currentlySelectedProteases = null,
            string currentlySelectedProtein = null)
        {
            InitializeComponent();

            // Initialize collections
            _allProteases = new ObservableCollection<string>(availableProteases.OrderBy(p => p));
            _allProteins = new ObservableCollection<string>(availableProteins.OrderBy(p => p));
            _filteredProteins = new ObservableCollection<string>(_allProteins);

            // Populate ListBoxes
            lbProteases.ItemsSource = _allProteases;
            lbProteins.ItemsSource = _filteredProteins;

            // Pre-select current selections if provided
            if (currentlySelectedProteases != null && currentlySelectedProteases.Any())
            {
                foreach (var protease in currentlySelectedProteases)
                {
                    if (_allProteases.Contains(protease))
                    {
                        lbProteases.SelectedItems.Add(protease);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentlySelectedProtein) && _allProteins.Contains(currentlySelectedProtein))
            {
                lbProteins.SelectedItems.Add(currentlySelectedProtein);
            }

            UpdateSummary();
        }

        private void FragmentModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbFragmentModel.SelectedItem is ComboBoxItem selectedItem)
            {
                string modelTag = selectedItem?.Tag?.ToString();
                if (!string.IsNullOrEmpty(modelTag) && modelTag != "Prosit2020HCD")
                {
                    throw new NotImplementedException($"Model {modelTag ?? "null"} is not implemented yet. Only Prosit2020HCD is currently supported.");
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (!ValidateInputs())
            {
                return;
            }

            // Build export options
            ExportOptions = new SpectralLibraryExportOptions
            {
                SelectedProteases = lbProteases.SelectedItems.Cast<string>().ToList(),
                SelectedProteins = lbProteins.SelectedItems.Cast<string>().ToList(),
                PredictionModel = ((ComboBoxItem)cbFragmentModel.SelectedItem).Tag.ToString(),
                ChargeStates = GetSelectedChargeStates(),
                CollisionEnergy = int.Parse(tbCollisionEnergy.Text),
                ExcludeIncompatiblePeptides = cbExcludeIncompatiblePeptides.IsChecked == true,
                MinimumIntensityThreshold = double.Parse(tbIntensityThreshold.Text),
                OutputFormat = ((ComboBoxItem)cbOutputFormat.SelectedItem).Tag.ToString()
            };

            DialogResultOk = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResultOk = false;
            this.Close();
        }

        private bool ValidateInputs()
        {
            // Validate proteases selected
            if (lbProteases.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one protease.", "No Protease Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Note: No validation for proteins - if none selected, all will be used

            // Validate collision energy (IntegerTextBoxControl handles bounds, just check if empty)
            if (string.IsNullOrWhiteSpace(tbCollisionEnergy.Text))
            {
                MessageBox.Show("Please enter a valid collision energy.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Validate intensity threshold (DoubleTextBoxControl handles bounds, just check if empty)
            if (string.IsNullOrWhiteSpace(tbIntensityThreshold.Text))
            {
                MessageBox.Show("Please enter a valid minimum intensity threshold.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Validate at least one charge state selected
            if (!GetSelectedChargeStates().Any())
            {
                MessageBox.Show("Please select at least one charge state.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private List<int> GetSelectedChargeStates()
        {
            var charges = new List<int>();
            if (cbCharge2.IsChecked == true) charges.Add(2);
            if (cbCharge3.IsChecked == true) charges.Add(3);
            if (cbCharge4.IsChecked == true) charges.Add(4);
            return charges;
        }

        #region Protease Selection Handlers

        private void SelectAllProteases_Click(object sender, RoutedEventArgs e)
        {
            lbProteases.SelectAll();
            UpdateSummary();
        }

        private void ClearAllProteases_Click(object sender, RoutedEventArgs e)
        {
            lbProteases.SelectedItems.Clear();
            UpdateSummary();
        }

        private void Proteases_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSummary();
        }

        #endregion

        #region Protein Selection Handlers

        private void SelectAllProteins_Click(object sender, RoutedEventArgs e)
        {
            lbProteins.SelectAll();
            UpdateSummary();
        }

        private void ClearAllProteins_Click(object sender, RoutedEventArgs e)
        {
            lbProteins.SelectedItems.Clear();
            UpdateSummary();
        }

        private void Proteins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSummary();
        }

        private void ProteinSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = tbProteinSearch.Text;

            _filteredProteins.Clear();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Show all proteins
                foreach (var protein in _allProteins)
                {
                    _filteredProteins.Add(protein);
                }
            }
            else
            {
                // Filter proteins
                foreach (var protein in _allProteins)
                {
                    if (protein.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        _filteredProteins.Add(protein);
                    }
                }
            }
        }

        #endregion

        private void UpdateSummary()
        {
            runProteaseCount.Text = lbProteases.SelectedItems.Count.ToString();
            
            // Update protein count display - show "All" if none selected
            if (lbProteins.SelectedItems.Count == 0)
            {
                runProteinCount.Text = $"All ({_allProteins.Count})";
            }
            else
            {
                runProteinCount.Text = lbProteins.SelectedItems.Count.ToString();
            }
        }
    }
}
