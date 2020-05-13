using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace ProteaseGuruGUI
{
    public class ProteaseSummaryForTreeView
    {
        public ProteaseSummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
            Summary = new ObservableCollection<SummaryForTreeView>();
            Expanded = true;
        }

        public string DisplayName { get; }

        public ObservableCollection<SummaryForTreeView> Summary { get; }

        public bool Expanded { get; set; }

    }

    public class SummaryForTreeView
    {
        public SummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
