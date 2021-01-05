using Engine;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UsefulProteomicsDatabases;

namespace Tasks
{
    //digest the provided databases with the proteases and parameters provided by the user
    public class DigestionTask : ProteaseGuruTask
    {
        public DigestionTask(): base(MyTask.Digestion)
        { 
          DigestionParameters = new Parameters();
        }
        public static event EventHandler<StringEventArgs> DigestionWarnHandler;
        public Parameters DigestionParameters { get; set; }

        public Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile;

        public static event EventHandler<StringEventArgs> OutLabelStatusHandler;


        public override MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList)
        {            
            PeptideByFile =
                new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>(dbFileList.Count);
            int threads_1 = Environment.ProcessorCount - 1 > dbFileList.Count() ? dbFileList.Count : Environment.ProcessorCount - 1;
            int[] threadArray_1 = Enumerable.Range(0, threads_1).ToArray();

            Parallel.ForEach(threadArray_1, (j) =>
            {
                for (; j < dbFileList.Count(); j += threads_1)
                {
                    var database = dbFileList[j];                    
                    Status("Loading Protein Database(s)...", "loadDbs");
                    List<Protein> proteins = LoadProteins(database);                   
                    int maxThreads = Environment.ProcessorCount - 1;                    
                    int[] threads = Enumerable.Range(0, maxThreads).ToArray();
                    Parallel.ForEach(threads, (i) =>
                    {
                        for (; i < DigestionParameters.ProteasesForDigestion.Count; i += maxThreads)
                        {
                            Status("Digesting Proteins...", "digestDbs");

                            var peptides = DigestDatabase(proteins, DigestionParameters.ProteasesForDigestion[i], DigestionParameters);
                            var peptidesFormatted = DeterminePeptideStatus(database.FileName, peptides, DigestionParameters);
                            lock (PeptideByFile)
                            {
                                if (PeptideByFile.ContainsKey(database.FileName))
                                {
                                    PeptideByFile[database.FileName].Add(DigestionParameters.ProteasesForDigestion[i].Name, peptidesFormatted);
                                }
                                else 
                                {
                                    Dictionary<string, Dictionary<Protein, List<InSilicoPep>>> peptidesByProtease = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                                    peptidesByProtease.Add(DigestionParameters.ProteasesForDigestion[i].Name, peptidesFormatted);
                                    PeptideByFile.Add(database.FileName, peptidesByProtease);
                                }
                                
                            }
                        }

                    });

                }
            });
            Status("Writing Peptide Output...", "peptides");
            WritePeptidesToTsv(PeptideByFile, OutputFolder, DigestionParameters);            
            MyTaskResults myRunResults = new MyTaskResults(this);
            Status("Writing Results Summary...", "summary");
           
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
            Dictionary<Protein, List<PeptideWithSetModifications>> peptidesForProtein = new Dictionary<Protein, List<PeptideWithSetModifications>>(proteinsFromDatabase.Count);
            foreach (var protein in proteinsFromDatabase)
            {
                List<PeptideWithSetModifications> peptides = protein.Digest(dp, new List<Modification> { }, new List<Modification> { }).ToList();
                if (userDigestionParams.MaxPeptideMassAllowed != -1 && userDigestionParams.MinPeptideMassAllowed != -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass > userDigestionParams.MinPeptideMassAllowed && p.MonoisotopicMass < userDigestionParams.MaxPeptideMassAllowed).ToList();
                }
                else if (userDigestionParams.MaxPeptideMassAllowed == -1 && userDigestionParams.MinPeptideMassAllowed != -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass > userDigestionParams.MinPeptideMassAllowed).ToList();
                }
                else if (userDigestionParams.MaxPeptideMassAllowed != -1 && userDigestionParams.MinPeptideMassAllowed == -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass < userDigestionParams.MaxPeptideMassAllowed).ToList();
                }                
                peptidesForProtein.Add(protein, peptides);
            }
            return peptidesForProtein;
        }        

        //determine if a peptide is unqiue or shared. Also generates in silico peptide objects
        Dictionary<Protein, List<InSilicoPep>> DeterminePeptideStatus(string databaseName, Dictionary<Protein, List<PeptideWithSetModifications>> databasePeptides, Parameters userParams)
        {
            SSRCalc3 RTPrediction = new SSRCalc3("SSRCalc 3.0 (300A)", SSRCalc3.Column.A300);
            Dictionary<Protein, List<InSilicoPep>> inSilicoPeptides = new Dictionary<Protein, List<InSilicoPep>>();
            if (userParams.TreatModifiedPeptidesAsDifferent == true)
            {
                foreach (var peptideSequence in databasePeptides.Select(p => p.Value).SelectMany(pep => pep).GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList()))
                {
                    if (peptideSequence.Value.Select(p => p.Protein).Distinct().Count() == 1)
                    {
                        foreach (var peptide in peptideSequence.Value)
                        {                          
                            
                            if (inSilicoPeptides.ContainsKey(peptide.Protein))
                            {
                                inSilicoPeptides[peptide.Protein].Add(new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, true, RTPrediction.ScoreSequence(peptide), GetCifuentesMobility(peptide), peptide.Length, peptide.MonoisotopicMass, databaseName,
                                    peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name));
                            }
                            else
                            {
                                inSilicoPeptides.Add(peptide.Protein, new List<InSilicoPep>() { new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, true, RTPrediction.ScoreSequence(peptide), GetCifuentesMobility(peptide), peptide.Length, peptide.MonoisotopicMass, databaseName,
                                peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name)});
                            }

                        }
                    }
                    else
                    {
                        foreach (var peptide in peptideSequence.Value)
                        {
                            
                            if (inSilicoPeptides.ContainsKey(peptide.Protein))
                            {
                                inSilicoPeptides[peptide.Protein].Add(new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, false, RTPrediction.ScoreSequence(peptide), GetCifuentesMobility(peptide), peptide.Length, peptide.MonoisotopicMass,databaseName,
                                    peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name));
                            }
                            else
                            {
                                inSilicoPeptides.Add(peptide.Protein, new List<InSilicoPep>() { new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, false, RTPrediction.ScoreSequence(peptide), GetCifuentesMobility(peptide), peptide.Length, peptide.MonoisotopicMass, databaseName,
                                peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name)});
                            }

                        }
                    }
                }
            }
            else 
            {
                foreach (var peptideSequence in databasePeptides.Select(p => p.Value).SelectMany(pep => pep).GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList()))
                {
                    if (peptideSequence.Value.Select(p => p.Protein).Distinct().Count() == 1)
                    {
                        foreach (var peptide in peptideSequence.Value)
                        {
                            var hydrophob = RTPrediction.ScoreSequence(peptide);
                            var em = GetCifuentesMobility(peptide);
                            if (inSilicoPeptides.ContainsKey(peptide.Protein))
                            {
                                inSilicoPeptides[peptide.Protein].Add(new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, true, hydrophob, em, peptide.Length, peptide.MonoisotopicMass, databaseName,
                                    peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name));
                            }
                            else
                            {
                                inSilicoPeptides.Add(peptide.Protein, new List<InSilicoPep>() { new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, true, hydrophob, em, peptide.Length, peptide.MonoisotopicMass, databaseName,
                                peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name)});
                            }

                        }
                    }
                    else
                    {
                        foreach (var peptide in peptideSequence.Value)
                        {
                            var hydrophob = RTPrediction.ScoreSequence(peptide);
                            var em = GetCifuentesMobility(peptide);
                            if (inSilicoPeptides.ContainsKey(peptide.Protein))
                            {
                                inSilicoPeptides[peptide.Protein].Add(new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, false, hydrophob, em, peptide.Length, peptide.MonoisotopicMass, databaseName,
                                    peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name));
                            }
                            else
                            {
                                inSilicoPeptides.Add(peptide.Protein, new List<InSilicoPep>() { new InSilicoPep(peptide.BaseSequence, peptide.FullSequence, peptide.PreviousAminoAcid, peptide.NextAminoAcid, false, hydrophob, em, peptide.Length, peptide.MonoisotopicMass, databaseName,
                                peptide.Protein.Accession, peptide.Protein.Name, peptide.OneBasedStartResidueInProtein, peptide.OneBasedEndResidueInProtein, peptide.DigestionParams.Protease.Name)});
                            }

                        }
                    }
                }
            }
            foreach (var protein in databasePeptides.Keys.Where(p => inSilicoPeptides.ContainsKey(p) == false))
            {
                inSilicoPeptides.Add(protein, new List<InSilicoPep>());
            }
            databasePeptides = null;
            return inSilicoPeptides;
        }
        
        //calculate electrophoretic mobility of a peptide
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
        
        // write peptides to tsv files as results
        protected static void WritePeptidesToTsv(Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile, string filePath, Parameters userParams)
        {
            string tab = "\t";
            string header = "Database" + tab + "Protease" + tab + "Base Sequence" + tab + "Full Sequence" + tab + "Previous Amino Acid" + tab +
                "Next Amino Acid" + tab +"Start Residue"+tab+"End Residue"+ tab+ "Length" + tab + "Molecular Weight" + tab + "Protein Accession" + tab + "Protein Name" + tab + "Unique Peptide (in this database)" + tab + "Unique Peptide (in all databases)" + tab+ "Peptide sequence exclusive to this Database" +tab+
                "Hydrophobicity" + tab + "Electrophoretic Mobility";
            List<InSilicoPep> allPeptides = new List<InSilicoPep>();
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFileUpdated = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();
            if (peptideByFile.Count > 1)
            {
                Dictionary<string, List<InSilicoPep>> allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
                foreach (var database in peptideByFile)
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
                    if (userParams.TreatModifiedPeptidesAsDifferent)
                    {
                        peptidesToProteins = protease.Value.GroupBy(p => p.FullSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }
                    else
                    {
                        peptidesToProteins = protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(group => group.Key, group => group.ToList());
                    }

                    var unique = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() == 1).ToList();
                    var shared = peptidesToProteins.Where(p => p.Value.Select(p => p.Protein).Distinct().Count() > 1).ToList();
                    
                    foreach (var entry in unique)
                    {
                        foreach (var peptide in entry.Value)
                        {
                            peptide.UniqueAllDbs = true;
                            peptide.SeqOnlyInThisDb = true;
                            allPeptides.Add(peptide);                           

                        }                        
                    }
                    foreach (var entry in shared)
                    {

                        if (entry.Value.Select(p => p.Database).Distinct().Count() == 1)
                        {
                            foreach (var peptide in entry.Value)
                            {
                                peptide.UniqueAllDbs = false;
                                peptide.SeqOnlyInThisDb = true;
                                allPeptides.Add(peptide);                                
                            }
                        }
                        else
                        {
                            foreach (var peptide in entry.Value)
                            {
                                peptide.UniqueAllDbs = false;
                                peptide.SeqOnlyInThisDb = false;
                                allPeptides.Add(peptide);                                
                            }
                        }                        
                    }
                    
                }
            }
            else
            {
                foreach (var database in peptideByFile)
                {
                    foreach (var protease in database.Value)
                    {
                        foreach (var protein in protease.Value)
                        {                           
                            foreach (var peptide in protein.Value)
                            {
                                peptide.UniqueAllDbs = peptide.Unique;
                                peptide.SeqOnlyInThisDb = true;                                
                                allPeptides.Add(peptide);
                            }                            
                        }
                    }
                }
            }
                       
            
            var numberOfPeptides = allPeptides.Count();
            double numberOfFiles = Math.Ceiling(numberOfPeptides / 1000000.0);
            var peptidesInFile = 1;
            var peptideIndex = 0;
            var fileCount = 1;           

                while (fileCount <= Convert.ToInt32(numberOfFiles))
                {
                    using (StreamWriter output = new StreamWriter(filePath + @"\ProteaseGuruPeptides_" + fileCount + ".tsv"))
                    {
                        output.WriteLine(header);
                        while (peptidesInFile < 1000000)
                        {
                            if (peptideIndex < numberOfPeptides)
                            {
                                output.WriteLine(allPeptides[peptideIndex].ToString());
                                peptideIndex++;
                            }                            
                            peptidesInFile++;
                                                        
                        }
                        output.Close();
                        peptidesInFile = 1;
                    }                    
                    fileCount++;
                }

            List<string> parameters = new List<string>();
            parameters.Add("Digestion Conditions:");
            parameters.Add("Proteases: " + string.Join(',', userParams.ProteasesForDigestion.Select(p => p.Name).ToList()));
            parameters.Add("Max Missed Cleavages: " + userParams.NumberOfMissedCleavagesAllowed);
            parameters.Add("Min Peptide Length: " + userParams.MinPeptideLengthAllowed);
            parameters.Add("Max Peptide Length: " + userParams.MaxPeptideLengthAllowed);
            parameters.Add("Treat modified peptides as different peptides: " + userParams.TreatModifiedPeptidesAsDifferent);
            parameters.Add("Min Peptide Mass: " + userParams.MinPeptideLengthAllowed);
            parameters.Add("Max Peptide Mass: " + userParams.MaxPeptideLengthAllowed);

            File.WriteAllLines(filePath + @"\DigestionConditions.txt", parameters);
            
        }

        protected void Status(string v, string id)
        {
            OutLabelStatusHandler?.Invoke(this, new StringEventArgs(v, new List<string> { id }));
        }
    }
}
