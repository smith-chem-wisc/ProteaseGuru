using System.Collections.ObjectModel;
using System.Windows.Controls;
using Engine;
using Omics;
using Proteomics;
using Tasks;

namespace GUI
{
    /// <summary>
    /// Interaction logic for AllResultsWindow.xaml
    /// </summary>
    public partial class AllResultsWindow : UserControl
    {
        private readonly ObservableCollection<ProteaseSummaryForTreeView> SummaryForTreeViewObservableCollection;           
        private readonly Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> PeptideByFile;        
        RunParameters UserParams;
        public Dictionary<string, Dictionary<string, string>> HistogramDataTable = new();

        public AllResultsWindow()
        {
        }

        //Sets up the All ResultsWindow
        public AllResultsWindow(Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile, RunParameters userParams) // change constructor to receive analysis information
        {
            InitializeComponent();
            PeptideByFile = peptideByFile;
            UserParams = userParams;
            SummaryForTreeViewObservableCollection = new ObservableCollection<ProteaseSummaryForTreeView>();
            GenerateResultsSummary();
        } 
        
        //Code for the generation of the digestion results summary that is displayed in TreeView        
        private void GenerateResultsSummary()
        {
            if (PeptideByFile.Count > 1) // if there is more than one database then we need to do all database summary 
            {
                ProteaseSummaryForTreeView allDatabases = new ProteaseSummaryForTreeView("Cumulative Database Results:");
                //get all the peptides from all the databases together
                Dictionary<string, List<InSilicoPep>> allDatabasePeptidesByProtease = new();
                             
                foreach (var database in PeptideByFile)
                {
                    foreach (var protease in database.Value)
                    {                        
                        if (allDatabasePeptidesByProtease.ContainsKey(protease.Key))
                        {
                            foreach (var protein in protease.Value)
                            {
                                allDatabasePeptidesByProtease[protease.Key].AddRange(protein.Value);
                            }
                        }
                        else
                        {                            
                            allDatabasePeptidesByProtease.Add(protease.Key, protease.Value.SelectMany(p=>p.Value).ToList());
                        }
                        
                    }                        
                }

                foreach (var protease in allDatabasePeptidesByProtease)
                {
                    Dictionary<string, List<InSilicoPep>> peptidesToProteins = new();

                    if (UserParams.TreatModifiedPeptidesAsDifferent)
                    {
                        peptidesToProteins = protease.Value.GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    else
                    {
                        peptidesToProteins = protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    var unique = peptidesToProteins.Where(p => p.Value.DistinctBy(p => p.Protein).Count() == 1).ToList();
                    var shared = peptidesToProteins.Where(p => p.Value.DistinctBy(p => p.Protein).Count() > 1).ToList();
                    var sharedPeptidesInOneDb = shared.Where(p => p.Value.DistinctBy(p => p.Database).Count() == 1);
                    var uniquePeptidesInOneDb = unique.Where(p => p.Value.DistinctBy(p => p.Database).Count() == 1);

                    List<InSilicoPep> peptidesInOneDb = new();
                    int sharedCount = shared.Count;
                    int uniqueCount = unique.Count;
                    int uniqueDetectableCount = unique.Count(p => p.Value.Any(pep => pep.PflyDetectability == true));
                    int sharedDetectableCount = shared.Count(p => p.Value.Any(pep => pep.PflyDetectability == true));

                    foreach (var entry in unique)
                    {
                        if (entry.Value.DistinctBy(p => p.Database).ToList().Count > 1)
                        {
                            uniqueCount = uniqueCount - 1;
                            sharedCount = sharedCount + 1;
                            if (entry.Value.Any(p => p.PflyDetectability == true))
                            {
                                uniqueDetectableCount--;
                                sharedDetectableCount++;
                            }
                        }

                    }

                    foreach (var pep in uniquePeptidesInOneDb)
                    {
                        peptidesInOneDb.AddRange(pep.Value);
                    }

                    foreach (var pep in sharedPeptidesInOneDb)
                    {
                        peptidesInOneDb.AddRange(pep.Value);
                    }

                    
                    string prot = protease.Key;
                    DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                    
                    List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();
                    List<InSilicoPep> allDetectable = allPeptides.Where(p => p.PflyDetectability == true).ToList();
                    thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()}: " + allPeptides.Count + $" ({allDetectable.Count})"));
                    thisDigestion.Summary.Add(new SummaryForTreeView($"     Number of Distinct {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + peptidesToProteins.Count + $" ({peptidesToProteins.Count(p => p.Value.Any(isp => isp.PflyDetectability == true))})"));
                    var peptidesForSingleDatabase = peptidesInOneDb.GroupBy(p => p.Database).ToDictionary(group => group.Key, group => group.ToList());

                    thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Unique {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + uniqueCount + $" ({uniqueDetectableCount})"));
                    thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Shared {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + sharedCount + $" ({sharedDetectableCount})"));

                    foreach (var db in peptidesForSingleDatabase)
                    {
                        if (UserParams.TreatModifiedPeptidesAsDifferent)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences Found Only in " + db.Key + ": " + db.Value.DistinctBy(p => p.FullSequence).Count() + $" ({db.Value.Where(p=>p.PflyDetectability == true).DistinctBy(p => p.FullSequence).Count()})"));
                        }
                        else
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences Found Only in " + db.Key + ": " + db.Value.DistinctBy(p => p.BaseSequence).Count() + $" ({db.Value.Where(p => p.PflyDetectability == true).DistinctBy(p => p.BaseSequence).Count()})"));
                        }
                        
                    }

                    allDatabases.Summary.Add(thisDigestion);
                }

                //put the results summary in the GUI for users to view
                SummaryForTreeViewObservableCollection.Add(allDatabases);
                //Now do a similar results summary for each individual database on its own
                foreach (var database in PeptideByFile)
                {
                    ProteaseSummaryForTreeView thisProtease = new ProteaseSummaryForTreeView(database.Key+ " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                        var allPeptides = protease.Value.SelectMany(p => p.Value).ToList();
                        var allDetectable = allPeptides.Where(p => p.PflyDetectability == true).ToList();
                        if (UserParams.TreatModifiedPeptidesAsDifferent)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Count + $" ({allDetectable.Count})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"     Number of Distinct {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + allPeptides.DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.DistinctBy(p => p.FullSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Unique {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == true).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == true).DistinctBy(p => p.FullSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Shared {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == false).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == false).DistinctBy(p => p.FullSequence).Count()})"));
                        }
                        else 
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Count + $" ({allDetectable.Count})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"     Number of Distinct {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + allPeptides.DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.DistinctBy(p => p.BaseSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Unique {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == true).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == true).DistinctBy(p => p.BaseSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Shared {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == false).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == false).DistinctBy(p => p.BaseSequence).Count()})"));
                        }                       

