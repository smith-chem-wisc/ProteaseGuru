using Proteomics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Engine;
using Tasks;
using Proteomics.ProteolyticDigestion;
using System.IO;
using System.Globalization;
using static Tasks.ProteaseGuruTask;
using MzLibUtil;
using System.Diagnostics;
using Omics.Digestion;
using Omics.Modifications;
using UsefulProteomicsDatabases;
using GuiFunctions;

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
        private readonly ObservableCollection<RunSummaryForTreeView> SummaryForTreeViewObservableCollection;

        private readonly DigestionConditionsSetupViewModel ParametersViewModel;

        //set up the main window that users interact with
        public MainWindow()
        {
            InitializeComponent();
            Title = "ProteaseGuru: Version " + GlobalVariables.ProteaseGuruVersion;

            // TODO: Set up default parameters to check for
            ParametersViewModel = new(new Parameters());
            digestionConditionsControl.DataContext = ParametersViewModel;
          
            dataGridProteinDatabases.DataContext = ProteinDbObservableCollection;
            dataGridResults.DataContext = ResultsObservableCollection;
            dataGridParameters.DataContext = ParametersObservableCollection;
            dataGridReloadDb.DataContext = ReloadProteinDbObservableCollection;
            EverythingRunnerEngine.NewDbsHandler += AddNewDB;
            EverythingRunnerEngine.WarnHandler += GuiWarnHandler;
            DigestionTask.OutLabelStatusHandler += NewoutLabelStatus;
            SummaryForTreeViewObservableCollection = new ObservableCollection<RunSummaryForTreeView>();
        }

        //the add button for loading previous peptide result files
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
                        MessageBox.Show("Error: Only ProteaseGuru results files in .tsv format should be loaded here. Please remove '"+ filepath +"' before proceeding with analysis");
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

        //add button for digestion parameters from previous results
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

        //add a protein database file
        private void AddAFile(string draggedFilePath)
        {
            // this line is NOT used because .xml.gz (extensions with two dots) mess up with Path.GetExtension
            //var theExtension = Path.GetExtension(draggedFilePath).ToLowerInvariant();

            // we need to get the filename before parsing out the extension because if we assume that everything after the dot
            // is the extension and there are dots in the file path (i.e. in a folder name), this will mess up
            var filename = System.IO.Path.GetFileName(draggedFilePath);
            var theExtension = System.IO.Path.GetExtension(filename).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
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
        // add a previous results, prarmeters or database file
        private void ReloadAFile(string draggedFilePath)
        {
            // this line is NOT used because .xml.gz (extensions with two dots) mess up with Path.GetExtension
            //var theExtension = Path.GetExtension(draggedFilePath).ToLowerInvariant();

            // we need to get the filename before parsing out the extension because if we assume that everything after the dot
            // is the extension and there are dots in the file path (i.e. in a folder name), this will mess up
            var filename = System.IO.Path.GetFileName(draggedFilePath);
            var theExtension = System.IO.Path.GetExtension(filename).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
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

        //make sure database file has correct path
        private bool DatabaseExists(ObservableCollection<ProteinDbForDataGrid> pDOC, ProteinDbForDataGrid uuu)
        {
            foreach (ProteinDbForDataGrid pdoc in pDOC)
            {
                if (pdoc.FilePath == uuu.FilePath) { return true; }
            }

            return false;
        }
        
        //make sure results file has correct path
        private bool ResultsFileExists(ObservableCollection<ResultsForDataGrid> ROC, ResultsForDataGrid uuu)
        {
            foreach (var roc in ROC)
            {
                if (roc.FilePath == uuu.FilePath) { return true; }
            }

            return false;
        }
        
        //make sure parameters file has correct path
        private bool ParametersFileExists(ObservableCollection<ParametersForDataGrid> POC, ParametersForDataGrid uuu)
        {
            foreach (var poc in POC)
            {
                if (poc.FilePath == uuu.FilePath) { return true; }
            }

            return false;
        }

        private void PrintErrorsReadingMods()
        {
            // print any error messages reading the mods to the notifications area
            foreach (var error in GlobalVariables.ErrorsReadingMods)
            {
                GuiWarnHandler(null, new StringEventArgs(error, null));
            }
            GlobalVariables.ErrorsReadingMods.Clear();
        }

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

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            string outputFolder = OutputFolderTextBox.Text;
            if (outputFolder.Contains("$DATETIME"))
            {
                // the exact file path isn't known, so just open the parent directory
                outputFolder = Directory.GetParent(outputFolder).FullName;
            }

            if (!Directory.Exists(outputFolder) && !string.IsNullOrEmpty(outputFolder))
            {
                // create the directory if it doesn't exist yet
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
                // open the directory
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = outputFolder,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                // this should only happen if the file path is empty or something unexpected happened
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
                //Find Output Folder
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

        //clear the list of databases in code and in the GUI
        private void ClearXML_Click(object sender, RoutedEventArgs e)
        {
            ProteinDbObservableCollection.Clear();
            dataGridProteinDatabases.ItemsSource = ProteinDbObservableCollection;
            dataGridProteinDatabases.Items.Refresh();
        }

        //clear the list of previous analyzed databases in code and in GUI
        private void ClearReloadedXML_Click(object sender, RoutedEventArgs e)
        {
            ReloadProteinDbObservableCollection.Clear();
            dataGridReloadDb.ItemsSource = ReloadProteinDbObservableCollection;
            dataGridReloadDb.Items.Refresh();
        }

        //Clear the list of results files in code and in GUI
        private void ClearResults_Click(object sender, RoutedEventArgs e)
        {
            ResultsObservableCollection.Clear();
            dataGridResults.ItemsSource = ResultsObservableCollection;
            dataGridResults.Items.Refresh();
        }

        //Clear the list of parameters in code and in GUI
        private void ClearParameters_Click(object sender, RoutedEventArgs e)
        {
            ParametersObservableCollection.Clear();
            dataGridParameters.ItemsSource = ParametersObservableCollection;
            dataGridParameters.Items.Refresh();
        }

        //Add protein database for Digestion
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
                    bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
                    theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(filepath)).ToLowerInvariant() : theExtension;
                    var extension = System.IO.Path.GetExtension(filepath);
                    if (theExtension == ".xml" || theExtension == ".fasta" || theExtension == ".fa" )
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

        //add previously analyzed database for data reload process
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
                    bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
                    theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(filepath)).ToLowerInvariant() : theExtension;
                    var extension = System.IO.Path.GetExtension(filepath);
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

        //allows files to be dragged and dropped not just added by button selection
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
        

        //run in silico digestion and trigger result windows after complete
        private async void RunTaskButton_Click(object sender, RoutedEventArgs e)
        {
            RunTaskButton.IsEnabled = false; // disable while running

            GlobalVariables.StopLoops = false;
            
            // check for valid tasks/spectra files/protein databases
            if (ParametersViewModel.ProteaseSpecificParameters.All(p => !p.IsSelected))
            {
                MessageBox.Show("Warning: No protease has been selected. Please select at least one protease in 'Digestion Conditions' before continuing");
                RunTaskButton.IsEnabled = true;
                return;
            }
            
            if (!ProteinDbObservableCollection.Any())
            {
                MessageBox.Show("Warning: No protein databases have been provided for digestion. Add at least one protein database before proceeding with analysis.");
                RunTaskButton.IsEnabled = true;
                return;
            }

            if (!ParametersViewModel.Parameters.ProteaseSpecificParameters.Any())
            {
                MessageBox.Show("Warning: No proteases have been selected for digestion. Select at least one protease and save the updated digestion conditions before proceeding with analysis.");
                RunTaskButton.IsEnabled = true;
                return;
            }

            var task = new DigestionTask();
            task.DigestionParameters = ParametersViewModel.Parameters;
            string taskId = $"Task1-{task.TaskType}";

            // everything is OK to run
            EverythingRunnerEngine a = new EverythingRunnerEngine([(taskId, task)],
                ProteinDbObservableCollection.Select(b => new DbForDigestion(b.FilePath)).ToList(),
                OutputFolderTextBox.Text);

            ProgressBar runProgressBar = new ProgressBar();
            runProgressBar.Orientation = Orientation.Horizontal;
            runProgressBar.Width = 300;
            runProgressBar.Height = 30;
            runProgressBar.IsIndeterminate = true;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            RunStatus.Items.Add(runProgressBar);
            var results = await Task.Run(() => a.Run());
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptidesByFile = results.PeptideByFile;
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease = results.SequenceCoverageByProtease;
            stopwatch.Stop();

            runProgressBar.IsIndeterminate = false;

            // when done with tasks
            AllResultsTab.Content = new AllResultsWindow(peptidesByFile, ParametersViewModel.Parameters); // update results display
            ProteinCovMap.Content = new ProteinResultsWindow(peptidesByFile, ParametersViewModel.Parameters, sequenceCoverageByProtease);
            AllHistogramsTab.Content = new HistogramWindow(peptidesByFile, ParametersViewModel.Parameters, sequenceCoverageByProtease);
            AllResultsTab.IsSelected = true; // switch to results tab
            RunTaskButton.IsEnabled = true; // allow user to run new task
        }

        //logic for loading in results from previous runs and opening up the results windows
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
                if (line.Trim() != string.Empty) // skip empty lines
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
                List<string> proteaseNames = new List<string>();
                int missedCleavages = 0;
                int minPeptideLength = 0;
                int maxPeptideLength = 0;
                bool treatModPeps = false;
                int minPeptideMass = -1;
                int maxPeptideMass = -1;

                foreach (var parameter in fileData)
                {
                    var info = parameter.Split(": ");
                    switch (info[0])
                    {
                        case "Digestion Conditions:":
                            break;
                        case "Proteases":
                            proteaseNames = info[1].Split(",").Select(p => p.Trim()).ToList();
                            break;
                        case "Missed Cleavages":
                            missedCleavages = Convert.ToInt32(info[1]);
                            break;
                        case "Min Peptide Length":
                            minPeptideLength = Convert.ToInt32(info[1]);
                            break;
                        case "Max Peptide Length":
                            maxPeptideLength = Convert.ToInt32(info[1]);
                            break;
                        case "Treat modified peptides as different peptides":
                            if (info[1] == "True")
                            {
                                treatModPeps = true;
                            }
                            break;
                        case "Min Peptide Mass":
                            minPeptideMass = Convert.ToInt32(info[1]);
                            break;
                        case "Max Peptide Mass":
                            maxPeptideMass = Convert.ToInt32(info[1]);
                            break;
                        default:
                            // Could be protease-specific parameters or unknown
                            break;
                    }
                }

                // Create ProteaseSpecificParameters for each protease
                foreach (var proteaseName in proteaseNames)
                {
                    if (dict.ContainsKey(proteaseName))
                    {
                        DigestionParams digestionParams = new DigestionParams(
                            protease: proteaseName,
                            maxMissedCleavages: missedCleavages,
                            minPeptideLength: minPeptideLength,
                            maxPeptideLength: maxPeptideLength);

                        loadedParams.ProteaseSpecificParameters.Add(
                            new ProteaseSpecificParameters(digestionParams));
                    }
                }

                loadedParams.TreatModifiedPeptidesAsDifferent = treatModPeps;
                loadedParams.MinPeptideMassAllowed = minPeptideMass;
                loadedParams.MaxPeptideMassAllowed = maxPeptideMass;
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
                        bool unique = false;
                        if (info[12] == "True")
                        {
                            unique = true;
                        }
                        bool uniqueAll = false;
                        if (info[13] == "True")
                        {
                            uniqueAll = true;
                        }
                        bool oneDb = false;
                        if (info[14] == "True")
                        {
                            oneDb = true;
                        }
                        double hydrophobicity = Convert.ToDouble(info[15]);
                        double electrophoreticMobility = Convert.ToDouble(info[16]);

                        // Handle Chronologer RT - use -1 as default for older files without this column
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
                    var proteaseParams = loadedParams.ProteaseSpecificParameters;

                    Dictionary<Protein, List<InSilicoPep>> proteinDic = new Dictionary<Protein, List<InSilicoPep>>();

                    foreach (var protein in proteinsFromDb)
                    {
                        if (!proteinDic.ContainsKey(protein))
                        {
                            proteinDic.Add(protein, new List<InSilicoPep>() { });
                        }                        
                    }
                    Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> proteaseDic = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                    foreach (var proteaseParam in proteaseParams)
                    {
                        if (!proteaseDic.ContainsKey(proteaseParam.DigestionAgentName))
                        {
                            proteaseDic.Add(proteaseParam.DigestionAgentName, proteinDic);
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

            AllResultsTab.Content = new AllResultsWindow(PeptidesByFile, loadedParams); // update results display
            ProteinCovMap.Content = new ProteinResultsWindow(PeptidesByFile, loadedParams, seqCov);
            AllHistogramsTab.Content = new HistogramWindow(PeptidesByFile, loadedParams, seqCov);
            AllResultsTab.IsSelected = true; // switch to results tab
        }
        

        //be able to use hyperlinks to webpages
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            GlobalVariables.StartProcess(e.Uri.ToString());
        }
        
        private void OnRunTabSelection(object sender, RoutedEventArgs e)
        {
            DigestionTask task = new DigestionTask();
            task.DigestionParameters = ParametersViewModel.Parameters;

            OutputFolderTextBox.IsEnabled = true;

            GenerateRunSummary();

            // output folder
            if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
            {
                if (ProteinDbObservableCollection.Count == 0)
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
            task.DigestionParameters.OutputFolder = outputFolder;
        }
                
        // generate summary for users to see all the databases, proteases and parameters that were selected before the run is started
        private void GenerateRunSummary()
        {
            SummaryForTreeViewObservableCollection.Clear();

            RunSummaryForTreeView runSummary = new RunSummaryForTreeView("Digestion Plan:");
            CategorySummaryForTreeView databases = new CategorySummaryForTreeView("Databases:");
            foreach (var db in ProteinDbObservableCollection)
            {
                databases.Summary.Add(new FeatureForTreeView(db.FileName));
            }
            runSummary.Summary.Add(databases);
            
            CategorySummaryForTreeView proteases = new CategorySummaryForTreeView("Proteases:");
            foreach (var proteaseParam in ParametersViewModel.ProteaseSpecificParameters.Where(p => p.IsSelected))
            {
                proteases.Summary.Add(new FeatureForTreeView(proteaseParam.DigestionAgentName));
            }
            runSummary.Summary.Add(proteases);
            
            CategorySummaryForTreeView parameters = new CategorySummaryForTreeView("Digestion Parameters:");

            // Get common parameters across all proteases
            if (ParametersViewModel.ProteaseSpecificParameters.Any())
            {
                var firstParams = ParametersViewModel.ProteaseSpecificParameters.First();
                bool allSameMissedCleavages = ParametersViewModel.ProteaseSpecificParameters.All(p => p.MaxMissedCleavages == firstParams.MaxMissedCleavages);
                bool allSameMinLength = ParametersViewModel.ProteaseSpecificParameters.All(p => p.MinLength == firstParams.MinLength);
                bool allSameMaxLength = ParametersViewModel.ProteaseSpecificParameters.All(p => p.MaxLength == firstParams.MaxLength);

                if (allSameMissedCleavages)
                {
                    FeatureForTreeView missedCleavages = new FeatureForTreeView("Number of Missed Cleavages: " + firstParams.MaxMissedCleavages);
                    parameters.Summary.Add(missedCleavages);
                }
                else
                {
                    parameters.Summary.Add(new FeatureForTreeView("Number of Missed Cleavages: Varies by protease"));
                }

                if (allSameMinLength)
                {
                    FeatureForTreeView minPep = new FeatureForTreeView("Minimum Peptide Length: " + firstParams.MinLength);
                    parameters.Summary.Add(minPep);
                }
                else
                {
                    parameters.Summary.Add(new FeatureForTreeView("Minimum Peptide Length: Varies by protease"));
                }

                if (allSameMaxLength)
                {
                    FeatureForTreeView maxPep = new FeatureForTreeView("Maximum Peptide Length: " + firstParams.MaxLength);
                    parameters.Summary.Add(maxPep);
                }
                else
                {
                    parameters.Summary.Add(new FeatureForTreeView("Maximum Peptide Length: Varies by protease"));
                }
            }

            FeatureForTreeView modPep = new FeatureForTreeView("Treat Modified Peptides as Different Peptides: " + ParametersViewModel.TreatModifiedPeptidesAsDifferent);           
            FeatureForTreeView minMass = new FeatureForTreeView("Minimum Peptide Mass: " + ParametersViewModel.MinPeptideMass);                    
            FeatureForTreeView maxMass = new FeatureForTreeView("Maximum Peptide Mass: " + ParametersViewModel.MaxPeptideMass);
            
            parameters.Summary.Add(modPep);
            parameters.Summary.Add(minMass);
            parameters.Summary.Add(maxMass);
            runSummary.Summary.Add(parameters);

            SummaryForTreeViewObservableCollection.Add(runSummary);
            RunSummaryTreeView.DataContext = SummaryForTreeViewObservableCollection;
        }

        //make it easy for users to email us with issues
        private void MenuItem_EmailHelp_Click(object sender, RequestNavigateEventArgs e)
        {
            string mailto = string.Format("mailto:{0}?Subject=ProteaseGuru. Issue:", "mm_support@chem.wisc.edu");
            GlobalVariables.StartProcess(mailto);
        }
        
        //load proteins from reloaded databases
        protected List<Protein> LoadProteins(DbForDigestion database)
        {
            List<string> dbErrors = new List<string>();
            List<Protein> proteinList = new List<Protein>();

            string theExtension = System.IO.Path.GetExtension(database.FilePath).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
            theExtension = compressed ? System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(database.FilePath)).ToLowerInvariant() : theExtension;

            if (theExtension.Equals(".fasta") || theExtension.Equals(".fa"))
            {
                proteinList = ProteinDbLoader.LoadProteinFasta(database.FilePath, true, DecoyType.None, false, out dbErrors, ProteinDbLoader.UniprotAccessionRegex,
                    ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                    ProteinDbLoader.UniprotOrganismRegex,  -1);
                
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
                    //count which residues are covered at least one time by a peptide
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
                    //divide the number of covered residues by the total residues in the protein
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

        //private void MenuItem_YouTube_Click(object sender, RoutedEventArgs e)
        //{
        //    GlobalVariables.StartProcess(@"https://www.youtube.com/channel/UCwPeeXcYSQBdbfXt-SdYhEg");
        //}
    }
}
