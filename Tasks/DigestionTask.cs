using System.Collections.Concurrent;
using System.Data;
using Chromatography.RetentionTimePrediction.Chronologer;
using Engine;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using UsefulProteomicsDatabases;

namespace Tasks
{
    /// <summary>
    /// Digests the provided databases with the proteases and parameters provided by the user.
    /// Implements IDisposable to properly clean up Chronologer predictor resources.
    /// </summary>
    public class DigestionTask : ProteaseGuruTask, IDisposable
    {
        #region Parallelism Configuration

        // Maximum concurrency for parallel operations
        private static readonly int MaxConcurrency = Environment.ProcessorCount;

        // Calculated parallelism limits to avoid oversubscription
        // With 8 cores: OuterParallelism = 4, InnerParallelism = 2, total max = 4 * 2 = 8 threads
        private static readonly int OuterParallelism = Math.Max(1, MaxConcurrency / 2);
        private static readonly int InnerParallelism = Math.Max(1, MaxConcurrency / OuterParallelism);

        #endregion

        #region Chronologer Predictor Pool

        // Instance-scoped predictor pool to avoid cross-instance race conditions
        private readonly object _predictorLock = new object();
        private ConcurrentBag<ChronologerRetentionTimePredictor>? _predictorPool;
        private bool _disposed;

        #endregion

        #region Public Properties and Events

        public static event EventHandler<StringEventArgs>? DigestionWarnHandler;
        public static event EventHandler<StringEventArgs>? OutLabelStatusHandler;

        public Parameters DigestionParameters { get; set; }
        public Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>? PeptideByFile;
        public static Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>? AllPeptidesByProtease;
        public Dictionary<string, Dictionary<Protein, (double, double)>> SequenceCoverageByProtease = new();

        #endregion

        #region Constructor

        public DigestionTask() : base(MyTask.Digestion)
        {
            DigestionParameters = new Parameters();
        }

        #endregion

        #region Main Execution

