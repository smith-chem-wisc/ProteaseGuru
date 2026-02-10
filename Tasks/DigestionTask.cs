using System.Collections.Concurrent;
using System.Data;
using Chromatography.RetentionTimePrediction;
using Chromatography.RetentionTimePrediction.Chronologer;
using Engine;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using SharpLearning.Common.Interfaces;
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

        public Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>? PeptideByFile;

        public static event EventHandler<StringEventArgs> OutLabelStatusHandler;

        public static Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>? AllPeptidesByProtease;

        public  Dictionary<string, Dictionary<Protein, (double, double)>> SequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double, double)>>();
        public override MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList)
        {
            AllPeptidesByProtease = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
            PeptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>(dbFileList.Count);

            // Use a thread-safe dictionary for parallel writes
            var concurrentPeptideByFile = new ConcurrentDictionary<string, ConcurrentDictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();

            // Process each database in parallel
            Parallel.ForEach(dbFileList, database =>
            {
                Status("Loading Protein Database(s)...", "loadDbs");
                List<Protein> proteins = LoadProteins(database);

                // Initialize the entry for this database
                var proteaseResults = new ConcurrentDictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                concurrentPeptideByFile[database.FileName] = proteaseResults;

                // Capture variables for the inner parallel loop to avoid closure issues
                string databaseFileName = database.FileName;
                List<Protein> proteinsForDigestion = proteins;

                // Process each protease in parallel for this database
                Parallel.ForEach(DigestionParameters.ProteasesForDigestion, protease =>
                {
                    Status("Digesting Proteins...", "digestDbs");

                    var peptides = DigestDatabase(proteinsForDigestion, protease, DigestionParameters);
                    var peptidesFormatted = DeterminePeptideStatus(databaseFileName, peptides, DigestionParameters);

                    // Thread-safe add to the concurrent dictionary
                    proteaseResults[protease.Name] = peptidesFormatted;
                });
            });

            // Convert concurrent dictionary back to regular dictionary
            foreach (var dbEntry in concurrentPeptideByFile)
            {
                PeptideByFile[dbEntry.Key] = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>(dbEntry.Value);
            }

            Status("Writing Peptide Output...", "peptides");
            WritePeptidesToTsv(PeptideByFile, OutputFolder, DigestionParameters);
            SequenceCoverageByProtease = CalculateProteinSequenceCoverage(PeptideByFile);
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
                    proteinList = ProteinDbLoader.LoadProteinFasta(database.FilePath, true, DecoyType.None, false, out dbErrors, ProteinDbLoader.UniprotAccessionRegex,
                        ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                        ProteinDbLoader.UniprotOrganismRegex,  -1);
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

        //determine if a peptide is unique or shared. Also generates in silico peptide objects
        /// <summary>
        /// Determines if each peptide is unique (maps to one protein) or shared (maps to multiple proteins).
        /// Also calculates physicochemical properties (hydrophobicity, electrophoretic mobility) and 
        /// generates InSilicoPep objects for downstream analysis.
        /// </summary>
        /// <param name="databaseName">Name of the source database file</param>
        /// <param name="databasePeptides">Dictionary mapping proteins to their digested peptides</param>
        /// <param name="userParams">User-specified digestion parameters</param>
        /// <returns>Dictionary mapping proteins to their processed InSilicoPep objects</returns>
        Dictionary<Protein, List<InSilicoPep>> DeterminePeptideStatus(
            string databaseName, 
            Dictionary<Protein, List<PeptideWithSetModifications>> databasePeptides, 
            Parameters userParams)
        {
            // ============================================================================
            // PHASE 1: Determine uniqueness for all peptide sequences
            // ============================================================================
    
            // Flatten all peptides to determine which sequences are unique vs shared
            var allPeptides = databasePeptides
                .SelectMany(kvp => kvp.Value)
                .ToList();

            // Group by sequence to determine uniqueness
            var peptideGroups = userParams.TreatModifiedPeptidesAsDifferent
                ? allPeptides.GroupBy(p => p.FullSequence)
                : allPeptides.GroupBy(p => p.BaseSequence);

            // Build lookup: sequence -> isUnique (maps to exactly one protein)
            var uniquenessLookup = peptideGroups.ToDictionary(
                group => group.Key,
                group => group.Select(p => p.Protein).Distinct().Count() == 1
            );

            // ============================================================================
            // PHASE 2: Batch calculate hydrophobicity and electrophoretic mobility
            // ============================================================================
    
            var hydrophobicityValues = BatchCalculateHydrophobicity(allPeptides);
            var mobilityValues = BatchCalculateElectrophoreticMobility(allPeptides);
            var retentionTimesChronologer = BatchCalculateRetentionTimesChronologer(allPeptides);

            // Create a lookup from peptide to its calculated values
            var peptideToIndex = new Dictionary<PeptideWithSetModifications, int>();

            for (int i = 0; i < allPeptides.Count; i++)
            {
                peptideToIndex[allPeptides[i]] = i;
            }

            // ============================================================================
            // PHASE 3: Build InSilicoPep objects - process protein by protein to maintain order
            // ============================================================================
    
            var inSilicoPeptides = new Dictionary<Protein, List<InSilicoPep>>();

            foreach (var proteinEntry in databasePeptides)
            {
                var protein = proteinEntry.Key;
                var peptideList = new List<InSilicoPep>();

                foreach (var peptide in proteinEntry.Value)
                {
                    // Look up uniqueness
                    string sequenceKey = userParams.TreatModifiedPeptidesAsDifferent 
                        ? peptide.FullSequence 
                        : peptide.BaseSequence;
                    bool isUnique = uniquenessLookup[sequenceKey];

                    // Get pre-calculated values
                    int index = peptideToIndex[peptide];

                    // In the DeterminePeptideStatus method, update the InSilicoPep constructor call:
                    // Replace this section (around line 190-200):

                    var inSilicoPep = new InSilicoPep(
                        peptide.BaseSequence,
                        peptide.FullSequence,
                        peptide.PreviousAminoAcid,
                        peptide.NextAminoAcid,
                        isUnique,
                        hydrophobicityValues[index],
                        mobilityValues[index],
                        retentionTimesChronologer[index],  // Add this new parameter
                        peptide.Length,
                        peptide.MonoisotopicMass,
                        databaseName,
                        peptide.Protein.Accession,
                        peptide.Protein.Name,
                        peptide.OneBasedStartResidueInProtein,
                        peptide.OneBasedEndResidueInProtein,
                        peptide.DigestionParams.DigestionAgent.Name
                    );

                    peptideList.Add(inSilicoPep);
                }

                inSilicoPeptides[protein] = peptideList;
            }

            // ============================================================================
            // PHASE 4: Handle proteins with no peptides
            // ============================================================================
    
            foreach (var protein in databasePeptides.Keys.Where(p => !inSilicoPeptides.ContainsKey(p)))
            {
                inSilicoPeptides[protein] = new List<InSilicoPep>();
            }

            return inSilicoPeptides;
        }
        // Add this static field at the top of the DigestionTask class:
        private static readonly object ChronologerLock = new object();

        // Then update the BatchCalculateRetentionTimesChronologer method:
        private double[] BatchCalculateRetentionTimesChronologer(List<PeptideWithSetModifications> peptides)
        {
            var results = new double[peptides.Count];

            // Synchronize access to prevent concurrent file access to model
            lock (ChronologerLock)
            {
                var rtPredictor = new Chromatography.RetentionTimePrediction.Chronologer.ChronologerRetentionTimePredictor();

                for (int i = 0; i < peptides.Count; i++)
                {
                    var result = rtPredictor.PredictRetentionTime(peptides[i], out var failureReason);
                    results[i] = result ?? -1;
                }
            }

            return results;
        }

        /// <summary>
        /// Batch calculates hydrophobicity (retention time prediction) for a collection of peptides.
        /// This method is designed to be easily replaced with a batch-based ML prediction model.
        /// </summary>
        /// <param name="peptides">Collection of peptides to process</param>
        /// <returns>Array of hydrophobicity values in the same order as input peptides</returns>
        private double[] BatchCalculateHydrophobicity(List<PeptideWithSetModifications> peptides)
        {
            // Initialize the retention time predictor
            // TODO: Replace with batch-based ML model (e.g., Prosit, DeepLC, Chronologer)
            var rtPredictor = new SSRCalc3("SSRCalc 3.0 (300A)", SSRCalc3.Column.A300);
    
            var results = new double[peptides.Count];
    
            // Current implementation: calculate one-by-one
            // Future implementation: send entire batch to ML model
            for (int i = 0; i < peptides.Count; i++)
            {
                results[i] = rtPredictor.ScoreSequence(peptides[i]);
            }
    
            return results;
        }

        /// <summary>
        /// Batch calculates electrophoretic mobility for a collection of peptides.
        /// Uses the Cifuentes mobility equation based on charge and mass.
        /// </summary>
        /// <param name="peptides">Collection of peptides to process</param>
        /// <returns>Array of electrophoretic mobility values in the same order as input peptides</returns>
        private double[] BatchCalculateElectrophoreticMobility(List<PeptideWithSetModifications> peptides)
        {
            var results = new double[peptides.Count];
    
            // Can be parallelized if needed for large datasets
            for (int i = 0; i < peptides.Count; i++)
            {
                results[i] = GetCifuentesMobility(peptides[i]);
            }
    
            return results;
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
        /// <summary>
        /// Calculates protein sequence coverage for each protease across all databases.
        /// 
        /// Sequence coverage is defined as the percentage of residues in a protein's sequence
        /// that are covered by at least one peptide from the digestion.
        /// 
        /// Returns both total coverage (all peptides) and unique coverage (unique peptides only).
        /// Coverage values are returned as percentages (0-100), rounded to 2 decimal places.
        /// </summary>
        /// <param name="peptideByFile">
        /// Hierarchical dictionary structure:
        ///   Database filename -> Protease name -> Protein object -> List of InSilicoPep peptides
        /// </param>
        /// <returns>
        /// Dictionary structure:
        ///   Protease name -> Protein object -> (total coverage %, unique coverage %)
        /// </returns>
        /// <remarks>
        /// Algorithm:
        /// 1. Flatten peptides from all databases, grouped by protease
        /// 2. Build a mapping from protein accession strings to Protein objects
        /// 3. For each protease, group peptides by protein accession
        /// 4. For each protein:
        ///    a. Track which residue positions are covered by any peptide
        ///    b. Track which residue positions are covered by UNIQUE peptides only
        ///    c. Calculate coverage as: (covered residues / protein length) * 100
        /// 5. Return the coverage percentages for each protein-protease combination
        /// </remarks>
        private Dictionary<string, Dictionary<Protein, (double, double)>> CalculateProteinSequenceCoverage(
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile)
        {
            // ============================================================================
            // PHASE 1: Aggregate peptides from all databases, organized by protease
            // ============================================================================

            // Key: protease name, Value: all peptides digested by that protease (across all databases)
            Dictionary<string, List<InSilicoPep>> allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();

            // Maps protein accession string -> actual Protein object
            // This is needed because InSilicoPep stores protein accession as a string,
            // but we need to return Protein objects in the result and access their Length property
            Dictionary<string, Protein> accessionToProtein = new Dictionary<string, Protein>();

            // Iterate through each database file
            foreach (var database in peptideByFile)
            {
                // Iterate through each protease used on this database
                foreach (var protease in database.Value)
                {
                    string proteaseName = protease.Key;

                    // If we've already seen this protease (from another database), add to existing list
                    if (allDatabasePeptidesByProtease.ContainsKey(proteaseName))
                    {
                        // Add peptides from each protein in this database
                        foreach (var proteinEntry in protease.Value)
                        {
                            Protein protein = proteinEntry.Key;
                            List<InSilicoPep> peptides = proteinEntry.Value;

                            allDatabasePeptidesByProtease[proteaseName].AddRange(peptides);

                            // Store the accession -> Protein mapping
                            // (overwrites if same accession appears in multiple databases, which is fine)
                            accessionToProtein[protein.Accession] = protein;
                        }
                    }
                    else
                    {
                        // First time seeing this protease - create new entry
                        allDatabasePeptidesByProtease.Add(
                            proteaseName,
                            protease.Value.SelectMany(p => p.Value).ToList()
                        );

                        // Store protein mappings for all proteins digested by this protease
                        foreach (var proteinEntry in protease.Value)
                        {
                            accessionToProtein[proteinEntry.Key.Accession] = proteinEntry.Key;
                        }
                    }
                }
            }

            // ============================================================================
            // PHASE 2: Calculate coverage for each protease-protein combination
            // ============================================================================

            // Result structure: Protease name -> Protein -> (total coverage %, unique coverage %)
            Dictionary<string, Dictionary<Protein, (double, double)>> proteinSequenceCoverageByProtease =
                new Dictionary<string, Dictionary<Protein, (double, double)>>();

            // Process each protease
            foreach (var protease in allDatabasePeptidesByProtease)
            {
                string proteaseName = protease.Key;
                List<InSilicoPep> peptidesForProtease = protease.Value;

                // ------------------------------------------------------------------------
                // Group peptides by their parent protein accession
                // NOTE: InSilicoPep.Protein is a STRING (the accession), not a Protein object!
                // ------------------------------------------------------------------------
                var peptidesByProteinAccession = peptidesForProtease
                    .GroupBy(p => p.Protein)  // p.Protein is the accession string
                    .ToDictionary(
                        group => group.Key,      // Key is the accession string
                        group => group.ToList()  // Value is list of peptides for that protein
                    );

                // Storage for coverage results for this protease
                Dictionary<Protein, (double, double)> sequenceCoverages =
                    new Dictionary<Protein, (double, double)>();

                // Calculate coverage for each protein
                foreach (var proteinGroup in peptidesByProteinAccession)
                {
                    string proteinAccession = proteinGroup.Key;
                    List<InSilicoPep> peptidesForThisProtein = proteinGroup.Value;

                    // ------------------------------------------------------------------------
                    // CRITICAL: Look up the actual Protein object to get the correct sequence length
                    // The proteinAccession is a STRING, so proteinAccession.Length would give us
                    // the length of the accession string (e.g., "P68871" = 7), NOT the protein length!
                    // ------------------------------------------------------------------------
                    if (!accessionToProtein.TryGetValue(proteinAccession, out Protein actualProtein))
                    {
                        // Skip if we can't find the protein (shouldn't happen, but defensive)
                        continue;
                    }

                    // Get the actual protein sequence length (e.g., 147 for HBB_HUMAN)
                    int proteinSequenceLength = actualProtein.Length;

                    // ------------------------------------------------------------------------
                    // Track which residue positions (1-based) are covered by peptides
                    // Using HashSet ensures each position is only counted once, even if
                    // multiple peptides cover the same residue
                    // ------------------------------------------------------------------------
                    HashSet<int> coveredResidues = new HashSet<int>();       // All peptides
                    HashSet<int> coveredResiduesUnique = new HashSet<int>(); // Unique peptides only

                    // Use HashSet to avoid counting duplicate peptides multiple times
                    var uniquePeptideSet = peptidesForThisProtein.ToHashSet();

                    // For each peptide, mark all residues it covers
                    foreach (var peptide in uniquePeptideSet)
                    {
                        // peptide.StartResidue and EndResidue are 1-based positions in the protein
                        for (int residuePosition = peptide.StartResidue; residuePosition <= peptide.EndResidue; residuePosition++)
                        {
                            // This residue is covered by at least one peptide
                            coveredResidues.Add(residuePosition);

                            // If the peptide is unique (maps to only one protein), also add to unique coverage
                            if (peptide.Unique)
                            {
                                coveredResiduesUnique.Add(residuePosition);
                            }
                        }
                    }

                    // ------------------------------------------------------------------------
                    // Calculate coverage percentages
                    // Coverage = (number of covered residues / total residues) * 100
                    // ------------------------------------------------------------------------
                    double totalCoveragePercent = (double)coveredResidues.Count / proteinSequenceLength * 100.0;
                    double uniqueCoveragePercent = (double)coveredResiduesUnique.Count / proteinSequenceLength * 100.0;

                    // Round to 2 decimal places for cleaner display
                    totalCoveragePercent = Math.Round(totalCoveragePercent, 2);
                    uniqueCoveragePercent = Math.Round(uniqueCoveragePercent, 2);

                    // Store the results using the actual Protein object as the key
                    sequenceCoverages.Add(actualProtein, (totalCoveragePercent, uniqueCoveragePercent));
                }

                // Add this protease's coverage results to the final output
                proteinSequenceCoverageByProtease.Add(proteaseName, sequenceCoverages);
            }

            return proteinSequenceCoverageByProtease;
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
                "Next Amino Acid" + tab + "Start Residue" + tab + "End Residue" + tab + "Length" + tab + "Molecular Weight" + tab + "Protein Accession" + tab + "Protein Name" + tab + "Unique Peptide (in this database)" + tab + "Unique Peptide (in all databases)" + tab + "Peptide sequence exclusive to this Database" + tab +
                "Hydrophobicity" + tab + "Electrophoretic Mobility" + tab + "Chronologer Retention Time";
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
                        if (entry.Value.Select(p=>p.Database).Distinct().ToList().Count >1)
                        {
                            foreach (var peptide in entry.Value)
                            {
                                peptide.UniqueAllDbs = false;
                                peptide.SeqOnlyInThisDb = false;
                                allPeptides.Add(peptide);

                            }
                        }
                        else
                        {
                            foreach (var peptide in entry.Value)
                            {
                                peptide.UniqueAllDbs = true;
                                peptide.SeqOnlyInThisDb = true;
                                allPeptides.Add(peptide);

                            }
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
            parameters.Add("Database: " + string.Join(',', peptideByFile.Keys));
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

        //digest proteins for each database using the protease and settings provided
        protected Dictionary<Protein, List<PeptideWithSetModifications>> DigestDatabase(List<Protein> proteinsFromDatabase,
            Protease protease, Parameters userDigestionParams)
        {           
            DigestionParams dp = new DigestionParams(protease: protease.Name, maxMissedCleavages: userDigestionParams.NumberOfMissedCleavagesAllowed,
                minPeptideLength: userDigestionParams.MinPeptideLengthAllowed, maxPeptideLength: userDigestionParams.MaxPeptideLengthAllowed);            
            Dictionary<Protein, List<PeptideWithSetModifications>> peptidesForProtein = new Dictionary<Protein, List<PeptideWithSetModifications>>(proteinsFromDatabase.Count);
            foreach (var protein in proteinsFromDatabase)
            {
                List<PeptideWithSetModifications> peptides = protein.Digest(dp, userDigestionParams.fixedMods, userDigestionParams.variableMods).ToList();
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
    }
}
