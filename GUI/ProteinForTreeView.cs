using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProteaseGuruGUI
{
    class ProteinForTreeView
    {
        public ProteinForTreeView(string displayName, string accession, List<string> uniquePep, List<string> sharedPep, List<string> allPep)
        {
            Expanded = false;
            DisplayName = displayName;
            Accession = accession;
            UniquePeptides = uniquePep;
            SharedPeptides = sharedPep;
            AllPeptides = allPep;
            Summary = new ObservableCollection<SummaryForTreeView>();
        }

        public List<string> UniquePeptides { get; }
        public List<string> SharedPeptides { get; }
        public List<string> AllPeptides { get; }
        public string DisplayName { get; }
        public string Accession { get; }
        public bool Expanded { get; set; }
        public ObservableCollection<SummaryForTreeView> Summary { get; set; }
    }
}
