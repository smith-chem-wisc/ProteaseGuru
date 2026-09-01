using System;
using System.Collections.Generic;
using System.Text;
using ProteaseGuru.Engine;

namespace ProteaseGuru.Tasks
{
    public abstract class ProteaseGuruTask
    {
        public enum MyTask
        {
            Digestion,
            PeptideResultsAnalysis
        }

        protected string OutputFolder { get; private set; } = string.Empty;

        protected MyTaskResults? MyTaskResults;

        protected ProteaseGuruTask(MyTask taskType)
        {
            this.TaskType = taskType;
        }

        public abstract MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList);
        public abstract MyTaskResults RunSpecific(MyTaskResults digestionResults, List<string> peptideFilePaths);
        public MyTask TaskType { get; set; }

        protected static void Main()
        {

        }
    }
}