        public override MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList)
        {
            // Initialize predictor pool for this run
            InitializePredictorPool();

            try
            {
                AllPeptidesByProtease = new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                PeptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>(dbFileList.Count);

                // Use a thread-safe dictionary for parallel writes
                var concurrentPeptideByFile = new ConcurrentDictionary<string, ConcurrentDictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();

                // Outer parallelism: limited to avoid oversubscription with inner parallel loops
                var outerParallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = OuterParallelism
                };

                // Process each database in parallel (limited)
                Parallel.ForEach(dbFileList, outerParallelOptions, database =>
                {
                    Status("Loading Protein Database(s)...", "loadDbs");
                    List<Protein> proteins = LoadProteins(database);

                    // Initialize the entry for this database
                    var proteaseResults = new ConcurrentDictionary<string, Dictionary<Protein, List<InSilicoPep>>>();
                    concurrentPeptideByFile[database.FileName] = proteaseResults;

                    string databaseFileName = database.FileName;
                    List<Protein> proteinsForDigestion = proteins;

                    // Process each protease sequentially within each database
                    // (inner batch methods handle parallelism)
                    foreach (var protease in DigestionParameters.ProteasesForDigestion)
                    {
                        Status("Digesting Proteins...", "digestDbs");

                        var peptides = DigestDatabase(proteinsForDigestion, protease, DigestionParameters);
                        var peptidesFormatted = DeterminePeptideStatus(databaseFileName, peptides, DigestionParameters);

                        proteaseResults[protease.Name] = peptidesFormatted;
                    }
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
            finally
            {
                // Clean up predictor pool after run completes
                DisposePredictorPool();
            }
        }

        public override MyTaskResults RunSpecific(MyTaskResults digestionResults, List<string> peptideFilePaths)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Protein Loading

        /// <summary>
        /// Load proteins from XML or FASTA databases and keep them associated with the database file name.
        /// </summary>
        protected List<Protein> LoadProteins(DbForDigestion database)
        {
            List<string> dbErrors = new List<string>();
            List<Protein> proteinList = new List<Protein>();

            string theExtension = Path.GetExtension(database.FilePath).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz");
            theExtension = compressed
                ? Path.GetExtension(Path.GetFileNameWithoutExtension(database.FilePath)).ToLowerInvariant()
                : theExtension;

            if (theExtension.Equals(".fasta") || theExtension.Equals(".fa"))
            {
                proteinList = ProteinDbLoader.LoadProteinFasta(
                    database.FilePath, true, DecoyType.None, false, out dbErrors,
                    ProteinDbLoader.UniprotAccessionRegex,
                    ProteinDbLoader.UniprotFullNameRegex,
                    ProteinDbLoader.UniprotFullNameRegex,
                    ProteinDbLoader.UniprotGeneNameRegex,
                    ProteinDbLoader.UniprotOrganismRegex, -1);
            }
            else
            {
                List<string> modTypesToExclude = new List<string>();
                proteinList = ProteinDbLoader.LoadProteinXML(
                    database.FilePath, true, DecoyType.None, GlobalVariables.AllModsKnown,
                    false, modTypesToExclude, out Dictionary<string, Modification> um, -1, 4, 1);
            }

            if (!proteinList.Any())
            {
                Warn("Warning: No protein entries were found in the database");
                return new List<Protein>();
            }

            return proteinList;
        }

        #endregion

        #region Peptide Status Determination

        /// <summary>
        /// Determines if each peptide is unique (maps to one protein) or shared (maps to multiple proteins).
        /// Also calculates physicochemical properties and generates InSilicoPep objects.
        /// </summary>
        private Dictionary<Protein, List<InSilicoPep>> DeterminePeptideStatus(
            string databaseName,
            Dictionary<Protein, List<PeptideWithSetModifications>> databasePeptides,
            Parameters userParams)
        {
            // PHASE 1: Determine uniqueness for all peptide sequences
            var allPeptides = databasePeptides
                .SelectMany(kvp => kvp.Value)
                .ToList();

            var peptideGroups = userParams.TreatModifiedPeptidesAsDifferent
                ? allPeptides.GroupBy(p => p.FullSequence)
                : allPeptides.GroupBy(p => p.BaseSequence);

            var uniquenessLookup = peptideGroups.ToDictionary(
                group => group.Key,
                group => group.Select(p => p.Protein).Distinct().Count() == 1
            );

            // PHASE 2: Batch calculate physicochemical properties
            var hydrophobicityValues = BatchCalculateHydrophobicity(allPeptides);
            var mobilityValues = BatchCalculateElectrophoreticMobility(allPeptides);
            var retentionTimesChronologer = BatchCalculateRetentionTimesChronologer(allPeptides);

            var peptideToIndex = new Dictionary<PeptideWithSetModifications, int>();
            for (int i = 0; i < allPeptides.Count; i++)
            {
                peptideToIndex[allPeptides[i]] = i;
            }

            // PHASE 3: Build InSilicoPep objects
            var inSilicoPeptides = new Dictionary<Protein, List<InSilicoPep>>();

            foreach (var proteinEntry in databasePeptides)
            {
                var protein = proteinEntry.Key;
                var peptideList = new List<InSilicoPep>();

                foreach (var peptide in proteinEntry.Value)
                {
                    string sequenceKey = userParams.TreatModifiedPeptidesAsDifferent
                        ? peptide.FullSequence
                        : peptide.BaseSequence;
                    bool isUnique = uniquenessLookup[sequenceKey];
                    int index = peptideToIndex[peptide];

                    var inSilicoPep = new InSilicoPep(
                        peptide.BaseSequence,
                        peptide.FullSequence,
                        peptide.PreviousAminoAcid,
                        peptide.NextAminoAcid,
                        isUnique,
                        hydrophobicityValues[index],
                        mobilityValues[index],
                        retentionTimesChronologer[index],
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

            // PHASE 4: Handle proteins with no peptides
            foreach (var protein in databasePeptides.Keys.Where(p => !inSilicoPeptides.ContainsKey(p)))
            {
                inSilicoPeptides[protein] = new List<InSilicoPep>();
            }

            return inSilicoPeptides;
        }

        #endregion

        #region Chronologer Predictor Pool Management

        /// <summary>
        /// Initializes the predictor pool with a fixed size based on InnerParallelism.
        /// Called once per run, not resizable to avoid race conditions.
        /// </summary>
        private void InitializePredictorPool()
        {
            lock (_predictorLock)
            {
                if (_predictorPool != null)
                    return;

                _predictorPool = new ConcurrentBag<ChronologerRetentionTimePredictor>();

                // Pre-create predictors sequentially to avoid file access conflicts
                for (int i = 0; i < InnerParallelism; i++)
                {
                    _predictorPool.Add(new ChronologerRetentionTimePredictor());
                }
            }
        }

        /// <summary>
        /// Disposes all predictors in the pool.
        /// </summary>
        private void DisposePredictorPool()
        {
            lock (_predictorLock)
            {
                if (_predictorPool == null)
                    return;

                while (_predictorPool.TryTake(out var predictor))
                {
                    if (predictor is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }

                _predictorPool = null;
            }
        }

        /// <summary>
        /// Checks out a predictor from the pool. Blocks if none available.
        /// </summary>
        private ChronologerRetentionTimePredictor CheckoutPredictor()
        {
            if (_predictorPool == null)
                throw new InvalidOperationException("Predictor pool not initialized.");

            SpinWait spinner = default;
            ChronologerRetentionTimePredictor? predictor;
            while (!_predictorPool.TryTake(out predictor))
            {
                spinner.SpinOnce();
            }

            return predictor;
        }

        /// <summary>
        /// Returns a predictor to the pool for reuse.
        /// </summary>
        private void ReturnPredictor(ChronologerRetentionTimePredictor predictor)
        {
            _predictorPool?.Add(predictor);
        }

        #endregion

        #region Batch Calculations

        /// <summary>
        /// Batch calculates Chronologer-predicted retention times for a collection of peptides.
        /// </summary>
        private double[] BatchCalculateRetentionTimesChronologer(List<PeptideWithSetModifications> peptides)
        {
            var results = new double[peptides.Count];
            if (peptides.Count == 0) return results;

            int threadCount = Math.Min(InnerParallelism, peptides.Count);

            Parallel.For(0, peptides.Count,
                new ParallelOptions { MaxDegreeOfParallelism = threadCount },
                i =>
                {
                    var predictor = CheckoutPredictor();
                    try
                    {
                        var result = predictor.PredictRetentionTime(peptides[i], out _);
                        results[i] = result ?? -1;
                    }
                    finally
                    {
                        ReturnPredictor(predictor);
                    }
                }
            );

            return results;
        }

        /// <summary>
        /// Batch calculates hydrophobicity for a collection of peptides.
        /// </summary>
        private double[] BatchCalculateHydrophobicity(List<PeptideWithSetModifications> peptides)
        {
            var results = new double[peptides.Count];
            if (peptides.Count == 0) return results;

            var options = new ParallelOptions { MaxDegreeOfParallelism = InnerParallelism };

            Parallel.For(0, peptides.Count,
                options,
                () => new SSRCalc3("SSRCalc 3.0 (300A)", SSRCalc3.Column.A300),
                (i, loopState, rtPredictor) =>
                {
                    results[i] = rtPredictor.ScoreSequence(peptides[i]);
                    return rtPredictor;
                },
                (rtPredictor) => { }
            );

            return results;
        }

        /// <summary>
        /// Batch calculates electrophoretic mobility for a collection of peptides.
        /// </summary>
        private double[] BatchCalculateElectrophoreticMobility(List<PeptideWithSetModifications> peptides)
        {
            var results = new double[peptides.Count];
            if (peptides.Count == 0) return results;

            var options = new ParallelOptions { MaxDegreeOfParallelism = InnerParallelism };

            Parallel.For(0, peptides.Count, options, i =>
            {
                results[i] = GetCifuentesMobility(peptides[i]);
            });

            return results;
        }

        /// <summary>
        /// Calculates electrophoretic mobility of a peptide using the Cifuentes equation.
        /// </summary>
        private static double GetCifuentesMobility(PeptideWithSetModifications pwsm)
        {
            int kCount = 0, rCount = 0, hCount = 0;
            foreach (char c in pwsm.BaseSequence)
            {
                switch (c)
                {
                    case 'K': kCount++; break;
                    case 'R': rCount++; break;
                    case 'H': hCount++; break;
                }
            }

            int charge = 1 + kCount + rCount + hCount - CountModificationsThatShiftMobility(pwsm.AllModsOneIsNterminus.Values);
            double mobility = Math.Log(1 + 0.35 * charge) / Math.Pow(pwsm.MonoisotopicMass, 0.411);

            return double.IsNaN(mobility) ? 0 : mobility;
        }

        private static readonly HashSet<string> ShiftingModifications = new HashSet<string>(StringComparer.Ordinal)
        {
            "Acetylation", "Ammonia loss", "Carbamyl", "Deamidation", "Formylation",
            "N2-acetylarginine", "N6-acetyllysine", "N-acetylalanine", "N-acetylaspartate",
            "N-acetylcysteine", "N-acetylglutamate", "N-acetylglycine", "N-acetylisoleucine",
            "N-acetylmethionine", "N-acetylproline", "N-acetylserine", "N-acetylthreonine",
            "N-acetyltyrosine", "N-acetylvaline", "Phosphorylation", "Phosphoserine",
            "Phosphothreonine", "Phosphotyrosine", "Sulfonation"
        };

        public static int CountModificationsThatShiftMobility(IEnumerable<Modification> modifications)
        {
            return modifications.Count(mod =>
                mod.OriginalId != null && ShiftingModifications.Contains(mod.OriginalId));
        }

        #endregion

        #region Sequence Coverage Calculation

        /// <summary>
        /// Calculates protein sequence coverage for each protease across all databases.
        /// </summary>
        private Dictionary<string, Dictionary<Protein, (double, double)>> CalculateProteinSequenceCoverage(
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile)
        {
            // PHASE 1: Aggregate peptides from all databases by protease
            var allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
            var accessionToProtein = new Dictionary<string, Protein>();

            foreach (var database in peptideByFile)
            {
                foreach (var protease in database.Value)
                {
                    string proteaseName = protease.Key;

                    if (allDatabasePeptidesByProtease.ContainsKey(proteaseName))
                    {
                        foreach (var proteinEntry in protease.Value)
                        {
                            allDatabasePeptidesByProtease[proteaseName].AddRange(proteinEntry.Value);
                            accessionToProtein[proteinEntry.Key.Accession] = proteinEntry.Key;
                        }
                    }
                    else
                    {
                        allDatabasePeptidesByProtease.Add(
                            proteaseName,
                            protease.Value.SelectMany(p => p.Value).ToList()
                        );

                        foreach (var proteinEntry in protease.Value)
                        {
                            accessionToProtein[proteinEntry.Key.Accession] = proteinEntry.Key;
                        }
                    }
                }
            }

            // PHASE 2: Calculate coverage for each protease-protein combination
            var proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<Protein, (double, double)>>();

            foreach (var protease in allDatabasePeptidesByProtease)
            {
                string proteaseName = protease.Key;
                var peptidesForProtease = protease.Value;

                var peptidesByProteinAccession = peptidesForProtease
                    .GroupBy(p => p.Protein)
                    .ToDictionary(group => group.Key, group => group.ToList());

                var sequenceCoverages = new Dictionary<Protein, (double, double)>();

                foreach (var proteinGroup in peptidesByProteinAccession)
                {
                    string proteinAccession = proteinGroup.Key;
                    var peptidesForThisProtein = proteinGroup.Value;

                    if (!accessionToProtein.TryGetValue(proteinAccession, out Protein? actualProtein))
                        continue;

                    int proteinSequenceLength = actualProtein.Length;
                    var coveredResidues = new HashSet<int>();
                    var coveredResiduesUnique = new HashSet<int>();
                    var uniquePeptideSet = peptidesForThisProtein.ToHashSet();

                    foreach (var peptide in uniquePeptideSet)
                    {
                        for (int residuePosition = peptide.StartResidue; residuePosition <= peptide.EndResidue; residuePosition++)
                        {
                            coveredResidues.Add(residuePosition);
                            if (peptide.Unique)
                            {
                                coveredResiduesUnique.Add(residuePosition);
                            }
                        }
                    }

                    double totalCoveragePercent = Math.Round((double)coveredResidues.Count / proteinSequenceLength * 100.0, 2);
                    double uniqueCoveragePercent = Math.Round((double)coveredResiduesUnique.Count / proteinSequenceLength * 100.0, 2);

                    sequenceCoverages.Add(actualProtein, (totalCoveragePercent, uniqueCoveragePercent));
                }

                proteinSequenceCoverageByProtease.Add(proteaseName, sequenceCoverages);
            }

            return proteinSequenceCoverageByProtease;
        }

        #endregion

        #region Database Digestion

        /// <summary>
        /// Digests proteins for each database using the protease and settings provided.
        /// </summary>
        protected Dictionary<Protein, List<PeptideWithSetModifications>> DigestDatabase(
            List<Protein> proteinsFromDatabase,
            Protease protease,
            Parameters userDigestionParams)
        {
            var dp = new DigestionParams(
                protease: protease.Name,
                maxMissedCleavages: userDigestionParams.NumberOfMissedCleavagesAllowed,
                minPeptideLength: userDigestionParams.MinPeptideLengthAllowed,
                maxPeptideLength: userDigestionParams.MaxPeptideLengthAllowed);

            var peptidesForProtein = new Dictionary<Protein, List<PeptideWithSetModifications>>(proteinsFromDatabase.Count);

            foreach (var protein in proteinsFromDatabase)
            {
                var peptides = protein.Digest(dp, userDigestionParams.fixedMods, userDigestionParams.variableMods).ToList();

                // Apply mass filters
                if (userDigestionParams.MinPeptideMassAllowed != -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass > userDigestionParams.MinPeptideMassAllowed).ToList();
                }
                if (userDigestionParams.MaxPeptideMassAllowed != -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass < userDigestionParams.MaxPeptideMassAllowed).ToList();
                }

                peptidesForProtein.Add(protein, peptides);
            }

            return peptidesForProtein;
        }

        #endregion

        #region TSV Output

        /// <summary>
        /// Writes peptides to TSV files as results.
        /// </summary>
        protected static void WritePeptidesToTsv(
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile,
            string filePath,
            Parameters userParams)
        {
            const string tab = "\t";
            string header = string.Join(tab,
                "Database", "Protease", "Base Sequence", "Full Sequence", "Previous Amino Acid",
                "Next Amino Acid", "Start Residue", "End Residue", "Length", "Molecular Weight",
                "Protein Accession", "Protein Name", "Unique Peptide (in this database)",
                "Unique Peptide (in all databases)", "Peptide sequence exclusive to this Database",
                "Hydrophobicity", "Electrophoretic Mobility", "Chronologer Retention Time");

            var allPeptides = new List<InSilicoPep>();

            if (peptideByFile.Count > 1)
            {
                var allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();

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
                    var peptidesToProteins = userParams.TreatModifiedPeptidesAsDifferent
                        ? protease.Value.GroupBy(p => p.FullSequence).ToDictionary(g => g.Key, g => g.ToList())
                        : protease.Value.GroupBy(p => p.BaseSequence).ToDictionary(g => g.Key, g => g.ToList());

                    var unique = peptidesToProteins.Where(p => p.Value.Select(x => x.Protein).Distinct().Count() == 1).ToList();
                    var shared = peptidesToProteins.Where(p => p.Value.Select(x => x.Protein).Distinct().Count() > 1).ToList();

                    foreach (var entry in unique)
                    {
                        bool multipleDbsForSequence = entry.Value.Select(p => p.Database).Distinct().Count() > 1;
                        foreach (var peptide in entry.Value)
                        {
                            peptide.UniqueAllDbs = !multipleDbsForSequence;
                            peptide.SeqOnlyInThisDb = !multipleDbsForSequence;
                            allPeptides.Add(peptide);
                        }
                    }

                    foreach (var entry in shared)
                    {
                        bool singleDb = entry.Value.Select(p => p.Database).Distinct().Count() == 1;
                        foreach (var peptide in entry.Value)
                        {
                            peptide.UniqueAllDbs = false;
                            peptide.SeqOnlyInThisDb = singleDb;
                            allPeptides.Add(peptide);
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

            // Write peptides to files (max 1M per file)
            int numberOfPeptides = allPeptides.Count;
            const int peptidesPerFile = 1000000;
            int numberOfFiles = (int)Math.Ceiling(numberOfPeptides / (double)peptidesPerFile);
            int peptideIndex = 0;

            for (int fileCount = 1; fileCount <= numberOfFiles; fileCount++)
            {
                string outputPath = Path.Combine(filePath, $"ProteaseGuruPeptides_{fileCount}.tsv");
                using var output = new StreamWriter(outputPath);
                output.WriteLine(header);

                int peptidesWrittenToThisFile = 0;
                while (peptidesWrittenToThisFile < peptidesPerFile && peptideIndex < numberOfPeptides)
                {
                    output.WriteLine(allPeptides[peptideIndex].ToString());
                    peptideIndex++;
                    peptidesWrittenToThisFile++;
                }
            }

            // Write digestion conditions
            var parameters = new List<string>
            {
                "Digestion Conditions:",
                "Database: " + string.Join(',', peptideByFile.Keys),
                "Proteases: " + string.Join(", ", userParams.ProteasesForDigestion.Select(p => p.Name)),
                "Max Missed Cleavages: " + userParams.NumberOfMissedCleavagesAllowed,
                "Min Peptide Length: " + userParams.MinPeptideLengthAllowed,
                "Max Peptide Length: " + userParams.MaxPeptideLengthAllowed,
                "Treat modified peptides as different peptides: " + userParams.TreatModifiedPeptidesAsDifferent,
                "Min Peptide Mass: " + userParams.MinPeptideMassAllowed,
                "Max Peptide Mass: " + userParams.MaxPeptideMassAllowed
            };

            File.WriteAllLines(Path.Combine(filePath, "DigestionConditions.txt"), parameters);
        }

        #endregion

        #region Utility Methods

        private void Warn(string message)
        {
            DigestionWarnHandler?.Invoke(null, new StringEventArgs(message, null));
        }

        protected void Status(string message, string id)
        {
            OutLabelStatusHandler?.Invoke(this, new StringEventArgs(message, new List<string> { id }));
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                DisposePredictorPool();
            }

            _disposed = true;
        }

        #endregion
    }
}
