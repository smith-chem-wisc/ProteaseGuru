using System.Collections.ObjectModel;

namespace GUI
{
    public class ProteinSummaryForTreeView
    {
        public ProteinSummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
            Summary = new ObservableCollection<AnalysisSummaryForTreeView>();
            Expanded = true;
        }

        public string DisplayName { get; }

        public ObservableCollection<AnalysisSummaryForTreeView> Summary { get; }        

        public bool Expanded { get; set; }

    }

    public class AnalysisSummaryForTreeView
    {
        public AnalysisSummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
            Summary = new ObservableCollection<ProtSummaryForTreeView>();
            Expanded = false;
        }

        public string DisplayName { get; }

        public ObservableCollection<ProtSummaryForTreeView> Summary { get; }

        public bool Expanded { get; set; }

    }

    public class ProtSummaryForTreeView
    {
        public ProtSummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
