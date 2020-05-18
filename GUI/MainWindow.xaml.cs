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

namespace GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ProteinDbForDataGrid> ProteinDbObservableCollection = new ObservableCollection<ProteinDbForDataGrid>();
        private readonly ObservableCollection<PreRunTask> StaticTasksObservableCollection = new ObservableCollection<PreRunTask>();
        private ObservableCollection<InRunTask> DynamicTasksObservableCollection;
        public MainWindow()
        {
            InitializeComponent();
            PopulateProteaseList();
            dataGridProteinDatabases.DataContext = ProteinDbObservableCollection;
            EverythingRunnerEngine.NewDbsHandler += AddNewDB;
            EverythingRunnerEngine.WarnHandler += GuiWarnHandler;
        }

        private void AddProteinDatabase_Click(object sender, MouseButtonEventArgs e)
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
                    AddAFile(filepath);
                }
            }

            dataGridProteinDatabases.Items.Refresh();
        }

        private void ClearXML_Click(object sender, MouseButtonEventArgs e)
        {
            ProteinDbObservableCollection.Clear();
        }

        private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var ye = sender as DataGridCell;

            //// prevent opening protein DB or spectra files if a run is in progress
            //if ((ye.DataContext is ProteinDbForDataGrid || ye.DataContext is RawDataForDataGrid) && !LoadTaskButton.IsEnabled)
            //{
            //    return;
            //}

            //// open the file with the default process for this file format
            //if (ye.Content is TextBlock hm && hm != null && !string.IsNullOrEmpty(hm.Text))
            //{
            //    try
            //    {
            //        GlobalVariables.StartProcess(hm.Text);
            //    }
            //    catch (Exception)
            //    {
            //    }
            //}
        }
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
        private bool DatabaseExists(ObservableCollection<ProteinDbForDataGrid> pDOC, ProteinDbForDataGrid uuu)
        {
            foreach (ProteinDbForDataGrid pdoc in pDOC)
            {
                if (pdoc.FilePath == uuu.FilePath) { return true; }
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
        private void UpdateFieldsFromUser(DigestionTask run)
        {
            run.DigestionParameters.NumberOfMissedCleavagesAllowed = Convert.ToInt32(MissedCleavagesTextBox.Text);
            run.DigestionParameters.MinPeptideLengthAllowed = Convert.ToInt32(MinPeptideLengthTextBox.Text);
            run.DigestionParameters.MaxPeptideLengthAllowed = Convert.ToInt32(MaxPeptideLengthTextBox.Text);
            run.DigestionParameters.TreatModifiedPeptidesAsDifferent = Convert.ToBoolean(ModPepsAreUnique.IsChecked);
            List<Protease> proteases = new List<Protease>();
            foreach (var protease in ProteaseSelectedForUse.SelectedItems)
            {
                proteases.Add(ProteaseDictionary.Dictionary[protease.ToString()]);
            }
            run.DigestionParameters.ProteasesForDigestion = proteases;
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
                }

                dataGridProteinDatabases.Items.Refresh();
            }
        }

        private void UpdateOutputFolderTextbox()
        {
            if (ProteinDbObservableCollection.Any())
            {
                // if current output folder is blank and there is a database, use the file's path as the output path
                if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
                {
                    var pathOfFirstSpectraFile = System.IO.Path.GetDirectoryName(ProteinDbObservableCollection.First().FilePath);
                    OutputFolderTextBox.Text = System.IO.Path.Combine(pathOfFirstSpectraFile, @"$DATETIME");
                }
                // else do nothing (do not override if there is a path already there; might clear user-defined path)
            }
            else
            {
                // no spectra files; clear the output folder from the GUI
                OutputFolderTextBox.Clear();
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

        private void RunAllTasks_Click(object sender, RoutedEventArgs e)
        {
            GlobalVariables.StopLoops = false;

            // check for valid tasks/spectra files/protein databases

            if (!ProteinDbObservableCollection.Any())
            {
                GuiWarnHandler(null, new StringEventArgs("You need to add at least one protein database!", null));
                return;
            }

            DynamicTasksObservableCollection = new ObservableCollection<InRunTask>();

            for (int i = 0; i < StaticTasksObservableCollection.Count; i++)
            {
                DynamicTasksObservableCollection.Add(new InRunTask("Task" + (i + 1) + "-" + StaticTasksObservableCollection[i].proteaseGuruTask.TaskType.ToString(), StaticTasksObservableCollection[i].proteaseGuruTask));
            }


            // output folder
            if (string.IsNullOrEmpty(OutputFolderTextBox.Text))
            {
                var pathOfFirstSpectraFile = System.IO.Path.GetDirectoryName(ProteinDbObservableCollection.First().FilePath);
                OutputFolderTextBox.Text = System.IO.Path.Combine(pathOfFirstSpectraFile, @"$DATETIME");
            }

            var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
            string outputFolder = OutputFolderTextBox.Text.Replace("$DATETIME", startTimeForAllFilenames);
            OutputFolderTextBox.Text = outputFolder;

            // everything is OK to run
            var taskList = DynamicTasksObservableCollection.Select(b => (b.DisplayName, b.Task)).ToList();
            var databaseList = ProteinDbObservableCollection.Select(b => new DbForDigestion(b.FilePath)).ToList();
            EverythingRunnerEngine a = new EverythingRunnerEngine(taskList, databaseList, outputFolder);

            var t = new Task(a.Run);
            t.ContinueWith(EverythingRunnerExceptionHandler, TaskContinuationOptions.OnlyOnFaulted);
            t.Start();
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
                string tomlText = "";
                if (Directory.Exists(outputFolder))
                {
                    var tomls = Directory.GetFiles(outputFolder, "*.toml");
                    //will only be 1 toml per task
                    foreach (var tomlFile in tomls)
                    {
                        tomlText += "\n" + File.ReadAllText(tomlFile);
                    }

                    if (!tomls.Any())
                    {
                        tomlText = "TOML not found";
                    }
                }
                else
                {
                    tomlText = "Directory not found";
                }

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    string body = exception.Message + "%0D%0A" + exception.Data +
                       "%0D%0A" + exception.StackTrace +
                       "%0D%0A" + exception.Source +
                       "%0D%0A %0D%0A %0D%0A %0D%0A SYSTEM INFO: %0D%0A " +
                        SystemInfo.CompleteSystemInfo() +
                       "%0D%0A%0D%0A MetaMorpheus: version " + GlobalVariables.MetaMorpheusVersion
                       + "%0D%0A %0D%0A %0D%0A %0D%0A TOML: %0D%0A " +
                       tomlText;
                    body = body.Replace('&', ' ');
                    body = body.Replace("\n", "%0D%0A");
                    body = body.Replace("\r", "%0D%0A");
                    string mailto = string.Format("mailto:{0}?Subject=MetaMorpheus. Issue:&Body={1}", "mm_support@chem.wisc.edu", body);
                    GlobalVariables.StartProcess(mailto);
                    Console.WriteLine(body);
                }

            }

        }
        private void AddDigestionTask_Click(object sender, RoutedEventArgs e)
        {
            DigestionTask task = new DigestionTask();
            UpdateFieldsFromUser(task);
            AddTaskToCollection(task);
        }

        private void AddPeptidePsmTsvFiles_Click(object sender, RoutedEventArgs e)
        {
            PeptideResultAnalysisTask task = null;
            var dialog = new PeptideResultAnalysisWindow(task);
            if (dialog.ShowDialog() == true)
            {
                AddTaskToCollection(dialog.TheTask);
            }
        }

        private void AddTaskToCollection(ProteaseGuruTask task)
        {
            PreRunTask pre = new PreRunTask(task);
            StaticTasksObservableCollection.Add(pre);
        }

        private void ClearXML_Click(object sender, RoutedEventArgs e)
        {
            ProteinDbObservableCollection.Clear();
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
                    AddAFile(filepath);
                }
            }

            dataGridProteinDatabases.Items.Refresh();
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

        private void PopulateProteaseList()
        {
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
            foreach (Protease protease in dict.Values)
            {
                ProteaseSelectedForUse.Items.Add(protease);
            }
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
        private void RunTaskButton_Click(object sender, RoutedEventArgs e)
        {
            GlobalVariables.StopLoops = false;
            
            // check for valid tasks/spectra files/protein databases
            if (!StaticTasksObservableCollection.Any())
            {
                GuiWarnHandler(null, new StringEventArgs("You need to add at least one task!", null));
                return;
            }
            
            if (!ProteinDbObservableCollection.Any())
            {
                GuiWarnHandler(null, new StringEventArgs("You need to add at least one protein database!", null));
                return;
            }

            DynamicTasksObservableCollection = new ObservableCollection<InRunTask>();

            for (int i = 0; i < StaticTasksObservableCollection.Count; i++)
            {
                DynamicTasksObservableCollection.Add(new InRunTask("Task" + (i + 1) + "-" + StaticTasksObservableCollection[i].proteaseGuruTask.TaskType, StaticTasksObservableCollection[i].proteaseGuruTask));
            }
            
            // output folder
            if (string.IsNullOrEmpty(OutputFolderTextBox.Text))
            {
                var pathOfFirstSpectraFile = System.IO.Path.GetDirectoryName(ProteinDbObservableCollection.First().FilePath);
                OutputFolderTextBox.Text = System.IO.Path.Combine(pathOfFirstSpectraFile, @"$DATETIME");
            }

            var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
            string outputFolder = OutputFolderTextBox.Text.Replace("$DATETIME", startTimeForAllFilenames);
            OutputFolderTextBox.Text = outputFolder;
            
            // everything is OK to run
            EverythingRunnerEngine a = new EverythingRunnerEngine(DynamicTasksObservableCollection.Select(b => (b.DisplayName, b.Task)).ToList(),
                ProteinDbObservableCollection.Select(b => new DbForDigestion(b.FilePath)).ToList(),
                outputFolder);

            var t = new Task(a.Run);
            t.ContinueWith(EverythingRunnerExceptionHandler, TaskContinuationOptions.OnlyOnFaulted);
            t.Start();

        }
        private void CheckIfNumber(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !CheckIsNumber(e.Text);
        }
        public static bool CheckIsNumber(string text)
        {
            bool result = true;
            foreach (var character in text)
            {
                if (!Char.IsDigit(character) && !(character == '.') && !(character == '-'))
                {
                    result = false;
                }
            }
            return result;
        }
    }
}
