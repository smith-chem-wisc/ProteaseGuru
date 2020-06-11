using Engine;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UsefulProteomicsDatabases;

namespace Tasks
{
    public class DigestionTask : ProteaseGuruTask
    {
        public DigestionTask(): base(MyTask.Digestion)
        { 
          DigestionParameters = new Parameters();
        }
        public static event EventHandler<StringEventArgs> DigestionWarnHandler;
        public Parameters DigestionParameters { get; set; }

        public Dictionary<string, Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>> PeptideByFile;


        public override MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList)
        {                    
            PeptideByFile =
                new Dictionary<string, Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>>();
            foreach (var database in dbFileList)
            {
                Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>> peptidesByProtease = new Dictionary<Protease, Dictionary<Protein, List<InSilicoPeptide>>>();
                List<Protein> proteins = LoadProteins(database);
                foreach (var protease in DigestionParameters.ProteasesForDigestion)
                {
                    var peptides = DigestDatabase(proteins, protease, DigestionParameters);
                    var inSilicoPeptidesByFile = DeterminePeptideStatus(peptides, DigestionParameters);
                    peptidesByProtease.Add(protease, inSilicoPeptidesByFile);
                }
                PeptideByFile.Add(database.FileName, peptidesByProtease);
            }         
            
            MyTaskResults myRunResults = new MyTaskResults(this);
            return myRunResults;
        }
        // Load proteins from XML or FASTA databases and keep them associated with the database file name from which they came from
        protected List<Protein> LoadProteins(DbForDigestion database)
        {                        
                List<string> dbErrors = new List<string>();
                List<Protein> proteinList = new List<Protein>();
                
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
                        return new List<Protein>() { };
                    }
                    else
                    {
                        return proteinList;
                    }

                }
                else
                {
                    List<string> modTypesToExclude = new List<string> { };
                    proteinList = ProteinDbLoader.LoadProteinXML(database.FilePath, true, DecoyType.None, GlobalVariables.AllModsKnown, false, modTypesToExclude,
                        out Dictionary<string, Modification> um, -1, 4, 1);
                    if (!proteinList.Any())
                    {
                        Warn("Warning: No protein entries were found in the database");
                        return new List<Protein>() { };
                }
                    else
                    {
                        return proteinList;
                    }
                }
            
            
        }
        //digest proteins for each database using the protease and settings provided
        protected Dictionary<Protein, List<PeptideWithSetModifications>> DigestDatabase(List<Protein> proteinsFromDatabase,
            Protease protease, Parameters userDigestionParams)
        {           
            DigestionParams dp = new DigestionParams(protease: protease.Name, maxMissedCleavages: userDigestionParams.NumberOfMissedCleavagesAllowed,
                minPeptideLength: userDigestionParams.MinPeptideLengthAllowed, maxPeptideLength: userDigestionParams.MaxPeptideLengthAllowed);            
            Dictionary<Protein, List<PeptideWithSetModifications>> peptidesForProtein = new Dictionary<Protein, List<PeptideWithSetModifications>>();
            foreach (var protein in proteinsFromDatabase)
            {
                List<PeptideWithSetModifications> peptides = protein.Digest(dp, new List<Modification> { }, new List<Modification> { }).ToList();
                peptidesForProtein.Add(protein, peptides);
            }
            return peptidesForProtein;
        }
        //determine if peptides are unique and shared for the speicifc database that they came from (Will do pooled analysis later)
        Dictionary<Protein, List<InSilicoPeptide>> DeterminePeptideStatus(Dictionary<Protein, List<PeptideWithSetModifications>> databasePeptides, Parameters userParams)
        {
            SSRCalc3 RTPrediction = new SSRCalc3("SSRCalc 3.0 (300A)", SSRCalc3.Column.A300);
            bool treatModPeptidesAsDifferent = userParams.TreatModifiedPeptidesAsDifferent;            
            Dictionary<string, (List<PeptideWithSetModifications>, HashSet<Protein>)> peptidesToProteins = new Dictionary<string, (List<PeptideWithSetModifications>, HashSet<Protein>)>();
            foreach (var protein in databasePeptides) 
             
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
                            peptidesToProteins.Add(peptide.FullSequence, (new List<PeptideWithSetModifications>() { peptide }, new HashSet<Protein>() { protein.Key }));
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
            var sharedPeptides = peptidesToProteins.Select(p => p.Value).Where(p => p.Item2.Count >= 2).Select(p => p.Item1).SelectMany(p => p).ToList();
            var uniquePeptides = peptidesToProteins.Select(p => p.Value).Where(p => p.Item2.Count == 1).Select(p => p.Item1).SelectMany(p => p).ToList();            
            List<InSilicoPeptide> inSilicoPeptides = new List<InSilicoPeptide>();
            foreach (var peptide in sharedPeptides)
            {
                var pep = new InSilicoPeptide(peptide.Protein, peptide.DigestionParams, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein,
                                CleavageSpecificity.Full, peptide.PeptideDescription, peptide.MissedCleavages, peptide.AllModsOneIsNterminus, peptide.NumFixedMods,
                                peptide.BaseSequence, false);
                var hydrophob = RTPrediction.ScoreSequence(pep);
                var em = GetCifuentesMobility(peptide);
                pep.SetHydrophobicity(hydrophob);
                pep.SetElectrophoreticMobility(em);
                inSilicoPeptides.Add(pep);
            }
            foreach (var peptide in uniquePeptides)
            {
                var pep = new InSilicoPeptide(peptide.Protein, peptide.DigestionParams, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein,
                                    CleavageSpecificity.Full, peptide.PeptideDescription, peptide.MissedCleavages, peptide.AllModsOneIsNterminus, peptide.NumFixedMods,
                                    peptide.BaseSequence, true);
                var hydrophob = RTPrediction.ScoreSequence(pep);
                var em = GetCifuentesMobility(peptide);
                pep.SetHydrophobicity(hydrophob);
                pep.SetElectrophoreticMobility(em);
                inSilicoPeptides.Add(pep);
            }
            var labeledPeptides = inSilicoPeptides.GroupBy(p => p.Protein).ToDictionary(group => group.Key, group => group.ToList());
            return labeledPeptides;
        }
        private static double GetCifuentesMobility(PeptideWithSetModifications pwsm)
        {
            int charge = 1 + pwsm.BaseSequence.Count(f => f == 'K') + pwsm.BaseSequence.Count(f => f == 'R') + pwsm.BaseSequence.Count(f => f == 'H') - CountModificationsThatShiftMobility(pwsm.AllModsOneIsNterminus.Values.AsEnumerable());// the 1 + is for N-terminal

            double mobility = (Math.Log(1 + 0.35 * (double)charge)) / Math.Pow(pwsm.MonoisotopicMass, 0.411);
            if (Double.IsNaN(mobility)==true)
            {
                mobility = 0;
            }
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
            DigestionWarnHandler?.Invoke(null, new StringEventArgs(v, null));
        }

        public override MyTaskResults RunSpecific(MyTaskResults digestionResults, List<string> peptideFilePaths)
        {
            throw new NotImplementedException();
        }
    }
}
