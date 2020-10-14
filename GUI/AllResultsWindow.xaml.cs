using GUI;
using OxyPlot;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Tasks;


namespace ProteaseGuruGUI
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
               
                // if we want modified peptides to be treated differently than unmodified peptides we must use the FullSequence for unique peptide determination
                if (UserParams.TreatModifiedPeptidesAsDifferent)
                {
                    foreach (var protease in allDatabasePeptidesByProtease)
                    {
                        string prot = protease.Key;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                        var peptidesToProteins = protease.Value.GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList());
                        List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.FullSequence).Distinct().Count()));
                        var uniquePeptides = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1);
                        var sharedPeptides = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() > 1);
                        var sharedPeptidesInOneDb = sharedPeptides.Where(p => p.Value.Select(p => p.Database).Distinct().Count() == 1);
                        Dictionary<string, List<string>> peptidesForSingleDatabase = new Dictionary<string, List<string>>();
                        foreach (var entry in uniquePeptides)
                        {
                            var database = entry.Value.Select(p => p.Database).Distinct().FirstOrDefault();
                            if (peptidesForSingleDatabase.ContainsKey(database))
                            {
                                peptidesForSingleDatabase[database].Add(entry.Key);
                            }
                            else
                            {
                                peptidesForSingleDatabase.Add(database, new List<string>() { entry.Key });
                            }
                        }
                        foreach (var entry in sharedPeptidesInOneDb)
                        {
                            var database = entry.Value.Select(p => p.Database).Distinct().FirstOrDefault();
                            if (peptidesForSingleDatabase.ContainsKey(database))
                            {
                                peptidesForSingleDatabase[database].Add(entry.Key);
                            }
                            else
                            {
                                peptidesForSingleDatabase.Add(database, new List<string>() { entry.Key });
                            }
                        }

                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptide Sequences: " + uniquePeptides.Count()));                        
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptide Sequences: " + sharedPeptides.Count()));

                        foreach (var db in peptidesForSingleDatabase)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptide Sequences Found Only in "+db.Key+": " + db.Value.Count()));
                        }


                        allDatabases.Summary.Add(thisDigestion);

                    }
                }
                // We don't care about distinguishing modified and unmodified peptides so we use BaseSequence
                else 
                {
                    foreach (var protease in allDatabasePeptidesByProtease)
                    {
                        string prot = protease.Key;
                        DigestionSummaryForTreeView thisDigestion = new DigestionSummaryForTreeView(prot + " Results:");
                        var peptidesToProteins = protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                        List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptides: " + allPeptides.Count));
                        thisDigestion.Summary.Add(new SummaryForTreeView("     Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count()));
                        var uniquePeptides = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1);
                        var sharedPeptides = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() > 1);
                        var sharedPeptidesInOneDb = sharedPeptides.Where(p => p.Value.Select(p => p.Database).Distinct().Count() == 1);                        
                        Dictionary<string, List<string>> peptidesForSingleDatabase = new Dictionary<string, List<string>>();
                        foreach (var entry in uniquePeptides)
                        {
                            var database = entry.Value.Select(p => p.Database).Distinct().FirstOrDefault();
                            if (peptidesForSingleDatabase.ContainsKey(database))
                            {
                                peptidesForSingleDatabase[database].Add(entry.Key);
                            }
                            else
                            {
                                peptidesForSingleDatabase.Add(database, new List<string>() { entry.Key });
                            }
                        }
                        foreach (var entry in sharedPeptidesInOneDb)
                        {
                            var database = entry.Value.Select(p => p.Database).Distinct().FirstOrDefault();
                            if (peptidesForSingleDatabase.ContainsKey(database))
                            {
                                peptidesForSingleDatabase[database].Add(entry.Key);
                            }
                            else
                            {
                                peptidesForSingleDatabase.Add(database, new List<string>() { entry.Key });
                            }
                        }

                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Unique Peptide Sequences: " + uniquePeptides.Count()));
                        thisDigestion.Summary.Add(new SummaryForTreeView("Number of Shared Peptide Sequences: " + sharedPeptides.Count()));

                        foreach (var db in peptidesForSingleDatabase)
                        {
                            thisDigestion.Summary.Add(new SummaryForTreeView("Number of Peptide Sequences Found Only in " + db.Key + ": " + db.Value.Count()));
                        }
                        allDatabases.Summary.Add(thisDigestion);

                    }
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
