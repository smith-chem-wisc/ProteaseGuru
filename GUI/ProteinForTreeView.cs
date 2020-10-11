using Proteomics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Tasks;

namespace ProteaseGuruGUI
{
    //gives structure for protein information in protein search window
    class ProteinForTreeView
    {
        public ProteinForTreeView(Protein protein, string displayName, List<InSilicoPep> uniquePep,
            List<InSilicoPep> sharedPep, List<InSilicoPep> allPep)
        {
            Protein = protein;
            Expanded = false;
            DisplayName = displayName;
            UniquePeptides = uniquePep;
            SharedPeptides = sharedPep;
            AllPeptides = allPep;
            Summary = new ObservableCollection<SummaryForTreeView>();
        }

        public Protein Protein { get; }
        public List<InSilicoPep> UniquePeptides { get; }
        public List<InSilicoPep> SharedPeptides { get; }
        public List<InSilicoPep> AllPeptides { get; }
        public string DisplayName { get; }
        public bool Expanded { get; set; }
        public ObservableCollection<SummaryForTreeView> Summary { get; set; }
    }
}
