using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class MyTaskResults
    {        
        public TimeSpan Time;

        private readonly List<string> resultTexts;

        private readonly StringBuilder TaskSummaryText = new StringBuilder();
        private readonly StringBuilder PsmPeptideProteinSummaryText = new StringBuilder();

        internal MyTaskResults(ProteaseGuruTask s)
        {
            resultTexts = new List<string>();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Time to run task: " + Time);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("--------------------------------------------------");
            
            return sb.ToString();
        }

        internal void AddResultText(string resultsText)
        {
            resultTexts.Add(resultsText);
        }

        internal void AddPsmPeptideProteinSummaryText(string targetTextString)
        {
            PsmPeptideProteinSummaryText.Append(targetTextString);
        }

        internal void AddTaskSummaryText(string niceTextString)
        {
            TaskSummaryText.AppendLine(niceTextString);
        }
    }
}
