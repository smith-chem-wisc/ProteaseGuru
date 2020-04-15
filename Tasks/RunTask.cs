using Engine;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UsefulProteomicsDatabases;

namespace Tasks
{
    public class RunTask
    {
        
        public static event EventHandler<StringEventArgs> WarnHandler;
        public Parameters DigestionParameters { get; set; }
        public RunTask()
        {
            DigestionParameters = new Parameters();

        }
        
        protected RunResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList,List<string> peptideResultFilePaths)
        {
            //Key (string) = databse file name
            //Value (List<Protein>) = list of protein objects loaded from said database
            Dictionary<string, List<Protein>> proteinsByDatabase = LoadProteins(dbFileList);
            //Dictionary 1
            //Key (Protease) = protease that is used for digesting proteins
            //Value (Dictionary2)= organizing peptides and proteins by database
            //Dictionary 2
            //Key (string) = database file name
            //Value (Dictionary 3)= keep peptides associated with the protein that they were digestion products from (may not be necessary because the peptides already have this information)
            //Dictionary3
            //Key (Protein) = protein that was digested
            //Value (List<PeptideWithSetModifications>) = peptides that were geenrated by digestion of Protein (Key)
            Dictionary<Protease, Dictionary<string, Dictionary<Protein, List<InSilicoPeptide>>>> peptideByProtease = 
                new Dictionary<Protease, Dictionary<string, Dictionary<Protein, List<InSilicoPeptide>>>>();
            foreach (var protease in DigestionParameters.ProteasesForDigestion)
            {
                var peptides = DigestDatabase(proteinsByDatabase, protease, DigestionParameters);
                var inSilicoPeptidesByFile = DeterminePeptideStatus(peptides, DigestionParameters);
                peptideByProtease.Add(protease, inSilicoPeptidesByFile);
            }            
            RunResults myRunResults = new RunResults(this);
            return myRunResults;
        }
       // Load proteins from XML or FASTA databases and keep them associated with the database file name from which they came from
        protected Dictionary<string, List<Protein>> LoadProteins(List<DbForDigestion> dbFileList)
        {
            Dictionary<string, List<Protein>> databaseProteins = new Dictionary<string, List<Protein>>();
            foreach (var database in dbFileList)
            {
                List<string> dbErrors = new List<string>();
                List<Protein> proteinList = new List<Protein>();
                databaseProteins.Add(database.FileName, ProteinDbLoader.LoadProteinFasta(database.FilePath, true, DecoyType.None, false, ProteinDbLoader.UniprotAccessionRegex, 
                    ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                     ProteinDbLoader.UniprotOrganismRegex, out dbErrors, -1));

                string theExtension = Path.GetExtension(database.FilePath).ToLowerInvariant();
                bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
                theExtension = compressed ? Path.GetExtension(Path.GetFileNameWithoutExtension(database.FilePath)).ToLowerInvariant() : theExtension;

                if (theExtension.Equals(".fasta") || theExtension.Equals(".fa"))
                {
                    proteinList = ProteinDbLoader.LoadProteinFasta(database.FilePath, true, DecoyType.None, false, ProteinDbLoader.UniprotAccessionRegex, 
                        ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                        ProteinDbLoader.UniprotOrganismRegex, out dbErrors, -1);
                    if (!proteinList.Any())
                    {
                        Warn("Warning: No protein entries were found in the database");
                    }
                    else
                    {
                        databaseProteins.Add(database.FileName, proteinList);
                    }
                }
                else
                {
                    List<string> modTypesToExclude = new List<string> { };
                    proteinList = ProteinDbLoader.LoadProteinXML(database.FileName, true, DecoyType.None, GlobalVariables.AllModsKnown, false, modTypesToExclude, 
                        out Dictionary<string, Modification> um, -1, 4, 1);
                    if (!proteinList.Any())
                    {
                        Warn("Warning: No protein entries were found in the database");
                    }
                    else
                    {
                        databaseProteins.Add(database.FileName, proteinList);
                    }
                }
            }
            return databaseProteins;
        }
        //digest proteins for each database using the protease and settings provided
        protected Dictionary<string, Dictionary<Protein, List<PeptideWithSetModifications>>> DigestDatabase(Dictionary<string, List<Protein>> proteinsByDatabase, 
            Protease protease, Parameters userDigestionParams)
        {
            Dictionary<string, Dictionary<Protein, List<PeptideWithSetModifications>>> peptidesByDatabase = new Dictionary<string, Dictionary<Protein, List<PeptideWithSetModifications>>>();
            DigestionParams dp = new DigestionParams(protease: protease.Name, maxMissedCleavages: userDigestionParams.NumberOfMissedCleavagesAllowed, 
                minPeptideLength: userDigestionParams.MinPeptideLengthAllowed, maxPeptideLength: userDigestionParams.MaxPeptideLengthAllowed);
            foreach (var database in proteinsByDatabase)
            {
                Dictionary<Protein, List<PeptideWithSetModifications>> peptidesForProtein = new Dictionary<Protein, List<PeptideWithSetModifications>>();
                foreach (var protein in database.Value)
                {
                    List<PeptideWithSetModifications> peptides = protein.Digest(dp, GlobalVariables.AllModsKnown, new List<Modification> { }).ToList();
                    peptidesForProtein.Add(protein, peptides);
                }
                peptidesByDatabase.Add(database.Key, peptidesForProtein);
            }
            return peptidesByDatabase;
        }
        //determine if peptides are unique and shared for the speicifc database that they came from (Will do pooled analysis later)
        Dictionary<string, Dictionary<Protein, List<InSilicoPeptide>>> DeterminePeptideStatus(Dictionary<string, Dictionary<Protein, List<PeptideWithSetModifications>>> databasePeptides,
            Parameters userParams)
        {
            SSRCalc3 RTPrediction = new SSRCalc3("SSRCalc 3.0 (300A)", SSRCalc3.Column.A300);
            bool treatModPeptidesAsDifferent = userParams.TreatModifiedPeptidesAsDifferent;
            Dictionary<string, Dictionary<Protein, List<InSilicoPeptide>>> peptides = new Dictionary<string, Dictionary<Protein, List<InSilicoPeptide>>>();
            foreach (var database in databasePeptides)
            {                
                Dictionary<string, (List<PeptideWithSetModifications>,HashSet<Protein>)> peptidesToProteins = new Dictionary<string, (List<PeptideWithSetModifications>, HashSet<Protein>)>();
                foreach (var protein in database.Value)
                {
                    if (treatModPeptidesAsDifferent)
                    {
                        //use full sequences
                        foreach (var peptide in protein.Value)
                        {
                            
                            if (peptidesToProteins.ContainsKey(peptide.FullSequence))
                            {                                
                                peptidesToProteins[peptide.FullSequence].Item1.Add(peptide);
                                peptidesToProteins[peptide.FullSequence].Item2.Add(protein.Key);
                            }
                            else 
                            {
                                peptidesToProteins.Add(peptide.FullSequence, (new List<PeptideWithSetModifications>() { peptide }, new HashSet<Protein>(){protein.Key}));
                            }
                        }
                    }
                    else
                    {
                        //use base sequences
                        foreach (var peptide in protein.Value)
                        {
                            if (peptidesToProteins.ContainsKey(peptide.BaseSequence))
                            {
                                peptidesToProteins[peptide.BaseSequence].Item1.Add(peptide);
                                peptidesToProteins[peptide.BaseSequence].Item2.Add(protein.Key);
                            }
                            else
                            {
                                peptidesToProteins.Add(peptide.BaseSequence, (new List<PeptideWithSetModifications>() { peptide }, new HashSet<Protein>() { protein.Key }));
                            }
                        }

                    }
                }
                var sharedPeptides = peptidesToProteins.Select(p=>p.Value).Where(p => p.Item2.Count >= 2).Select(p=>p.Item1).SelectMany(p=>p).ToList();
                var uniquePeptides = peptidesToProteins.Select(p => p.Value).Where(p => p.Item2.Count == 1).Select(p => p.Item1).SelectMany(p => p).ToList();
                Dictionary<Protein, List<InSilicoPeptide>> labeledPeptides = new Dictionary<Protein, List<InSilicoPeptide>>();
                foreach (var protein in database.Value)
                {
                    List<InSilicoPeptide> peptidesForProteins = new List<InSilicoPeptide>();
                    foreach (var peptide in protein.Value)
                    {
                        if (sharedPeptides.Contains(peptide))
                        {
                            var pep = new InSilicoPeptide(peptide.Protein, peptide.DigestionParams, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein,
                                CleavageSpecificity.Full, peptide.PeptideDescription, peptide.MissedCleavages, peptide.AllModsOneIsNterminus, peptide.NumFixedMods,
                                peptide.BaseSequence, false);
                            var hydrophob = RTPrediction.ScoreSequence(pep);
                            var em = GetCifuentesMobility(peptide);
                            pep.SetHydrophobicity(hydrophob);
                            pep.SetElectrophoreticMobility(em);
                            peptidesForProteins.Add(pep);
                        }
                        if (uniquePeptides.Contains(peptide))
                        {
                            peptidesForProteins.Add(new InSilicoPeptide(peptide.Protein, peptide.DigestionParams, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein,
                                CleavageSpecificity.Full, peptide.PeptideDescription, peptide.MissedCleavages, peptide.AllModsOneIsNterminus, peptide.NumFixedMods, 
                                peptide.BaseSequence, true));
                        }
                    }
                    labeledPeptides.Add(protein.Key, peptidesForProteins);
                }
                peptides.Add(database.Key, labeledPeptides);                
            }

            return peptides;
        }
        private static double GetCifuentesMobility(PeptideWithSetModifications pwsm)
        {
            int charge = 1 + pwsm.BaseSequence.Count(f => f == 'K') + pwsm.BaseSequence.Count(f => f == 'R') + pwsm.BaseSequence.Count(f => f == 'H') - CountModificationsThatShiftMobility(pwsm.AllModsOneIsNterminus.Values.AsEnumerable());// the 1 + is for N-terminal

            double mobility = (Math.Log(1 + 0.35 * (double)charge)) / Math.Pow(pwsm.MonoisotopicMass, 0.411);

            return mobility;
        }
        public static int CountModificationsThatShiftMobility(IEnumerable<Modification> modifications)
        {
            List<string> shiftingModifications = new List<string> { "Acetylation", "Ammonia loss", "Carbamyl", "Deamidation", "Formylation",
                "N2-acetylarginine", "N6-acetyllysine", "N-acetylalanine", "N-acetylaspartate", "N-acetylcysteine", "N-acetylglutamate", "N-acetylglycine",
                "N-acetylisoleucine", "N-acetylmethionine", "N-acetylproline", "N-acetylserine", "N-acetylthreonine", "N-acetyltyrosine", "N-acetylvaline",
                "Phosphorylation", "Phosphoserine", "Phosphothreonine", "Phosphotyrosine", "Sulfonation" };

            return modifications.Select(n => n.OriginalId).Intersect(shiftingModifications).Count();
        }
        private void Warn(string v)
        {
            WarnHandler?.Invoke(null, new StringEventArgs(v, null));
        }
    }
}
