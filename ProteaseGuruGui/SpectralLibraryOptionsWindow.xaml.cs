using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Omics.SequenceConversion;
using PredictionClients.Koina.Util;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Gui
{
    public partial class SpectralLibraryOptionsWindow : Window
    {
        private SpectralLibraryExportOptions _exportOptions = new();

        private CancellationTokenSource? _exportCts;
        private bool _isExporting;

        private ObservableCollection<string> _allProteases;
        private ObservableCollection<string> _allProteins;
        private ObservableCollection<string> _filteredProteins;
        private HashSet<string> _selectedProteins = new();
        private bool _isRefreshingProteinFilter;
        private FragmentIntensityInputOptions? _fragmentInputOptions;

        /// <summary>
        /// The peptides this export will draw from. Both the digestion results and the individual
        /// protein analyzer supply one, so the dialog does not care which produced them.
        /// </summary>
        private readonly ISpectralLibraryPeptideSource _source;

        /// <param name="source">Supplies the selectable proteases and proteins, and later the peptides</param>
        /// <param name="currentlySelectedProteases">Pre-selected in the list</param>
        public SpectralLibraryOptionsWindow(
            ISpectralLibraryPeptideSource source,
            List<string>? currentlySelectedProteases = null)
        {
            InitializeComponent();
            _source = source;
            InitializeModelSelectors();

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

            _exportOptions = BuildExportOptions();

            var saveDialog = new SaveFileDialog
            {
                Filter = _exportOptions.OutputFormat.FileFilter(),
                DefaultExt = _exportOptions.OutputFormat.Extension(),
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
                    var peptides = _source.GetPeptides(_exportOptions, progress, _exportCts!.Token);

                    if (peptides.Count == 0)
                    {
                        return null;
                    }

                    return new SpectralLibraryGenerator(peptides, _exportOptions, outputPath)
                        .GenerateLibrary(progress, _exportCts.Token);
                }, _exportCts!.Token);

                if (spectra == null)
                {
                    statusText.Text = "No peptides to export. Check the protease and protein selection, and the detectability threshold if that filter is on.";
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

                FragmentIntensityModel = ((FragmentIntensityModelDefinition)cbFragmentModel.SelectedItem).Id,
                RetentionTimeModel = ((RetentionTimeModelDefinition)cbRetentionTimeModel.SelectedItem).Id,
                ChargeStates = GetSelectedChargeStates(),
                CollisionEnergy = GetCollisionEnergy(),
                InstrumentType = GetStringInput(instrumentTypePanel, cbInstrumentType),
                FragmentationType = GetStringInput(fragmentationTypePanel, cbFragmentationType),

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
                IntensityRankThreshold = int.TryParse(tbRankThreshold.Text, out int rankThreshold) ? rankThreshold : null,

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
                MessageBox.Show("Please select at least one protein.", "No Protein Selected",
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

            if (cbRetentionTimeModel.SelectedItem == null)
            {
                MessageBox.Show("Please select a retention time model.", "Invalid Input",
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

            if (_fragmentInputOptions == null)
            {
                MessageBox.Show("The selected model's input options could not be loaded.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!ValidateCollisionEnergy(_fragmentInputOptions.CollisionEnergies) ||
                !ValidateStringInput("instrument type", instrumentTypePanel, cbInstrumentType,
                    _fragmentInputOptions.InstrumentTypes) ||
                !ValidateStringInput("fragmentation type", fragmentationTypePanel, cbFragmentationType,
                    _fragmentInputOptions.FragmentationTypes))
            {
                return false;
            }

            if (!RequireNumber(tbMinMzThreshold.Text, "minimum m/z threshold", 0, double.MaxValue) ||
                !RequireNumber(tbMaxMzThreshold.Text, "maximum m/z threshold", 0, double.MaxValue))
            {
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

            if (cbEnableIntensityThresholdFiltering.IsChecked == true &&
                !RequireNumber(tbRelIntThreshold.Text, "minimum intensity threshold", 0, 100))
            {
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
                !RequireNumber(tbDetectabilityThreshold.Text, "detectability threshold", 0, 1))
            {
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

        /// <summary>
        /// A decimal box can hold text that is neither empty nor a number -- a bare "." passes the
        /// control's input filter, and its clamp does nothing when parsing fails.
        /// </summary>
        private static bool RequireNumber(string text, string fieldName, double minimum, double maximum)
        {
            if (!double.TryParse(text, out double value))
            {
                MessageBox.Show($"Please enter a number for the {fieldName}.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (value < minimum || value > maximum)
            {
                MessageBox.Show($"The {fieldName} must be between {minimum} and {maximum}.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private List<int> GetSelectedChargeStates()
        {
            return chargeStateChoices.Children
                .OfType<CheckBox>()
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (int)checkBox.Tag)
                .OrderBy(charge => charge)
                .ToList();
        }

        private void InitializeModelSelectors()
        {
            cbRetentionTimeModel.ItemsSource = KoinaModelCatalog.RetentionTimeModels;
            cbFragmentModel.ItemsSource = KoinaModelCatalog.FragmentIntensityModels;

            cbRetentionTimeModel.SelectedItem = KoinaModelCatalog.RetentionTime(
                RetentionTimePredictionModel.ChronologerRt);
            cbFragmentModel.SelectedItem = KoinaModelCatalog.FragmentIntensity(
                FragmentIntensityPredictionModel.Prosit2020IntensityHcd);
        }

        private void RetentionTimeModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbRetentionTimeModel.SelectedItem is not RetentionTimeModelDefinition definition)
            {
                tbRetentionTimeModelSummary.Text = string.Empty;
                return;
            }

            using var model = definition.Create(SequenceConversionHandlingMode.ReturnNull);
            string scale = model.IsIndexedRetentionTimeModel ? "indexed retention time (iRT)" : "retention time";
            tbRetentionTimeModelSummary.Text =
                $"{model.ModelName} predicts {scale} for peptides of {model.MinPeptideLength}-{model.MaxPeptideLength} " +
                $"canonical residues; {DescribeAllowedModifications(model.AllowedUnimodIds)}.";
            AppendInputScopeNote(tbRetentionTimeModelSummary, definition.InputScopeNote);
        }

        private void FragmentModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbFragmentModel.SelectedItem is not FragmentIntensityModelDefinition definition)
            {
                _fragmentInputOptions = null;
                tbFragmentModelSummary.Text = string.Empty;
                return;
            }

            var previousCharges = GetSelectedChargeStates();
            int? previousCollisionEnergy = GetCollisionEnergy();
            string? previousInstrument = GetStringInput(instrumentTypePanel, cbInstrumentType);
            string? previousFragmentation = GetStringInput(fragmentationTypePanel, cbFragmentationType);

            var model = definition.Create(
                SequenceConversionHandlingMode.ReturnNull,
                IncompatibleParameterHandlingMode.ReturnNull,
                FragmentIonMappingMode.MapToInputFullSequence);
            _fragmentInputOptions = FragmentIntensityInputOptions.From(model);

            tbFragmentModelSummary.Text =
                $"{model.ModelName} accepts peptides of {model.MinPeptideLength}-{model.MaxPeptideLength} " +
                $"canonical residues; {DescribeAllowedModifications(model.AllowedUnimodIds)}.";
            AppendInputScopeNote(tbFragmentModelSummary, definition.InputScopeNote);

            PopulateChargeStates(_fragmentInputOptions.AllowedPrecursorCharges, previousCharges);
            ConfigureCollisionEnergy(_fragmentInputOptions.CollisionEnergies, previousCollisionEnergy);
            ConfigureStringInput(instrumentTypePanel, cbInstrumentType,
                _fragmentInputOptions.InstrumentTypes, previousInstrument, "LUMOS", "QE", "NONE");
            ConfigureStringInput(fragmentationTypePanel, cbFragmentationType,
                _fragmentInputOptions.FragmentationTypes, previousFragmentation, "HCD", "CID");
        }

        private static void AppendInputScopeNote(TextBlock summary, string? inputScopeNote)
        {
            if (!string.IsNullOrWhiteSpace(inputScopeNote))
            {
                summary.Text += Environment.NewLine + inputScopeNote;
            }
        }

        private void PopulateChargeStates(IReadOnlyList<int> allowedCharges, IReadOnlyCollection<int> previousCharges)
        {
            chargeStateChoices.Children.Clear();

            var selected = previousCharges.Where(allowedCharges.Contains).ToHashSet();
            if (selected.Count == 0)
            {
                selected.UnionWith(new[] { 2, 3 }.Where(allowedCharges.Contains));
                if (selected.Count == 0 && allowedCharges.Count > 0)
                    selected.Add(allowedCharges[0]);
            }

            foreach (int charge in allowedCharges)
            {
                chargeStateChoices.Children.Add(new CheckBox
                {
                    Content = $"{charge}+",
                    Tag = charge,
                    IsChecked = selected.Contains(charge),
                    Margin = new Thickness(5),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }

        private void ConfigureCollisionEnergy(KoinaInputDomain<int> domain, int? previousValue)
        {
            collisionEnergyPanel.Visibility = domain.IsApplicable ? Visibility.Visible : Visibility.Collapsed;
            if (!domain.IsApplicable)
            {
                cbCollisionEnergy.ItemsSource = null;
                return;
            }

            tbCollisionEnergy.Visibility = domain.IsRestricted ? Visibility.Collapsed : Visibility.Visible;
            cbCollisionEnergy.Visibility = domain.IsRestricted ? Visibility.Visible : Visibility.Collapsed;

            if (!domain.IsRestricted)
            {
                tbCollisionEnergy.Text = (previousValue ?? 30).ToString();
                return;
            }

            var allowed = domain.AllowedValues.OrderBy(value => value).ToArray();
            cbCollisionEnergy.ItemsSource = allowed;
            cbCollisionEnergy.SelectedItem = previousValue is { } value && allowed.Contains(value)
                ? value
                : allowed.Contains(30) ? 30 : allowed[0];
        }

        private static void ConfigureStringInput(
            StackPanel panel,
            ComboBox comboBox,
            KoinaInputDomain<string> domain,
            string? previousValue,
            params string[] preferredValues)
        {
            panel.Visibility = domain.IsApplicable ? Visibility.Visible : Visibility.Collapsed;
            comboBox.ItemsSource = null;
            comboBox.IsEditable = domain.IsApplicable && !domain.IsRestricted;
            comboBox.Text = string.Empty;

            if (!domain.IsApplicable)
                return;

            if (!domain.IsRestricted)
            {
                comboBox.Text = previousValue ?? string.Empty;
                return;
            }

            var allowed = domain.AllowedValues.OrderBy(value => value).ToArray();
            comboBox.ItemsSource = allowed;

            string? selected = allowed.FirstOrDefault(value =>
                string.Equals(value, previousValue, StringComparison.OrdinalIgnoreCase));
            selected ??= preferredValues
                .Select(preferred => allowed.FirstOrDefault(value =>
                    string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(value => value != null);
            comboBox.SelectedItem = selected ?? allowed[0];
        }

        private int? GetCollisionEnergy()
        {
            if (collisionEnergyPanel.Visibility != Visibility.Visible)
                return null;

            if (cbCollisionEnergy.Visibility == Visibility.Visible)
                return cbCollisionEnergy.SelectedItem is int value ? value : null;

            return int.TryParse(tbCollisionEnergy.Text, out int parsed) ? parsed : null;
        }

        private static string? GetStringInput(StackPanel panel, ComboBox comboBox)
        {
            if (panel.Visibility != Visibility.Visible)
                return null;

            string? value = comboBox.IsEditable ? comboBox.Text : comboBox.SelectedItem as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private bool ValidateCollisionEnergy(KoinaInputDomain<int> domain)
        {
            if (!domain.IsApplicable)
                return true;

            int? value = GetCollisionEnergy();
            if (value == null)
            {
                MessageBox.Show("Please enter a valid collision energy.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (domain.IsRestricted && !domain.AllowedValues.Contains(value.Value))
            {
                MessageBox.Show("Please select a collision energy supported by the model.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private static bool ValidateStringInput(
            string fieldName,
            StackPanel panel,
            ComboBox comboBox,
            KoinaInputDomain<string> domain)
        {
            if (!domain.IsApplicable)
                return true;

            string? value = GetStringInput(panel, comboBox);
            if (value == null)
            {
                MessageBox.Show($"Please enter a valid {fieldName}.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (domain.IsRestricted && !domain.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show($"Please select a {fieldName} supported by the model.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private static string DescribeAllowedModifications(IReadOnlySet<int> allowedUnimodIds) =>
            allowedUnimodIds.Count == 0
                ? "accepts all UNIMOD modifications"
                : $"supports UNIMOD {string.Join(", ", allowedUnimodIds.OrderBy(id => id))}";

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

            // Say "All" rather than a bare count when everything is selected.
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
