using Proteomics;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Engine;
using Tasks;
using Proteomics.ProteolyticDigestion;
using System.IO;
using System.Globalization;
using MzLibUtil;
using System.Diagnostics;
using Omics.Digestion;
using Omics.Modifications;
using UsefulProteomicsDatabases;

namespace GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ProteinDbForDataGrid> ProteinDbObservableCollection = new ObservableCollection<ProteinDbForDataGrid>();
        private readonly ObservableCollection<ProteinDbForDataGrid> ReloadProteinDbObservableCollection = new ObservableCollection<ProteinDbForDataGrid>();
        private readonly ObservableCollection<ResultsForDataGrid> ResultsObservableCollection = new ObservableCollection<ResultsForDataGrid>();
        private readonly ObservableCollection<ParametersForDataGrid> ParametersObservableCollection = new ObservableCollection<ParametersForDataGrid>();
        private readonly ObservableCollection<PreRunTask> StaticTasksObservableCollection = new ObservableCollection<PreRunTask>();
        private ObservableCollection<InRunTask> DynamicTasksObservableCollection;
        private readonly ObservableCollection<RunSummaryForTreeView> SummaryForTreeViewObservableCollection;
        private Parameters UserParameters;

        // Progress tracking fields
        private volatile ProgressEventArgs _latestProgress;
        private System.Windows.Threading.DispatcherTimer _progressTimer;

        public MainWindow()
        {
            InitializeComponent();
            Title = "ProteaseGuru: Version " + GlobalVariables.ProteaseGuruVersion;
            UserParameters = new Parameters();
            PopulateProteaseList();
            dataGridProteinDatabases.DataContext = ProteinDbObservableCollection;
            dataGridResults.DataContext = ResultsObservableCollection;
            dataGridParameters.DataContext = ParametersObservableCollection;
            dataGridReloadDb.DataContext = ReloadProteinDbObservableCollection;
            EverythingRunnerEngine.NewDbsHandler += AddNewDB;
            EverythingRunnerEngine.WarnHandler += GuiWarnHandler;
            DigestionTask.OutLabelStatusHandler += NewoutLabelStatus;
            DigestionTask.ProgressHandler += UpdateProgress;
            SummaryForTreeViewObservableCollection = new ObservableCollection<RunSummaryForTreeView>();
            ResetDigestionTask.IsEnabled = false;
        }

        #region File Loading

        private void AddResults_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openPicker = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = "Results Files|*.tsv",
                FilterIndex = 1,
                RestoreDirectory = true,
                Multiselect = true
            };
            if (openPicker.ShowDialog() == true)
            {
                foreach (var filepath in openPicker.FileNames.OrderBy(p => p))
                {
                    if (System.IO.Path.GetExtension(filepath) != ".tsv")
                    {
                        MessageBox.Show("Error: Only ProteaseGuru results files in .tsv format should be loaded here. Please remove '" + filepath + "' before proceeding with analysis");
                        return;
                    }
                    else
                    {
                        ReloadAFile(filepath);
                    }
                }
            }
            dataGridResults.Items.Refresh();
        }

        private void AddParameters_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openPicker = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = "Results Files|*.txt",
                FilterIndex = 1,
                RestoreDirectory = true,
                Multiselect = true
            };
            if (openPicker.ShowDialog() == true)
            {
                foreach (var filepath in openPicker.FileNames.OrderBy(p => p))
                {
                    if (System.IO.Path.GetExtension(filepath) != ".txt")
                    {
                        MessageBox.Show("Error: Only ProteaseGuru digestion parameters in .txt format should be loaded here. Please remove '" + filepath + "' before proceeding with analysis");
                        return;
                    }
                    else
                    {
                        ReloadAFile(filepath);
                    }
                }
            }
            dataGridParameters.Items.Refresh();
        }

        private void AddAFile(string draggedFilePath)
        {
            var filename = System.IO.Path.GetFileName(draggedFilePath);
            var theExtension = System.IO.Path.GetExtension(filename).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz");
            theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(filename)).ToLowerInvariant() : theExtension;

            switch (theExtension)
            {
                case ".xml":
                case ".fasta":
                case ".fa":
                    ProteinDbForDataGrid uu = new ProteinDbForDataGrid(draggedFilePath);
                    if (!DatabaseExists(ProteinDbObservableCollection, uu))
                    {
                        ProteinDbObservableCollection.Add(uu);
                        if (theExtension.Equals(".xml"))
                        {
                            try
                            {
                                GlobalVariables.AddMods(UsefulProteomicsDatabases.ProteinDbLoader.GetPtmListFromProteinXml(draggedFilePath).OfType<Modification>(), true);
                                PrintErrorsReadingMods();
                            }
                            catch (Exception ee)
                            {
                                MessageBox.Show(ee.ToString());
                                GuiWarnHandler(null, new StringEventArgs("Cannot parse modification info from: " + draggedFilePath, null));
                                ProteinDbObservableCollection.Remove(uu);
                            }
                        }
                    }
                    break;
                default:
                    GuiWarnHandler(null, new StringEventArgs("Unrecognized file type: " + theExtension, null));
                    break;
            }
        }

        private void ReloadAFile(string draggedFilePath)
        {
            var filename = System.IO.Path.GetFileName(draggedFilePath);
            var theExtension = System.IO.Path.GetExtension(filename).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz");
            theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(filename)).ToLowerInvariant() : theExtension;

            switch (theExtension)
            {
                case ".xml":
                case ".fasta":
                case ".fa":
                    ProteinDbForDataGrid uu = new ProteinDbForDataGrid(draggedFilePath);
                    if (!DatabaseExists(ReloadProteinDbObservableCollection, uu))
                    {
                        ReloadProteinDbObservableCollection.Add(uu);
                        if (theExtension.Equals(".xml"))
                        {
                            try
                            {
                                GlobalVariables.AddMods(UsefulProteomicsDatabases.ProteinDbLoader.GetPtmListFromProteinXml(draggedFilePath).OfType<Modification>(), true);
                                PrintErrorsReadingMods();
                            }
                            catch (Exception ee)
                            {
                                MessageBox.Show(ee.ToString());
                                GuiWarnHandler(null, new StringEventArgs("Cannot parse modification info from: " + draggedFilePath, null));
                                ReloadProteinDbObservableCollection.Remove(uu);
                            }
                        }
                    }
                    break;
                case ".tsv":
                    ResultsForDataGrid file = new ResultsForDataGrid(draggedFilePath);
                    if (!ResultsFileExists(ResultsObservableCollection, file))
                    {
                        ResultsObservableCollection.Add(file);
                    }
                    break;
                case ".txt":
                    ParametersForDataGrid parameters = new ParametersForDataGrid(draggedFilePath);
                    if (!ParametersFileExists(ParametersObservableCollection, parameters))
                    {
                        ParametersObservableCollection.Add(parameters);
                    }
                    break;
                default:
                    GuiWarnHandler(null, new StringEventArgs("Unrecognized file type: " + theExtension, null));
                    break;
            }
        }

        private bool DatabaseExists(ObservableCollection<ProteinDbForDataGrid> pDOC, ProteinDbForDataGrid uuu)
        {
            return pDOC.Any(pdoc => pdoc.FilePath == uuu.FilePath);
        }

        private bool ResultsFileExists(ObservableCollection<ResultsForDataGrid> ROC, ResultsForDataGrid uuu)
        {
            return ROC.Any(roc => roc.FilePath == uuu.FilePath);
        }

        private bool ParametersFileExists(ObservableCollection<ParametersForDataGrid> POC, ParametersForDataGrid uuu)
        {
            return POC.Any(poc => poc.FilePath == uuu.FilePath);
        }

        private void PrintErrorsReadingMods()
        {
            foreach (var error in GlobalVariables.ErrorsReadingMods)
            {
                GuiWarnHandler(null, new StringEventArgs(error, null));
            }
            GlobalVariables.ErrorsReadingMods.Clear();
        }

        #endregion

        #region Event Handlers

        private void GuiWarnHandler(object sender, StringEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => GuiWarnHandler(sender, e)));
            }
        }

        private void AddNewDB(object sender, XmlForTaskListEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AddNewDB(sender, e)));
            }
            else
            {
                foreach (var uu in e.NewDatabases)
                {
                    ProteinDbObservableCollection.Add(new ProteinDbForDataGrid(uu));
                    ReloadProteinDbObservableCollection.Add(new ProteinDbForDataGrid(uu));
                }
                dataGridProteinDatabases.Items.Refresh();
                dataGridReloadDb.Items.Refresh();
            }
        }

        private void NewoutLabelStatus(object sender, StringEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewoutLabelStatus(sender, s)));
            }
            else
            {
                ProgressTextBox.Text = s.S;
            }
        }

        #endregion

        #region Progress Reporting

        private void StartProgressTimer()
        {
            _progressTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _progressTimer.Tick += ProgressTimer_Tick;
            _progressTimer.Start();
        }

        private void StopProgressTimer()
        {
            if (_progressTimer != null)
            {
                _progressTimer.Stop();
                _progressTimer.Tick -= ProgressTimer_Tick;
                _progressTimer = null;
            }
        }

        private void UpdateProgress(object sender, ProgressEventArgs e)
        {
            _latestProgress = e;
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            var progress = _latestProgress;
            if (progress != null)
            {
                TaskProgressBar.Maximum = progress.MaxProgress;
                TaskProgressBar.Value = progress.CurrentProgress;
                ProgressTextBox.Text = progress.StatusMessage;
            }
        }

        #endregion

        #region Parameter Updates

        private void UpdateFieldsFromUser(DigestionTask run)
        {
            if (!string.IsNullOrWhiteSpace(MissedCleavagesTextBox.Text))
            {
                try
                {
                    int value = Convert.ToInt32(MissedCleavagesTextBox.Text);
                    UserParameters.NumberOfMissedCleavagesAllowed = value;
                }
                catch (FormatException)
                {
                    MessageBox.Show("Error: The value provided for the 'Number of Missed Cleavages' is invalid, please replace with an integer value before proceeding with analysis.");
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(MinPeptideLengthTextBox.Text))
            {
                try
                {
                    int value = Convert.ToInt32(MinPeptideLengthTextBox.Text);
                    UserParameters.MinPeptideLengthAllowed = value;
                }
                catch (FormatException)
                {
                    MessageBox.Show("Error: The value provided for the 'Min Peptide Length' is invalid, please replace with an integer value before proceeding with analysis.");
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(MaxPeptideLengthTextBox.Text))
            {
                try
                {
                    int value = Convert.ToInt32(MaxPeptideLengthTextBox.Text);
                    UserParameters.MaxPeptideLengthAllowed = value;
                }
                catch (FormatException)
                {
                    MessageBox.Show("Error: The value provided for the 'Max Peptide Length' is invalid, please replace with an integer value before proceeding with analysis.");
                    return;
                }
            }
            UserParameters.TreatModifiedPeptidesAsDifferent = Convert.ToBoolean(ModPepsAreUnique.IsChecked);
            if (Convert.ToBoolean(FixedCarbamido.IsChecked))
            {
                UserParameters.fixedMods = GlobalVariables.AllModsKnown.Where(p => p.IdWithMotif == "Carbamidomethyl on C").ToList();
            }
            if (Convert.ToBoolean(VariableOx.IsChecked))
            {
                UserParameters.variableMods = GlobalVariables.AllModsKnown.Where(p => p.IdWithMotif == "Oxidation on M").ToList();
            }
            if (!string.IsNullOrWhiteSpace(MinPeptideMassTextBox.Text))
            {
                try
                {
                    int value = Convert.ToInt32(MinPeptideMassTextBox.Text);
                    UserParameters.MinPeptideMassAllowed = value;
                }
                catch (FormatException)
                {
                    MessageBox.Show("Error: The value provided for the 'Min Peptide Mass' is invalid, please replace with an integer value before proceeding with analysis.");
                    return;
                }
            }
            else
            {
                UserParameters.MinPeptideMassAllowed = -1;
            }
            if (!string.IsNullOrWhiteSpace(MaxPeptideMassTextBox.Text))
            {
                try
                {
                    int value = Convert.ToInt32(MaxPeptideMassTextBox.Text);
                    UserParameters.MaxPeptideMassAllowed = value;
                }
                catch (FormatException)
                {
                    MessageBox.Show("Error: The value provided for the 'Max Peptide Mass' is invalid, please replace with an integer value before proceeding with analysis.");
                    return;
                }
            }
            else
            {
                UserParameters.MaxPeptideMassAllowed = -1;
            }
            List<Protease> proteases = new List<Protease>();
            foreach (var protease in ProteaseSelectedForUse.SelectedItems)
            {
                var name = protease.ToString().Split(':')[1].Trim();
                proteases.Add(ProteaseDictionary.Dictionary[name]);
            }
            UserParameters.ProteasesForDigestion = proteases;
            run.DigestionParameters = UserParameters;
        }

        #endregion

        #region UI Click Handlers

        private void UpdateOutputFolderTextbox()
        {
            if (ProteinDbObservableCollection.Any())
            {
                if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
                {
                    var pathOfFirstSpectraFile = System.IO.Path.GetDirectoryName(ProteinDbObservableCollection.First().FilePath);
                    OutputFolderTextBox.Text = System.IO.Path.Combine(pathOfFirstSpectraFile, @"$DATETIME");
                }
            }
            else
            {
                OutputFolderTextBox.Clear();
            }
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            string outputFolder = OutputFolderTextBox.Text;
            if (outputFolder.Contains("$DATETIME"))
            {
                outputFolder = Directory.GetParent(outputFolder).FullName;
            }

            if (!Directory.Exists(outputFolder) && !string.IsNullOrEmpty(outputFolder))
            {
                try
                {
                    Directory.CreateDirectory(outputFolder);
                }
                catch (Exception ex)
                {
                    GuiWarnHandler(null, new StringEventArgs("Error opening directory: " + ex.Message, null));
                }
            }

            if (Directory.Exists(outputFolder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputFolder,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                GuiWarnHandler(null, new StringEventArgs("Output folder does not exist", null));
            }
        }

        private void EverythingRunnerExceptionHandler(Task obj)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => EverythingRunnerExceptionHandler(obj)));
            }
            else
            {
                Exception e = obj.Exception;
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                }

                var message = "Run failed, Exception: " + e.Message;
                var messageBoxResult = System.Windows.MessageBox.Show(message + "\n\nWould you like to report this crash?", "Runtime Error", MessageBoxButton.YesNo);

                Exception exception = e;
                string outputFolder = e.Data["folder"].ToString();

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    string body = exception.Message + "%0D%0A" + exception.Data +
                       "%0D%0A" + exception.StackTrace +
                       "%0D%0A" + exception.Source +
                       "%0D%0A %0D%0A %0D%0A %0D%0A SYSTEM INFO: %0D%0A " +
                        SystemInfo.CompleteSystemInfo() +
                        "%0D%0A %0D%0A %0D%0A %0D%0A TOML: %0D%0A ";
                    body = body.Replace('&', ' ');
                    body = body.Replace("\n", "%0D%0A");
                    body = body.Replace("\r", "%0D%0A");
                    string mailto = string.Format("mailto:{0}?Subject=ProteaseGuru. Issue:&Body={1}", "mm_support@chem.wisc.edu", body);
                    GlobalVariables.StartProcess(mailto);
                    Console.WriteLine(body);
                }
            }
        }

        private void AddDigestionTask_Click(object sender, RoutedEventArgs e)
        {
            if (StaticTasksObservableCollection.Count() != 0)
            {
                StaticTasksObservableCollection.Clear();
            }
            AddDigestionTask.IsEnabled = false;
            ResetDigestionTask.IsEnabled = true;

            ProteaseSelectedForUse.IsEnabled = false;
            MissedCleavagesTextBox.IsEnabled = false;
            MinPeptideLengthTextBox.IsEnabled = false;
            MaxPeptideLengthTextBox.IsEnabled = false;

            DigestionTask task = new DigestionTask();
            UpdateFieldsFromUser(task);
            AddTaskToCollection(task);
            OutputFolderTextBox.IsEnabled = true;

            GenerateRunSummary();

            if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
            {
                if (ProteinDbObservableCollection.Count() == 0)
                {
                    MessageBox.Show("Error: No databases are provided for digestion. Please add databases before proceeding with analysis.");
                    return;
                }
                var pathOfFirstDbFile = System.IO.Path.GetDirectoryName(ProteinDbObservableCollection.First().FilePath);
                OutputFolderTextBox.Text = System.IO.Path.Combine(pathOfFirstDbFile, @"$DATETIME");
            }

            var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
            string outputFolder = OutputFolderTextBox.Text.Replace("$DATETIME", startTimeForAllFilenames);
            OutputFolderTextBox.Text = outputFolder;
            UserParameters.OutputFolder = outputFolder;
        }

        private void AddTaskToCollection(ProteaseGuruTask task)
        {
            PreRunTask pre = new PreRunTask(task);
            StaticTasksObservableCollection.Add(pre);
        }

        private void ClearXML_Click(object sender, RoutedEventArgs e)
        {
            ProteinDbObservableCollection.Clear();
            dataGridProteinDatabases.ItemsSource = ProteinDbObservableCollection;
            dataGridProteinDatabases.Items.Refresh();
        }

        private void ClearReloadedXML_Click(object sender, RoutedEventArgs e)
        {
            ReloadProteinDbObservableCollection.Clear();
            dataGridReloadDb.ItemsSource = ReloadProteinDbObservableCollection;
            dataGridReloadDb.Items.Refresh();
        }

        private void ClearResults_Click(object sender, RoutedEventArgs e)
        {
            ResultsObservableCollection.Clear();
            dataGridResults.ItemsSource = ResultsObservableCollection;
            dataGridResults.Items.Refresh();
        }

        private void ClearParameters_Click(object sender, RoutedEventArgs e)
        {
            ParametersObservableCollection.Clear();
            dataGridParameters.ItemsSource = ParametersObservableCollection;
            dataGridParameters.Items.Refresh();
        }

        private void AddProteinDatabase_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openPicker = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = "Database Files|*.xml;*.xml.gz;*.fasta;*.fa",
                FilterIndex = 1,
                RestoreDirectory = true,
                Multiselect = true
            };
            if (openPicker.ShowDialog() == true)
            {
                foreach (var filepath in openPicker.FileNames.OrderBy(p => p))
                {
                    string theExtension = System.IO.Path.GetExtension(filepath).ToLowerInvariant();
                    bool compressed = theExtension.EndsWith("gz");
                    theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(filepath)).ToLowerInvariant() : theExtension;
                    if (theExtension == ".xml" || theExtension == ".fasta" || theExtension == ".fa")
                    {
                        AddAFile(filepath);
                    }
                    else
                    {
                        MessageBox.Show("Error: Database provided is not an acceptable file format. Please remove '" + filepath + "' before proceeding with analysis");
                        return;
                    }
                }
            }
            dataGridProteinDatabases.Items.Refresh();
        }

        private void ReloadProteinDatabase_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openPicker = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = "Database Files|*.xml;*.xml.gz;*.fasta;*.fa",
                FilterIndex = 1,
                RestoreDirectory = true,
                Multiselect = true
            };
            if (openPicker.ShowDialog() == true)
            {
                foreach (var filepath in openPicker.FileNames.OrderBy(p => p))
                {
                    string theExtension = System.IO.Path.GetExtension(filepath).ToLowerInvariant();
                    bool compressed = theExtension.EndsWith("gz");
                    theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(filepath)).ToLowerInvariant() : theExtension;
                    if (theExtension == ".xml" || theExtension == ".fasta" || theExtension == ".fa")
                    {
                        ReloadAFile(filepath);
                    }
                    else
                    {
                        MessageBox.Show("Error: Database provided is not an acceptable file format. Please remove '" + filepath + "' before proceeding with analysis");
                        return;
                    }
                }
            }
            dataGridReloadDb.Items.Refresh();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            string[] files = ((string[])e.Data.GetData(DataFormats.FileDrop)).OrderBy(p => p).ToArray();

            if (files != null)
            {
                foreach (var draggedFilePath in files)
                {
                    if (Directory.Exists(draggedFilePath))
                    {
                        foreach (string file in Directory.EnumerateFiles(draggedFilePath, "*.*", SearchOption.AllDirectories))
                        {
                            AddAFile(file);
                            ReloadAFile(file);
                        }
                    }
                    else
                    {
                        AddAFile(draggedFilePath);
                        ReloadAFile(draggedFilePath);
                    }
                    dataGridProteinDatabases.CommitEdit(DataGridEditingUnit.Row, true);
                    dataGridProteinDatabases.Items.Refresh();
                    dataGridReloadDb.CommitEdit(DataGridEditingUnit.Row, true);
                    dataGridReloadDb.Items.Refresh();
                    dataGridResults.CommitEdit(DataGridEditingUnit.Row, true);
                    dataGridResults.Items.Refresh();
                    dataGridParameters.CommitEdit(DataGridEditingUnit.Row, true);
                    dataGridParameters.Items.Refresh();
                }
            }
        }

        private void SelectDefaultProteases_Click(object sender, RoutedEventArgs e)
        {
            ProteaseSelectedForUse.SelectedItems.Clear();
            ProteaseSelectedForUse.SelectedItems.Add(ProteaseSelectedForUse.Items.GetItemAt(0));
            ProteaseSelectedForUse.SelectedItems.Add(ProteaseSelectedForUse.Items.GetItemAt(1));
            ProteaseSelectedForUse.SelectedItems.Add(ProteaseSelectedForUse.Items.GetItemAt(2));
            ProteaseSelectedForUse.SelectedItems.Add(ProteaseSelectedForUse.Items.GetItemAt(6));
            ProteaseSelectedForUse.SelectedItems.Add(ProteaseSelectedForUse.Items.GetItemAt(7));
            ProteaseSelectedForUse.SelectedItems.Add(ProteaseSelectedForUse.Items.GetItemAt(10));
        }

        private void ClearSelectedProteases_Click(object sender, RoutedEventArgs e)
        {
            ProteaseSelectedForUse.SelectedItems.Clear();
        }

        private void AddCustomProtease_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomProteaseWindow();
            dialog.ShowDialog();
            if (dialog.proteaseAdded == true)
            {
                PopulateProteaseList();
            }
        }

        #endregion

        #region Run Task

        private async void RunTaskButton_Click(object sender, RoutedEventArgs e)
        {
            RunTaskButton.IsEnabled = false;
            GlobalVariables.StopLoops = false;

            if (!StaticTasksObservableCollection.Any())
            {
                MessageBox.Show("Warning: No digestion conditions have been saved. Set and save digestion conditions before proceeding with analysis.");
                RunTaskButton.IsEnabled = true;
                return;
            }

            if (!ProteinDbObservableCollection.Any())
            {
                MessageBox.Show("Warning: No protein databases have been provided for digestion. Add at least one protein database before proceeding with analysis.");
                RunTaskButton.IsEnabled = true;
                return;
            }

            if (!UserParameters.ProteasesForDigestion.Any())
            {
                MessageBox.Show("Warning: No proteases have been selected for digestion. Select at least one protease and save the updated digestion conditions before proceeding with analysis.");
                RunTaskButton.IsEnabled = true;
                return;
            }

            DynamicTasksObservableCollection = new ObservableCollection<InRunTask>();

            for (int i = 0; i < StaticTasksObservableCollection.Count; i++)
            {
                DynamicTasksObservableCollection.Add(new InRunTask("Task" + (i + 1) + "-" + StaticTasksObservableCollection[i].proteaseGuruTask.TaskType, StaticTasksObservableCollection[i].proteaseGuruTask));
            }

            EverythingRunnerEngine a = new EverythingRunnerEngine(DynamicTasksObservableCollection.Select(b => (b.DisplayName, b.Task)).ToList(),
                ProteinDbObservableCollection.Select(b => new DbForDigestion(b.FilePath)).ToList(),
                OutputFolderTextBox.Text);

            // Reset and initialize the progress bar
            TaskProgressBar.Value = 0;
            TaskProgressBar.Maximum = 100;
            ProgressTextBox.Text = "Starting...";
            _latestProgress = null;

            // Start the progress timer BEFORE the background work begins
            StartProgressTimer();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var results = await Task.Run(() => a.Run());

            // Stop the timer after work completes
            StopProgressTimer();

            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptidesByFile = results.PeptideByFile;
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease = results.SequenceCoverageByProtease;
            stopwatch.Stop();

            // Update progress bar to show completion
            TaskProgressBar.Value = TaskProgressBar.Maximum;
            ProgressTextBox.Text = $"Complete! (Elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s)";

            StaticTasksObservableCollection.Clear();
            AllResultsTab.Content = new AllResultsWindow(peptidesByFile, UserParameters);
            ProteinCovMap.Content = new ProteinResultsWindow(peptidesByFile, UserParameters, sequenceCoverageByProtease);
            AllHistogramsTab.Content = new HistogramWindow(peptidesByFile, UserParameters, sequenceCoverageByProtease);
            AllResultsTab.IsSelected = true;
            RunTaskButton.IsEnabled = true;
        }

        #endregion

        #region Load Results

        private void LoadResults_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptidesByFileSetUp = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptidesByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();

            Parameters loadedParams = new Parameters();

            string proteaseDirectory = System.IO.Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion");
            string proteaseFilePath = System.IO.Path.Combine(proteaseDirectory, @"proteases.tsv");
            var myLines = File.ReadAllLines(proteaseFilePath);
            myLines = myLines.Skip(1).ToArray();
            Dictionary<string, Protease> dict = new Dictionary<string, Protease>();
            foreach (string line in myLines)
            {
                if (line.Trim() != string.Empty)
                {
                    string[] fields = line.Split('\t');
                    List<DigestionMotif> motifList = DigestionMotif.ParseDigestionMotifsFromString(fields[1]);
                    string name = fields[0];
                    var cleavageSpecificity = ((CleavageSpecificity)Enum.Parse(typeof(CleavageSpecificity), fields[4], true));
                    string psiMsAccessionNumber = fields[5];
                    string psiMsName = fields[6];
                    var protease = new Protease(name, cleavageSpecificity, psiMsAccessionNumber, psiMsName, motifList);
                    dict.Add(protease.Name, protease);
                }
            }

            foreach (var parameterFile in ParametersObservableCollection)
            {
                var fileData = File.ReadAllLines(parameterFile.FilePath);
                List<Protease> proteases = new List<Protease>();
                int missedCleavages = 0;
                int minPeptideLength = 0;
                int maxPeptideLength = 0;
                bool treatModPeps = false;
                foreach (var parameter in fileData)
                {
                    var info = parameter.Split(": ");
                    switch (info[0])
                    {
                        case "Digestion Conditions:":
                            break;
                        case "Databases":
                            // Skip - this is informational only
                            break;
                        case "Proteases":
                            var proteaseNames = info[1].Split(", ");
                            foreach (var protease in proteaseNames)
                            {
                                var trimmedName = protease.Trim();
                                if (dict.ContainsKey(trimmedName))
                                {
                                    proteases.Add(dict[trimmedName]);
                                }
                            }
                            break;
                        case "Max Missed Cleavages":
                            missedCleavages = Convert.ToInt32(info[1]);
                            break;
                        case "Min Peptide Length":
                            minPeptideLength = Convert.ToInt32(info[1]);
                            break;
                        case "Max Peptide Length":
                            maxPeptideLength = Convert.ToInt32(info[1]);
                            break;
                        case "Treat modified peptides as different peptides":
                            treatModPeps = info[1] == "True";
                            break;
                        case "Min Peptide Mass":
                            loadedParams.MinPeptideMassAllowed = Convert.ToInt32(info[1]);
                            break;
                        case "Max Peptide Mass":
                            loadedParams.MaxPeptideMassAllowed = Convert.ToInt32(info[1]);
                            break;
                        default:
                            // Unknown parameter - skip instead of failing
                            break;
                    }
                }

                loadedParams.ProteasesForDigestion = proteases;
                loadedParams.NumberOfMissedCleavagesAllowed = missedCleavages;
                loadedParams.MinPeptideLengthAllowed = minPeptideLength;
                loadedParams.MaxPeptideLengthAllowed = maxPeptideLength;
                loadedParams.TreatModifiedPeptidesAsDifferent = treatModPeps;
            }

            List<InSilicoPep> allpeptides = new List<InSilicoPep>();
            foreach (var resultFile in ResultsObservableCollection)
            {
                var fileData = File.ReadAllLines(resultFile.FilePath);
                int peptideCount = 0;
                var header = fileData[0].Split('\t');
                if (header[0] != "Database" && header[1] != "Protease" && header[2] != "Base Sequence" && header[3] != "Full Sequence")
                {
                    MessageBox.Show("Error: Results file provided is not from a previous ProteaseGuru run.");
                    return;
                }
                foreach (var peptide in fileData)
                {
                    if (peptideCount != 0)
                    {
                        var info = peptide.Split('\t');
                        string database = info[0];
                        string protease = info[1];
                        string baseSeq = info[2];
                        string fullSeq = info[3];
                        char previousAA = Convert.ToChar(info[4]);
                        char nextAA = Convert.ToChar(info[5]);
                        int start = Convert.ToInt32(info[6]);
                        int end = Convert.ToInt32(info[7]);
                        int length = Convert.ToInt32(info[8]);
                        double molecularWeight = Convert.ToDouble(info[9]);
                        string protein = info[10];
                        string proteinName = info[11];
                        bool unique = info[12] == "True";
                        bool uniqueAll = info[13] == "True";
                        bool oneDb = info[14] == "True";
                        double hydrophobicity = Convert.ToDouble(info[15]);
                        double electrophoreticMobility = Convert.ToDouble(info[16]);

                        double chronologerRetentionTime = -1;
                        if (info.Length > 17)
                        {
                            chronologerRetentionTime = Convert.ToDouble(info[17]);
                        }

                        InSilicoPep pep = new InSilicoPep(baseSeq, fullSeq, previousAA, nextAA, unique, hydrophobicity, electrophoreticMobility,
                            chronologerRetentionTime, length, molecularWeight, database, protein, proteinName, start, end, protease);
                        pep.UniqueAllDbs = uniqueAll;
                        pep.SeqOnlyInThisDb = oneDb;
                        allpeptides.Add(pep);
                    }
                    peptideCount++;
                }

                foreach (var db in ReloadProteinDbObservableCollection)
                {
                    var dbName = db.FileName;
                    var proteinsFromDb = LoadProteins(new DbForDigestion(db.FilePath));
                    var proteases = loadedParams.ProteasesForDigestion;

                    Dictionary<Protein, List<InSilicoPep>> proteinDic = new Dictionary<Protein, List<InSilicoPep>>();

                    foreach (var protein in proteinsFromDb)
                    {
                        if (!proteinDic.ContainsKey(protein))
                        {
                            proteinDic.Add(protein, new List<InSilicoPep>() { });
                        }
                    }
                    Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> proteaseDic = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                    foreach (var protease in proteases)
                    {
                        if (!proteaseDic.ContainsKey(protease.Name))
                        {
                            proteaseDic.Add(protease.Name, proteinDic);
                        }
                    }
                    if (!PeptidesByFileSetUp.ContainsKey(dbName))
                    {
                        PeptidesByFileSetUp.Add(dbName, proteaseDic);
                    }
                }

                foreach (var entry in PeptidesByFileSetUp)
                {
                    var pepByDb = allpeptides.Where(p => p.Database == entry.Key).ToList();
                    Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> proteaseComplete = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                    foreach (var protease in entry.Value)
                    {
                        var pepByProtease = pepByDb.Where(p => p.Protease == protease.Key).ToList();
                        Dictionary<Protein, List<InSilicoPep>> proteinComplete = new Dictionary<Protein, List<InSilicoPep>>();

                        foreach (var protein in protease.Value)
                        {
                            var pepByProtein = pepByProtease.Where(p => p.Protein == protein.Key.Accession).ToList();
                            proteinComplete.Add(protein.Key, pepByProtein);
                        }
                        proteaseComplete.Add(protease.Key, proteinComplete);
                    }
                    PeptidesByFile.Add(entry.Key, proteaseComplete);
                }
            }

            var seqCov = CalculateProteinSequenceCoverage(PeptidesByFile);

            AllResultsTab.Content = new AllResultsWindow(PeptidesByFile, loadedParams);
            ProteinCovMap.Content = new ProteinResultsWindow(PeptidesByFile, loadedParams, seqCov);
            AllHistogramsTab.Content = new HistogramWindow(PeptidesByFile, loadedParams, seqCov);
            AllResultsTab.IsSelected = true;
        }

        #endregion

        #region Utility Methods

        private void PopulateProteaseList()
        {
            string proteaseDirectory = System.IO.Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion");
            string proteaseFilePath = System.IO.Path.Combine(proteaseDirectory, @"proteases.tsv");
            Dictionary<string, Protease> dict = ProteaseDictionary.LoadProteaseDictionary(proteaseFilePath, GlobalVariables.ProteaseMods);
            var myLines = File.ReadAllLines(proteaseFilePath);
            myLines = myLines.Skip(1).ToArray();
            Dictionary<string, string> motif = new Dictionary<string, string>();
            foreach (string line in myLines)
            {
                if (line.Trim() != string.Empty)
                {
                    string[] fields = line.Split('\t');
                    motif.Add(fields[0], fields[1]);
                }
            }
            foreach (Protease protease in dict.Values)
            {
                ListBoxItem item = new ListBoxItem();
                item.Content = protease;
                item.ToolTip = "Cleavage specificity: " + motif[protease.Name].Trim(new char[] { '"' });
                ProteaseSelectedForUse.Items.Add(item);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            GlobalVariables.StartProcess(e.Uri.ToString());
        }

        private void CheckIfNumber(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !CheckIsNumber(e.Text);
        }

        public static bool CheckIsNumber(string text)
        {
            foreach (var character in text)
            {
                if (!Char.IsDigit(character) && character != '.' && character != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private void ResetDigestionTask_Click(object sender, RoutedEventArgs e)
        {
            StaticTasksObservableCollection.Clear();

            AddDigestionTask.IsEnabled = true;
            ResetDigestionTask.IsEnabled = false;

            ProteaseSelectedForUse.IsEnabled = true;
            MissedCleavagesTextBox.Clear();
            MissedCleavagesTextBox.IsEnabled = true;
            MinPeptideLengthTextBox.Clear();
            MinPeptideLengthTextBox.IsEnabled = true;
            MaxPeptideLengthTextBox.Clear();
            MaxPeptideLengthTextBox.IsEnabled = true;
            MinPeptideMassTextBox.Clear();
            MinPeptideMassTextBox.IsEnabled = true;
            MaxPeptideMassTextBox.Clear();
            MaxPeptideMassTextBox.IsEnabled = true;

            ModPepsAreUnique.IsChecked = false;

            SummaryForTreeViewObservableCollection.Clear();
        }

        private void OnRunTabSelection(object sender, RoutedEventArgs e)
        {
            if (AddDigestionTask.IsEnabled == true)
            {
                if (StaticTasksObservableCollection.Count() == 0)
                {
                    ResetDigestionTask.IsEnabled = true;
                    ProteaseSelectedForUse.IsEnabled = false;
                    MissedCleavagesTextBox.IsEnabled = false;
                    MinPeptideLengthTextBox.IsEnabled = false;
                    MaxPeptideLengthTextBox.IsEnabled = false;

                    DigestionTask task = new DigestionTask();
                    UpdateFieldsFromUser(task);
                    AddTaskToCollection(task);
                    OutputFolderTextBox.IsEnabled = true;

                    GenerateRunSummary();

                    if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
                    {
                        if (ProteinDbObservableCollection.Count() == 0)
                        {
                            MessageBox.Show("Error: No databases are provided for digestion. Please add databases before proceeding with analysis.");
                            return;
                        }
                        var pathOfFirstSpectraFile = System.IO.Path.GetDirectoryName(ProteinDbObservableCollection.First().FilePath);
                        OutputFolderTextBox.Text = System.IO.Path.Combine(pathOfFirstSpectraFile, @"$DATETIME");
                    }

                    var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
                    string outputFolder = OutputFolderTextBox.Text.Replace("$DATETIME", startTimeForAllFilenames);
                    OutputFolderTextBox.Text = outputFolder;
                    UserParameters.OutputFolder = outputFolder;
                }
            }
        }

        private void GenerateRunSummary()
        {
            RunSummaryForTreeView runSummary = new RunSummaryForTreeView("Digestion Plan:");
            CategorySummaryForTreeView databases = new CategorySummaryForTreeView("Databases:");
            foreach (var db in ProteinDbObservableCollection)
            {
                databases.Summary.Add(new FeatureForTreeView(db.FileName));
            }
            runSummary.Summary.Add(databases);
            CategorySummaryForTreeView proteases = new CategorySummaryForTreeView("Proteases:");
            foreach (var prot in UserParameters.ProteasesForDigestion)
            {
                proteases.Summary.Add(new FeatureForTreeView(prot.Name));
            }
            runSummary.Summary.Add(proteases);
            CategorySummaryForTreeView parameters = new CategorySummaryForTreeView("Digestion Parameters:");
            parameters.Summary.Add(new FeatureForTreeView("Number of Missed Cleavages: " + UserParameters.NumberOfMissedCleavagesAllowed));
            parameters.Summary.Add(new FeatureForTreeView("Minimum Peptide Length: " + UserParameters.MinPeptideLengthAllowed));
            parameters.Summary.Add(new FeatureForTreeView("Maximum Peptide Length: " + UserParameters.MaxPeptideLengthAllowed));
            parameters.Summary.Add(new FeatureForTreeView("Treat Modified Peptides as Different Peptides: " + UserParameters.TreatModifiedPeptidesAsDifferent));
            parameters.Summary.Add(new FeatureForTreeView("Minimum Peptide Mass: " + UserParameters.MinPeptideMassAllowed));
            parameters.Summary.Add(new FeatureForTreeView("Maximum Peptide Mass: " + UserParameters.MaxPeptideMassAllowed));
            runSummary.Summary.Add(parameters);

            SummaryForTreeViewObservableCollection.Add(runSummary);
            RunSummaryTreeView.DataContext = SummaryForTreeViewObservableCollection;
        }

        private void MenuItem_EmailHelp_Click(object sender, RequestNavigateEventArgs e)
        {
            string mailto = string.Format("mailto:{0}?Subject=ProteaseGuru. Issue:", "mm_support@chem.wisc.edu");
            GlobalVariables.StartProcess(mailto);
        }

        protected List<Protein> LoadProteins(DbForDigestion database)
        {
            List<string> dbErrors = new List<string>();
            List<Protein> proteinList = new List<Protein>();

            string theExtension = System.IO.Path.GetExtension(database.FilePath).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz");
            theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(database.FilePath)).ToLowerInvariant() : theExtension;

            if (theExtension.Equals(".fasta") || theExtension.Equals(".fa"))
            {
                proteinList = ProteinDbLoader.LoadProteinFasta(database.FilePath, true, DecoyType.None, false, out dbErrors, ProteinDbLoader.UniprotAccessionRegex,
                    ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                    ProteinDbLoader.UniprotOrganismRegex, -1);
                return proteinList;
            }
            else
            {
                List<string> modTypesToExclude = new List<string> { };
                proteinList = ProteinDbLoader.LoadProteinXML(database.FilePath, true, DecoyType.None, GlobalVariables.AllModsKnown, false, modTypesToExclude,
                    out Dictionary<string, Modification> um, -1, 4, 1);
                return proteinList;
            }
        }

        private void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollControl = sender as ScrollViewer;
            if (!e.Handled && sender != null)
            {
                if ((e.Delta > 0 && scrollControl.VerticalOffset == 0)
                    || (e.Delta <= 0 && scrollControl.VerticalOffset >= scrollControl.ExtentHeight - scrollControl.ViewportHeight))
                {
                    e.Handled = true;
                    var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
                    eventArg.RoutedEvent = UIElement.MouseWheelEvent;
                    eventArg.Source = sender;
                    var parent = ((Control)sender).Parent as UIElement;
                    parent.RaiseEvent(eventArg);
                }
            }
        }

        private Dictionary<string, Dictionary<Protein, (double, double)>> CalculateProteinSequenceCoverage(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile)
        {
            Dictionary<string, List<InSilicoPep>> allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
            HashSet<Protein> proteins = new HashSet<Protein>();
            foreach (var database in peptideByFile)
            {
                foreach (var protease in database.Value)
                {
                    if (allDatabasePeptidesByProtease.ContainsKey(protease.Key))
                    {
                        foreach (var protein in protease.Value)
                        {
                            allDatabasePeptidesByProtease[protease.Key].AddRange(protein.Value);
                            proteins.Add(protein.Key);
                        }
                    }
                    else
                    {
                        allDatabasePeptidesByProtease.Add(protease.Key, protease.Value.SelectMany(p => p.Value).ToList());
                        foreach (var protein in protease.Value)
                        {
                            proteins.Add(protein.Key);
                        }
                    }
                }
            }

            Dictionary<string, Dictionary<Protein, (double, double)>> proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double, double)>>();
            foreach (var protease in allDatabasePeptidesByProtease)
            {
                var proteinForProtease = protease.Value.GroupBy(p => p.Protein).ToDictionary(group => group.Key, group => group.ToList());
                Dictionary<Protein, (double, double)> sequenceCoverages = new Dictionary<Protein, (double, double)>();
                foreach (var protein in proteinForProtease)
                {
                    HashSet<int> coveredOneBasesResidues = new HashSet<int>();
                    HashSet<int> coveredOneBasesResiduesUnique = new HashSet<int>();
                    var minPeptideList = protein.Value.ToHashSet();
                    foreach (var peptide in minPeptideList)
                    {
                        for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                        {
                            coveredOneBasesResidues.Add(i);
                            if (peptide.Unique == true)
                            {
                                coveredOneBasesResiduesUnique.Add(i);
                            }
                        }
                    }
                    double seqCoverageFract = (double)coveredOneBasesResidues.Count / protein.Key.Length;
                    double seqCoverageFractUnique = (double)coveredOneBasesResiduesUnique.Count / protein.Key.Length;

                    sequenceCoverages.Add(proteins.Where(p => p.Accession == protein.Key).First(), (Math.Round(seqCoverageFract, 3), Math.Round(seqCoverageFractUnique, 3)));
                }
                proteinSequenceCoverageByProtease.Add(protease.Key, sequenceCoverages);
            }
            return proteinSequenceCoverageByProtease;
        }

        private void MenuItem_Spritz_Click(object sender, RoutedEventArgs e)
        {
            GlobalVariables.StartProcess(@"https://smith-chem-wisc.github.io/Spritz/");
        }

        private void MenuItem_MetaMorpheus_Click(object sender, RoutedEventArgs e)
        {
            GlobalVariables.StartProcess(@"https://github.com/smith-chem-wisc/MetaMorpheus");
        }

        private void MenuItem_Twitter_Click(object sender, RoutedEventArgs e)
        {
            GlobalVariables.StartProcess(@"https://twitter.com/Smith_Chem_Wisc");
        }

        private void MenuItem_ProteomicsNewsBlog_Click(object sender, RoutedEventArgs e)
        {
            GlobalVariables.StartProcess(@"https://proteomicsnews.blogspot.com/");
        }

        #endregion
    }
}
