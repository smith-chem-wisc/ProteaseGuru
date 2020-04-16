using Engine;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public abstract class ProteaseGuruTask
    {
        public enum MyTask
        {
            Digestion,
            PeptideResultsAnalysis            
        }
        protected string OutputFolder { get; private set; }

        protected MyTaskResults MyTaskResults;

        protected ProteaseGuruTask(MyTask taskType)
        {
            this.TaskType = taskType;
        }
        public abstract MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList);
        public abstract MyTaskResults RunSpecific(MyTaskResults digestionResults, List<string> peptideFilePaths);

        public static event EventHandler<SingleTaskEventArgs> FinishedSingleTaskHandler;

        public static event EventHandler<SingleFileEventArgs> FinishedWritingFileHandler;

        public static event EventHandler<SingleTaskEventArgs> StartingSingleTaskHander;

        public static event EventHandler<StringEventArgs> StartingDataFileHandler;

        public static event EventHandler<StringEventArgs> FinishedDataFileHandler;

        public static event EventHandler<StringEventArgs> OutLabelStatusHandler;

        public static event EventHandler<StringEventArgs> WarnHandler;

        public static event EventHandler<StringEventArgs> LogHandler;

        public static event EventHandler<StringEventArgs> NewCollectionHandler;


        public MyTask TaskType { get; set; }
    }
}
