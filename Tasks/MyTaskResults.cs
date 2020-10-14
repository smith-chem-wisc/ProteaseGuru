using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tasks
{
    public class MyTaskResults
    {        
        public TimeSpan Time;

        private readonly List<string> resultTexts;

        private readonly StringBuilder TaskSummaryText = new StringBuilder();
        private readonly StringBuilder PsmPeptideProteinSummaryText = new StringBuilder();
        public readonly Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;
        Parameters parameters;

        internal MyTaskResults(ProteaseGuruTask s)
        {
            var results = (DigestionTask)s;
            PeptideByFile = results.PeptideByFile;
            parameters = results.DigestionParameters;
            resultTexts = new List<string>();
        }

        // results sumary for file output
        private List<string> writeSummary(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile)
        {
            List<string> summary = new List<string>();
            if (PeptideByFile.Count > 1)
            {
                summary.Add("Cumulative Database Results:");
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
                            allDatabasePeptidesByProtease.Add(protease.Key, protease.Value.SelectMany(p => p.Value).ToList());
                        }
                    }
                }
                if (parameters.TreatModifiedPeptidesAsDifferent)
                {
                    foreach (var protease in allDatabasePeptidesByProtease)
                    {
                        string prot = protease.Key;
                        summary.Add("   "+prot + " Results:");
                        var peptidesToProteins = protease.Value.GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList());
                        List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();
                        summary.Add("       Number of Peptides: " + allPeptides.Count);
                        summary.Add("            Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.FullSequence).Distinct().Count());
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

                        summary.Add("Number of Unique Peptide Sequences: " + uniquePeptides.Count());
                        summary.Add("Number of Shared Peptide Sequences: " + sharedPeptides.Count());

                        foreach (var db in peptidesForSingleDatabase)
                        {
                            summary.Add("Number of Peptide Sequences Found Only in " + db.Key + ": " + db.Value.Count());
                        }
                    }
                }
                else 
                {
                    foreach (var protease in allDatabasePeptidesByProtease)
                    {
                        string prot = protease.Key;
                        summary.Add("   "+prot + " Results:");
                        var peptidesToProteins = protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                        List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();
                        summary.Add("       Number of Peptides: " + allPeptides.Count);
                        summary.Add("           Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count());
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

                        summary.Add("Number of Unique Peptide Sequences: " + uniquePeptides.Count());
                        summary.Add("Number of Shared Peptide Sequences: " + sharedPeptides.Count());

                        foreach (var db in peptidesForSingleDatabase)
                        {
                            summary.Add("Number of Peptide Sequences Found Only in " + db.Key + ": " + db.Value.Count());
                        }
                    }
                }
                foreach (var database in PeptideByFile)
                {
                    summary.Add(database.Key + " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                       summary.Add("    "+prot + " Results:");
                        var allPeptides = protease.Value.SelectMany(p => p.Value);
                        if (parameters.TreatModifiedPeptidesAsDifferent)
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count());
                            summary.Add("           Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.FullSequence).Distinct().Count());
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.FullSequence).Distinct().Count());
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.FullSequence).Distinct().Count());
                        }
                        else
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count());
                            summary.Add("            Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count());
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.BaseSequence).Distinct().Count());
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.BaseSequence).Distinct().Count());
                        }
                    }
                }
            }
            else // if there is only one database then is results and all database results are the same thing
            {
                foreach (var database in PeptideByFile)
                {
                    summary.Add(database.Key + " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                        summary.Add("   "+prot + " Results:");
                        var allPeptides = protease.Value.SelectMany(p => p.Value);
                        if (parameters.TreatModifiedPeptidesAsDifferent)
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count());
                            summary.Add("            Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.FullSequence).Distinct().Count());
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.FullSequence).Distinct().Count());
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.FullSequence).Distinct().Count());
                        }
                        else
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count());
                            summary.Add("           Number of Distinct Peptide Sequences: " + allPeptides.Select(p => p.BaseSequence).Distinct().Count());
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique == true).Select(p => p.BaseSequence).Distinct().Count());
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => pep.Unique == false).Select(p => p.BaseSequence).Distinct().Count());
                        }
                    }
                }
            }
            return summary;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Digestion Task Summary: ");
            sb.AppendLine();            
            sb.AppendLine("--------------------------------------------------");
            foreach (var line in writeSummary(PeptideByFile))
            {
                sb.AppendLine(line);
            }
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
