using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        public readonly Dictionary<string, Dictionary<Protein, (double, double)>> SequenceCoverageByProtease;
        Parameters parameters;

        internal MyTaskResults(ProteaseGuruTask s)
        {
            var results = (DigestionTask)s;
            PeptideByFile = results.PeptideByFile;
            parameters = results.DigestionParameters;
            SequenceCoverageByProtease = results.SequenceCoverageByProtease;
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
                
                foreach (var protease in allDatabasePeptidesByProtease)
                {
                    Dictionary<string, List<InSilicoPep>> peptidesToProteins = new Dictionary<string, List<InSilicoPep>>();

                    if (parameters.TreatModifiedPeptidesAsDifferent)
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
                    int sharedDetectableCount = shared.Count(p => p.Value.Any(peptide => peptide.PflyDetectability == true));
                    int uniqueDetectableCount = unique.Count(p => p.Value.Any(peptide => peptide.PflyDetectability == true));


                    foreach (var entry in unique)
                    {
                        if (entry.Value.DistinctBy(p => p.Database).Count() > 1)
                        {
                            uniqueCount = uniqueCount - 1;
                            sharedCount = sharedCount + 1;

                            if (entry.Value.Any(p => p.PflyDetectability == true))
                            {
                                uniqueDetectableCount = uniqueDetectableCount - 1;
                                sharedDetectableCount = sharedDetectableCount + 1;
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
                    summary.Add("   " + prot + " Results:");
                    List<InSilicoPep> allPeptides = peptidesToProteins.SelectMany(p => p.Value).ToList();
                    var allDetectable = allPeptides.Where(p => p.PflyDetectability == true);
                    summary.Add("       Number of Peptides: " + allPeptides.Count + $" ({allDetectable.Count()})");
                    summary.Add("            Number of Distinct Peptide Sequences: " + peptidesToProteins.Count() + $" ({peptidesToProteins.Count(p => p.Value.Any(isp => isp.PflyDetectability == true))})");
                    var peptidesForSingleDatabase = peptidesInOneDb.GroupBy(p => p.Database).ToDictionary(group => group.Key, group => group.ToList());

                    summary.Add("Number of Unique Peptide Sequences: " + uniqueCount + $" ({uniqueDetectableCount})");
                    summary.Add("Number of Shared Peptide Sequences: " + sharedCount + $" ({sharedDetectableCount})");

                    foreach (var db in peptidesForSingleDatabase)
                    {
                        if (parameters.TreatModifiedPeptidesAsDifferent)
                        {
                            var distinctByFullSequence = db.Value.DistinctBy(p => p.FullSequence);
                            summary.Add("Number of Peptide Sequences Found Only in " + db.Key + ": " + distinctByFullSequence.Count() + $" ({distinctByFullSequence.Count(p => p.PflyDetectability == true)})");
                        }
                        else
                        {
                            var distinctByBaseSequence = db.Value.DistinctBy(p => p.BaseSequence);
                            summary.Add("Number of Peptide Sequences Found Only in " + db.Key + ": " + distinctByBaseSequence.Count() + $" ({distinctByBaseSequence.Count(p => p.PflyDetectability == true)})");
                        }
                        
                    }

                    peptidesForSingleDatabase = null;
                    unique = null;
                    shared = null;
                    sharedPeptidesInOneDb = null;
                    allPeptides = null;
                }

                foreach (var database in PeptideByFile)
                {
                    summary.Add(database.Key + " Results:");
                    foreach (var protease in database.Value)
                    {
                        string prot = protease.Key;
                       summary.Add("    "+prot + " Results:");
                        var allPeptides = protease.Value.SelectMany(p => p.Value);
                        var allDetectable = allPeptides.Where(p => p.PflyDetectability == true);
                        if (parameters.TreatModifiedPeptidesAsDifferent)
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count() + $" ({allDetectable.Count()})");
                            summary.Add("            Number of Distinct Peptide Sequences: " + allPeptides.DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.DistinctBy(p => p.FullSequence).Count()})");
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique).DistinctBy(p => p.FullSequence).Count()})");
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => !pep.Unique).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => !pep.Unique).DistinctBy(p => p.FullSequence).Count()})");
                        }
                        else
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count() + $" ({allDetectable.Count()})");
                            summary.Add("           Number of Distinct Peptide Sequences: " + allPeptides.DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.DistinctBy(p => p.BaseSequence).Count()})");
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique).DistinctBy(p => p.BaseSequence).Count()})");
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => !pep.Unique).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => !pep.Unique).DistinctBy(p => p.BaseSequence).Count()})");
                        }
                        allPeptides = null;
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
                        var allDetectable = allPeptides.Where(p => p.PflyDetectability == true);
                        if (parameters.TreatModifiedPeptidesAsDifferent)
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count() + $" ({allDetectable.Count()})");
                            summary.Add("            Number of Distinct Peptide Sequences: " + allPeptides.DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.DistinctBy(p => p.FullSequence).Count()})");
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique).DistinctBy(p => p.FullSequence).Count()})");
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => !pep.Unique).DistinctBy(p => p.FullSequence).Count() + $" ({allDetectable.Where(pep => !pep.Unique).DistinctBy(p => p.FullSequence).Count()})");
                        }
                        else
                        {
                            summary.Add("       Number of Peptides: " + allPeptides.Count() + $" ({allDetectable.Count()})");
                            summary.Add("           Number of Distinct Peptide Sequences: " + allPeptides.DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.DistinctBy(p => p.BaseSequence).Count()})");
                            summary.Add("       Number of Unique Peptides: " + allPeptides.Where(pep => pep.Unique).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => pep.Unique).DistinctBy(p => p.BaseSequence).Count()})");
                            summary.Add("       Number of Shared Peptides: " + allPeptides.Where(pep => !pep.Unique).DistinctBy(p => p.BaseSequence).Count() + $" ({allDetectable.Where(pep => !pep.Unique).DistinctBy(p => p.BaseSequence).Count()})");
                        }
                        allPeptides = null;
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