                        thisProtease.Summary.Add(thisDigestion);
                    }
                    //Put the database specific results summary in the GUI
                    SummaryForTreeViewObservableCollection.Add(thisProtease);
                }
                
            }
            else // if there is only one database then is results and all database results are the same thing
            {
                foreach (var database in PeptideByFile)
                {
                    ProteaseSummaryForTreeView thisProtease = new ProteaseSummaryForTreeView(database.Key + " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView( prot + " Results:");                        
                        var allPeptides = protease.Value.SelectMany(p => p.Value).ToList();
                        var allDetectable = allPeptides.Where(p => p.PflyDetectability == true).ToList();
                        if (UserParams.TreatModifiedPeptidesAsDifferent)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Count + $" ({allDetectable.Count})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"     Number of Distinct {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + allPeptides.DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.DistinctBy(p => p.FullSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Unique {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == true).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == true).DistinctBy(p => p.FullSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Shared {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == false).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == false).DistinctBy(p => p.FullSequence).Count()})"));
                        }
                        else
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Count + $" ({allDetectable.Count})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"     Number of Distinct {GlobalVariables.AnalyteType.GetUniqueFormLabel()} Sequences: " + allPeptides.DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.DistinctBy(p => p.BaseSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Unique {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == true).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == true).DistinctBy(p => p.BaseSequence).Count()})"));
                            thisDigestion.Summary.Add(new SummaryForTreeView($"Number of Shared {GlobalVariables.AnalyteType.GetUniqueFormLabel()}s: " + allPeptides.Where(pep => pep.Unique == false).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique == false).DistinctBy(p => p.BaseSequence).Count()})"));
                        }
                                     
                        thisProtease.Summary.Add(thisDigestion);
                    }
                    SummaryForTreeViewObservableCollection.Add(thisProtease);
                }
            }
            //Results are provided to the user at this point
            ProteaseSummaryTreeView.DataContext = SummaryForTreeViewObservableCollection;          
        }       

    }
}
