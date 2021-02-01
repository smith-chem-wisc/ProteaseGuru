﻿using Engine;
using FlashLFQ;
using Proteomics;
using Proteomics.ProteolyticDigestion;
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

        public Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;

        public MyTaskResults Run()
        {
            StartingAllTasks();
            var stopWatch = new Stopwatch();
            stopWatch.Start();            

            var startTimeForAllFilenames = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);

            OutputFolder = OutputFolder.Replace("$DATETIME", startTimeForAllFilenames);

            StringBuilder allResultsText = new StringBuilder();
            MyTaskResults results = null;

            for (int i = 0; i < RunList.Count; i++)
            {
                
                if (!CurrentXmlDbFilenameList.Any())
                {
                    Warn("Cannot proceed. No protein database files selected.");
                    FinishedAllTasks(OutputFolder);
                    break;
                }
                var ok = RunList[i];
                
                var outputFolderForThisTask = System.IO.Path.Combine(OutputFolder, "ProteaseGuruDigestionResults");

                if (!Directory.Exists(outputFolderForThisTask))
                    Directory.CreateDirectory(outputFolderForThisTask);

                // Actual task running code
                var myTaskResults = ok.Item2.RunSpecific(outputFolderForThisTask, CurrentXmlDbFilenameList);
                results = myTaskResults;

                PeptideByFile = myTaskResults.PeptideByFile;

                allResultsText.AppendLine(Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine + myTaskResults.ToString());
            }
            stopWatch.Stop();
            var resultsFileName = Path.Combine(OutputFolder, "allResults.txt");
            using (StreamWriter file = new StreamWriter(resultsFileName))
            {
                file.WriteLine("ProteaseGuru: Version " + GlobalVariables.ProteaseGuruVersion);
                file.WriteLine("Total time: " + stopWatch.Elapsed);
                file.Write(allResultsText.ToString());
            }
            FinishedWritingAllResultsFileHandler?.Invoke(this, new StringEventArgs(resultsFileName, null));
            FinishedAllTasks(OutputFolder);
            return results;
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
