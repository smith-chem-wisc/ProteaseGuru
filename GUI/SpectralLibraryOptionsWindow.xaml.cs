using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
        private HashSet<string> _selectedProteins = new();
        private bool _isRefreshingProteinFilter;

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
            List<string>? currentlySelectedProteases = null,
            string? currentlySelectedProtein = null)
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
                _selectedProteins.Add(currentlySelectedProtein);
                lbProteins.SelectedItems.Add(currentlySelectedProtein);
            }

            UpdateSummary();
        }

        private void FragmentModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbFragmentModel.SelectedItem is ComboBoxItem selectedItem)
            {
                string modelTag = selectedItem?.Tag?.ToString();
                if (!string.IsNullOrEmpty(modelTag) && modelTag != "Prosit2020IntensityHCD")
                {
                    throw new NotImplementedException($"Model {modelTag ?? "null"} is not implemented yet. Only Prosit2020IntensityHCD is currently supported.");
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
                SelectedProteins = _selectedProteins.ToList(),

                PredictionModel = ((ComboBoxItem)cbFragmentModel.SelectedItem).Tag.ToString(),
                ChargeStates = GetSelectedChargeStates(),
                CollisionEnergy = int.Parse(tbCollisionEnergy.Text),

                ExcludeIncompatiblePeptides = cbExcludeIncompatiblePeptides.IsChecked == true,
                ExcludeUndetectablePeptides = cbExcludeUndetectablePeptides.IsChecked == true,

                MinimumMZThreshold = double.TryParse(tbMinMzThreshold.Text, out double minMZ) ? minMZ : 200,

                MaximumMZThreshold = double.TryParse(tbMaxMzThreshold.Text, out double maxMZ) ? maxMZ : 2000,

                FilterByRelativeIntensity = cbEnableIntensityThresholdFiltering.IsChecked == true,
                // UI collects a percentage (0-100); convert to a fraction of the max intensity for filtering.
                RelativeIntensityThreshold = double.TryParse(tbRelIntThreshold.Text, out double intensityThreshold) ? intensityThreshold / 100.0 : 0,
                FilterByIntensityRank = cbEnableIntensityRankFiltering.IsChecked == true,
                IntensityRankThreshold = int.TryParse(tbRankThreshold.Text, out int rankThreshold) ? rankThreshold : -1, // -1 indicates keep all

                OutputFormat = ((ComboBoxItem)cbOutputFormat.SelectedItem).Tag.ToString()
            };

            DialogResultOk = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResultOk = false;
            Close();
        }

        private bool ValidateInputs()
        {
            // Validate proteases selected
            if (lbProteases.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one protease.", "No Protease Selected",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate proteins selected
            if (_selectedProteins.Count == 0)
            {
                var result = MessageBox.Show("Please select at least one protein." , "No Protein Selected",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate prediction model selected
            if (cbFragmentModel.SelectedItem == null)
            {
                MessageBox.Show("Please select a fragmentation model.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate at least one charge state selected
            if (!GetSelectedChargeStates().Any())
            {
                MessageBox.Show("Please select at least one charge state.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate collision energy (IntegerTextBoxControl handles bounds, just check if empty)
            if (string.IsNullOrWhiteSpace(tbCollisionEnergy.Text))
            {
                MessageBox.Show("Please enter a valid collision energy.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate m/z thresholds (DoubleTextBoxControl handles bounds, just check if empty)
            if (string.IsNullOrEmpty(tbMinMzThreshold.Text) || string.IsNullOrWhiteSpace(tbMaxMzThreshold.Text))
            {
                MessageBox.Show("Please enter valid m/z thresholds.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // NOTConverter ensures only one of the two intensity filtering options can be checked, so just check if either is checked and validate corresponding input

            // Validate intensity threshold if checked (DoubleTextBoxControl handles bounds, just check if empty)
            if (cbEnableIntensityThresholdFiltering.IsChecked == true && string.IsNullOrWhiteSpace(tbRelIntThreshold.Text))
            {
                MessageBox.Show("Please enter a valid minimum intensity threshold.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate intensity rank threshold if checked (IntegerTextBoxControl handles bounds, just check if empty)
            if (cbEnableIntensityRankFiltering.IsChecked == true && string.IsNullOrWhiteSpace(tbRankThreshold.Text))
            {
                MessageBox.Show("Please enter a valid intensity rank threshold.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Validate output format selected
            if (cbOutputFormat.SelectedItem == null)
            {
                MessageBox.Show("Please select an output format.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            _selectedProteins.Clear();

            foreach (var protein in _allProteins)
            {
                _selectedProteins.Add(protein);
            }

            RefreshProteinFilter();
        }

        private void ClearAllProteins_Click(object sender, RoutedEventArgs e)
        {
            _selectedProteins.Clear();
            RefreshProteinFilter();
        }

        private void Proteins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingProteinFilter)
            {
                return;
            }

            foreach (string added in e.AddedItems)
            {
                _selectedProteins.Add(added);
            }

            foreach (string removed in e.RemovedItems)
            {
                _selectedProteins.Remove(removed);
            }

            UpdateSummary();
        }

        private void ProteinSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshProteinFilter();
        }

        private void RefreshProteinFilter()
        {
            string searchText = tbProteinSearch.Text;

            _isRefreshingProteinFilter = true;

            try
            {
                _filteredProteins.Clear();
                lbProteins.SelectedItems.Clear();

                foreach (var protein in _allProteins)
                {
                    if (string.IsNullOrWhiteSpace(searchText) ||
                        protein.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        _filteredProteins.Add(protein);
                    }
                }

                foreach (var protein in _filteredProteins)
                {
                    if (_selectedProteins.Contains(protein))
                    {
                        lbProteins.SelectedItems.Add(protein);
                    }
                }
            }
            finally
            {
                _isRefreshingProteinFilter = false;
            }

            UpdateSummary();
        }

        #endregion

        private void UpdateSummary()
        {
            runProteaseCount.Text = lbProteases.SelectedItems.Count.ToString();

            // Show "All" if none selected
            if (_selectedProteins.Count == _allProteins.Count)
            {
                runProteinCount.Text = $"All ({_allProteins.Count})";
            }
            else
            {
                runProteinCount.Text = _selectedProteins.Count.ToString();
            }
        }
    }
}
