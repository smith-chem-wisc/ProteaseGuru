using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace GUI
{
    public class RunSummaryForTreeView
    {
        public RunSummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
            Summary = new ObservableCollection<CategorySummaryForTreeView>();
            Expanded = true;
        }

        public string DisplayName { get; }

        public ObservableCollection<CategorySummaryForTreeView> Summary { get; }        

        public bool Expanded { get; set; }

    }

    public class CategorySummaryForTreeView
    {
        public CategorySummaryForTreeView(string displayName)
        {
            DisplayName = displayName;
            Summary = new ObservableCollection<FeatureForTreeView>();
            Expanded = true;
        }

        public string DisplayName { get; }

        public ObservableCollection<FeatureForTreeView> Summary { get; }

        public bool Expanded { get; set; }

    }

    public class FeatureForTreeView
    {
        public FeatureForTreeView(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
