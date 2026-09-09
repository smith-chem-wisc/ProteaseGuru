using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Gui
{
    public partial class SpectralLibraryOptionsWindow : Window
    {
        public SpectralLibraryExportOptions ExportOptions { get; private set; }

        private CancellationTokenSource? _exportCts;
        private bool _isExporting;

        private ObservableCollection<string> _allProteases;
        private ObservableCollection<string> _allProteins;
        private ObservableCollection<string> _filteredProteins;
        private HashSet<string> _selectedProteins = new();
        private bool _isRefreshingProteinFilter;

        /// <summary>
        /// The peptides this export will draw from. Both the digestion results and the individual
        /// protein analyzer supply one, so the dialog does not care which produced them.
        /// </summary>
        public ISpectralLibraryPeptideSource Source { get; }

        /// <param name="source">Supplies the selectable proteases and proteins, and later the peptides</param>
        /// <param name="currentlySelectedProteases">Pre-selected in the list</param>
        public SpectralLibraryOptionsWindow(
            ISpectralLibraryPeptideSource source,
            List<string>? currentlySelectedProteases = null)
        {
            InitializeComponent();
            Source = source;

            // Initialize collections
            _allProteases = new ObservableCollection<string>(source.AvailableProteases.OrderBy(p => p));
            _allProteins = new ObservableCollection<string>(source.AvailableProteins.OrderBy(p => p));
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

            UpdateSummary();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_isExporting)
            {
                _exportCts?.Cancel();
                statusText.Text = "Cancelling after the current step...";
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            ExportOptions = BuildExportOptions();

            var saveDialog = new SaveFileDialog
            {
                Filter = ExportOptions.OutputFormat.FileFilter(),
                DefaultExt = ExportOptions.OutputFormat.Extension(),
                FileName = $"SpectralLibrary_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() != true)
            {
                return;
            }

            await RunExportAsync(saveDialog.FileName);
        }

        private async Task RunExportAsync(string outputPath)
        {
            BeginExport();
            try
            {
                IProgress<string> progress = new Progress<string>(message => statusText.Text = message);
                progress.Report("Gathering peptides...");

                // Gathering is a full digest for an on-demand source, so it runs off the UI thread
                // with the generator rather than freezing the window before the export appears to start.
                var spectra = await Task.Run(() =>
                {
                    var peptides = Source.GetPeptides(ExportOptions);
                    _exportCts!.Token.ThrowIfCancellationRequested();

                    if (peptides.Count == 0)
                    {
                        return null;
                    }

                    return new SpectralLibraryGenerator(peptides, ExportOptions, outputPath)
                        .GenerateLibrary(progress, _exportCts.Token);
                }, _exportCts!.Token);

                if (spectra == null)
                {
                    statusText.Text = "No peptides match the selected proteases and proteins.";
                    return;
                }

                NotificationService.Instance.AddNotification(
                    $"Spectral library generated with {spectra.Count} spectra. File saved to: {outputPath}",
                    NotificationType.Success);
                Close();
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "Export cancelled.";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Export failed: {ex.Message}";
                NotificationService.Instance.AddNotification(
                    $"Error generating spectral library: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                EndExport();
            }
        }

        private void BeginExport()
        {
            _isExporting = true;
            _exportCts?.Dispose();
            _exportCts = new CancellationTokenSource();
            settingsPanel.IsEnabled = false;
            btnExport.Content = "Cancel Export";
            btnCancel.IsEnabled = false;
            statusText.Text = string.Empty;
        }

        private void EndExport()
        {
            _isExporting = false;
            _exportCts?.Dispose();
            _exportCts = null;
            settingsPanel.IsEnabled = true;
            btnExport.Content = "Export";
            btnCancel.IsEnabled = true;
        }

        private SpectralLibraryExportOptions BuildExportOptions()
        {
            return new SpectralLibraryExportOptions
            {
                SelectedProteases = lbProteases.SelectedItems.Cast<string>().ToList(),
                SelectedProteins = _selectedProteins.ToList(),

                PredictionModel = Enum.Parse<FragmentIntensityPredictionModel>(((ComboBoxItem)cbFragmentModel.SelectedItem).Tag.ToString()!, ignoreCase: true),
                ChargeStates = GetSelectedChargeStates(),
                CollisionEnergy = int.Parse(tbCollisionEnergy.Text),

                ExcludeIncompatiblePeptides = cbExcludeIncompatiblePeptides.IsChecked == true,
                ExcludeUndetectablePeptides = cbExcludeUndetectablePeptides.IsChecked == true,
                DetectabilityThreshold = double.TryParse(tbDetectabilityThreshold.Text, out double detectabilityThreshold)
                    ? detectabilityThreshold
                    : 0.5,

                MinimumMZThreshold = double.TryParse(tbMinMzThreshold.Text, out double minMZ) ? minMZ : 200,

                MaximumMZThreshold = double.TryParse(tbMaxMzThreshold.Text, out double maxMZ) ? maxMZ : 2000,

                FilterByRelativeIntensity = cbEnableIntensityThresholdFiltering.IsChecked == true,
                // UI collects a percentage (0-100); convert to a fraction of the max intensity for filtering.
                RelativeIntensityThreshold = double.TryParse(tbRelIntThreshold.Text, out double intensityThreshold) ? intensityThreshold / 100.0 : 0,
                FilterByIntensityRank = cbEnableIntensityRankFiltering.IsChecked == true,
                IntensityRankThreshold = int.TryParse(tbRankThreshold.Text, out int rankThreshold) ? rankThreshold : -1, // -1 indicates keep all

                OutputFormat = Enum.Parse<SpectralLibraryFormat>(((ComboBoxItem)cbOutputFormat.SelectedItem).Tag.ToString()!, ignoreCase: true)
            };
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Cancellation lands at the next stage boundary, so the export may outlive the window
            // briefly; its progress reports are harmless once nobody is watching them.
            _exportCts?.Cancel();
            base.OnClosing(e);
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
                var result = MessageBox.Show("Please select at least one protein.", "No Protein Selected",
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

            // Validate minimum m/z does not exceed maximum m/z (otherwise every fragment is filtered out)
            if (double.TryParse(tbMinMzThreshold.Text, out double minMz) &&
                double.TryParse(tbMaxMzThreshold.Text, out double maxMz) &&
                minMz > maxMz)
            {
                MessageBox.Show("The minimum m/z threshold cannot be greater than the maximum m/z threshold.", "Invalid Input",
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

            if (cbExcludeUndetectablePeptides.IsChecked == true &&
                string.IsNullOrWhiteSpace(tbDetectabilityThreshold.Text))
            {
                MessageBox.Show("Please enter a valid detectability threshold.", "Invalid Input",
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
            // Prosit 2020 HCD accepts precursor charges 1-6; 7 is not offered because the model
            // rejects it, which the analyzer window used to hide by dropping it silently.
            var charges = new List<int>();
            if (cbCharge1.IsChecked == true) charges.Add(1);
            if (cbCharge2.IsChecked == true) charges.Add(2);
            if (cbCharge3.IsChecked == true) charges.Add(3);
            if (cbCharge4.IsChecked == true) charges.Add(4);
            if (cbCharge5.IsChecked == true) charges.Add(5);
            if (cbCharge6.IsChecked == true) charges.Add(6);
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
