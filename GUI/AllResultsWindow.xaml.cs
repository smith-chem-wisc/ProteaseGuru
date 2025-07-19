using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
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
        private readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;        
        Parameters UserParams;
        public Dictionary<string, Dictionary<string, string>> HistogramDataTable = new Dictionary<string, Dictionary<string, string>>();

        public AllResultsWindow()
        {
        }

        //Sets up the All ResultsWindow
        public AllResultsWindow(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile, Parameters userParams) // change constructor to receive analysis information
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
                Dictionary<string, List<InSilicoPep>> allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
                             
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
                    Dictionary<string, List<InSilicoPep>> peptidesToProteins = new Dictionary<string, List<InSilicoPep>>();

                    if (UserParams.TreatModifiedPeptidesAsDifferent)
                    {
                        peptidesToProteins = protease.Value.GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    else
                    {
                        peptidesToProteins = protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    var unique = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1).ToList();
                    var shared = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() > 1).ToList();
                    var sharedPeptidesInOneDb = shared.Where(p => p.Value.Select(p => p.Database).Distinct().Count() == 1);
                    var uniquePeptidesInOneDb = unique.Where(p => p.Value.Select(p => p.Database).Distinct().Count() == 1);

                    List<InSilicoPep> peptidesInOneDb = new List<InSilicoPep>();
                    int sharedCount = shared.Count;
                    int uniqueCount = unique.Count;

                    foreach (var entry in unique)
                    {
                        if (entry.Value.Select(p => p.Database).Distinct().ToList().Count > 1)
                        {
                            uniqueCount = uniqueCount - 1;
                            sharedCount = sharedCount + 1;
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
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                    thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + peptidesToProteins.Count()));
                    var peptidesForSingleDatabase = peptidesInOneDb.GroupBy(p => p.Database).ToDictionary(group => group.Key, group => group.ToList());


                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptide Sequences: " + uniqueCount));
                    thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptide Sequences: " + sharedCount));

                    foreach (var db in peptidesForSingleDatabase)
                    {
                        if (UserParams.TreatModifiedPeptidesAsDifferent)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptide Sequences Found Only in " + db.Key + ": " + db.Value.Select(p => p.FullSequence).Distinct().Count()));
                        }
                        else
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptide Sequences Found Only in " + db.Key + ": " + db.Value.Select(p => p.BaseSequence).Distinct().Count()));
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
                        var allPeptides = protease.Value.SelectMany(p => p.Value);
                        if (UserParams.TreatModifiedPeptidesAsDifferent)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.FullSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.FullSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.FullSequence).Distinct().Count()));
                        }
                        else 
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.BaseSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.BaseSequence).Distinct().Count()));
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
                        var  allPeptides = protease.Value.SelectMany(p => p.Value);
                        if (UserParams.TreatModifiedPeptidesAsDifferent)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.FullSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.FullSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.FullSequence).Distinct().Count()));
                        }
                        else 
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.BaseSequence).Distinct().Count()));
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.BaseSequence).Distinct().Count()));
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
