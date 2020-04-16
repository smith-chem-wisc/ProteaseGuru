using Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tasks
{
    public class EverythingRunnerEngine
    {
        private readonly List<(string, ProteaseGuruTask)> RunList;
        private string OutputFolder;
        private List<DbForDigestion> CurrentXmlDbFilenameList;

        public EverythingRunnerEngine(List<(string,ProteaseGuruTask)> runList, List<DbForDigestion> startingXmlDbFilenameList, string outputFolder)
        {
            RunList = runList;
            OutputFolder = outputFolder.Trim('"');            
            CurrentXmlDbFilenameList = startingXmlDbFilenameList;
        }

        public static event EventHandler<StringEventArgs> FinishedWritingAllResultsFileHandler;

        public static event EventHandler StartingAllTasksEngineHandler;

        public static event EventHandler<StringEventArgs> FinishedAllTasksEngineHandler;

        public static event EventHandler<XmlForTaskListEventArgs> NewDbsHandler;        

        public static event EventHandler<StringEventArgs> WarnHandler;

        public void Run()
        {
            StartingAllTasks();
            var stopWatch = new Stopwatch();
            stopWatch.Start();            

            var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);

            OutputFolder = OutputFolder.Replace("$DATETIME", startTimeForAllFilenames);

            StringBuilder allResultsText = new StringBuilder();

            for (int i = 0; i < RunList.Count; i++)
            {
                
                if (!CurrentXmlDbFilenameList.Any())
                {
                    Warn("Cannot proceed. No protein database files selected.");
                    FinishedAllTasks(OutputFolder);
                    return;
                }
                var ok = RunList[i];
                
                var outputFolderForThisTask = System.IO.Path.Combine(OutputFolder, "ProteaseGuruResults_"+ i);

                if (!Directory.Exists(outputFolderForThisTask))
                    Directory.CreateDirectory(outputFolderForThisTask);

                // Actual task running code
                var myTaskResults = ok.Item2.RunSpecific(outputFolderForThisTask, CurrentXmlDbFilenameList);
                                
                allResultsText.AppendLine(Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine + myTaskResults.ToString());
            }
            stopWatch.Stop();
            var resultsFileName = Path.Combine(OutputFolder, "allResults.txt");
            using (StreamWriter file = new StreamWriter(resultsFileName))
            {
                file.WriteLine("MetaMorpheus: version " + GlobalVariables.MetaMorpheusVersion);
                file.WriteLine("Total time: " + stopWatch.Elapsed);
                file.Write(allResultsText.ToString());
            }
            FinishedWritingAllResultsFileHandler?.Invoke(this, new StringEventArgs(resultsFileName, null));
            FinishedAllTasks(OutputFolder);
        }

        private void Warn(string v)
        {
            WarnHandler?.Invoke(this, new StringEventArgs(v, null));
        }

        private void StartingAllTasks()
        {
            StartingAllTasksEngineHandler?.Invoke(this, EventArgs.Empty);
        }

        private void FinishedAllTasks(string rootOutputDir)
        {
            FinishedAllTasksEngineHandler?.Invoke(this, new StringEventArgs(rootOutputDir, null));
        }   
    }
}
