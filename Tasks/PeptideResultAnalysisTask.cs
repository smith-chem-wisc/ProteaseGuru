using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class PeptideResultAnalysisTask: ProteaseGuruTask
    {
        public PeptideResultAnalysisTask() : base(MyTask.PeptideResultsAnalysis)
        { 
        
        }

        public override MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList)
        {
            throw new NotImplementedException();
        }

        public override MyTaskResults RunSpecific(MyTaskResults digestionResults, List<string> peptideFilePaths)
        {
            MyTaskResults results = new MyTaskResults(this);
            return results;

        }
    }
}
