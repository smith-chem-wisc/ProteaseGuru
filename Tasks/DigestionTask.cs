using System.Collections.Concurrent;
using System.Data;
using Chromatography.RetentionTimePrediction.Chronologer;
using Engine;
using Omics;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Proteomics.RetentionTimePrediction;
using Transcriptomics;
using UsefulProteomicsDatabases;
using PredictionClients.Koina.SupportedModels.FlyabilityModels;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using PredictionClients.Koina.Util;
using BayesianEstimation;
using PredictionClients.Koina.AbstractClasses;
using UsefulProteomicsDatabases.Transcriptomics;

namespace Tasks
{
    /// <summary>
    /// Digests the provided databases with the proteases and parameters provided by the user.
    /// Implements IDisposable to properly clean up Chronologer predictor resources.
    /// </summary>
    public class DigestionTask : ProteaseGuruTask, IDisposable
    {
        #region Parallelism Configuration

        // Databases are processed sequentially and each stage within a database (digestion and the
        // per-peptide property calculations) parallelizes across all cores, so exactly one parallel
        // region is active at a time. A single database therefore saturates the machine, without the
        // nested oversubscription the old outer-by-database / inner=2 scheme caused on many-core hosts.
        //
        // Defaults to all cores, but is overridden by the user-configurable GlobalVariables.MaxThreads
        // setting (persisted via GlobalParameters.MaxThreads / the GUI thread-count control).
        private static int MaxConcurrency => Math.Max(1, GlobalVariables.MaxThreads);

        #endregion

        #region Chronologer Predictor Pool

        // Instance-scoped predictor pool to avoid cross-instance race conditions
        private readonly object _predictorLock = new();
        private ConcurrentBag<ChronologerRetentionTimePredictor>? _predictorPool;
        private readonly object _pflyLock = new();
        private ConcurrentBag<PFly2024FineTuned>? _pflyPool;
        private bool _disposed;

        #endregion

        #region Public Properties and Events

        public static event EventHandler<StringEventArgs>? DigestionWarnHandler;
        public static event EventHandler<StringEventArgs>? OutLabelStatusHandler;

        public RunParameters DigestionParameters { get; set; }
        public Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>? PeptideByFile;
        public static Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>? AllPeptidesByProtease;
        public Dictionary<string, Dictionary<IBioPolymer, (double, double)>> SequenceCoverageByProtease = new();
        public Dictionary<string, Dictionary<IBioPolymer, (double, double)>> SequenceCoverageByProteaseFromDetectablePeptides = new();

        #endregion

        #region Constructor

        public DigestionTask() : base(MyTask.Digestion)
        {
            DigestionParameters = new RunParameters();
        }

        #endregion

        #region Main Execution

        public override MyTaskResults RunSpecific(string OutputFolder, List<DbForDigestion> dbFileList)
        {
            // Initialize predictor pools for this run
            InitializePredictorPool();
            InitializePflyPool();

            try
            {
                AllPeptidesByProtease = new Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>();
                PeptideByFile = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>(dbFileList.Count);

                // Process databases sequentially. The work inside each database (digestion and the
                // property calculations) parallelizes across all cores, so one database already
                // saturates the machine. Sequential databases also avoid running multiple batched
                // libtorch (Chronologer) passes concurrently, which would oversubscribe Torch's
                // own intra-op thread pool.
                foreach (var database in dbFileList)
                {
                    Status("Loading Protein Database(s)...", "loadDbs");
                    List<IBioPolymer> proteins = LoadBioPolymers(database.FilePath);

                    var proteaseResults = new Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>();

                    // Each protease is processed sequentially; DigestDatabase and the batch property
                    // calculations each parallelize internally across all cores.
                    foreach (var protease in DigestionParameters.ProteaseSpecificParameters)
                    {
                        Status("Digesting Proteins...", "digestDbs");

                        var peptides = DigestDatabase(proteins, protease, DigestionParameters);
                        var peptidesFormatted = DeterminePeptideStatus(database.FileName, peptides, DigestionParameters);

                        proteaseResults[protease.DigestionAgentName] = peptidesFormatted;
                    }

                    PeptideByFile[database.FileName] = proteaseResults;
                }

                Status("Writing Peptide Output...", "peptides");
                WritePeptidesToTsv(PeptideByFile, OutputFolder, DigestionParameters);
                SequenceCoverageByProtease = CalculateProteinSequenceCoverage(PeptideByFile);
                SequenceCoverageByProteaseFromDetectablePeptides = CalculateProteinSequenceCoverage(
                    PeptideByFile.ToDictionary(
                        db => db.Key,
                        db => db.Value.ToDictionary(
                            protease => protease.Key,
                            protease => protease.Value.ToDictionary(
                                protein => protein.Key,
                                protein => protein.Value.Where(peptide => peptide.PflyDetectability == true).ToList()
                            )
                        )
                    )
                );
                MyTaskResults myRunResults = new MyTaskResults(this);
                Status("Writing Results Summary...", "summary");

                return myRunResults;
            }
            finally
            {
                // Clean up predictor pools after run completes
                DisposePredictorPool();
                DisposePflyPool();
            }
        }

        public override MyTaskResults RunSpecific(MyTaskResults digestionResults, List<string> peptideFilePaths)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Protein Loading

        public List<IBioPolymer> LoadBioPolymers(string dbPath)
        {
            List<IBioPolymer> bioPolymerList;

            // Parse the database with all available threads (databases load sequentially now, so the
            // loader gets the full core budget instead of the previous single thread).
            if (GlobalVariables.AnalyteType == AnalyteType.Oligo)
                bioPolymerList = LoadOligoDb(dbPath, out Dictionary<string, Modification> unknownModification, MaxConcurrency)
                    .Cast<IBioPolymer>().ToList();
            else
                bioPolymerList = LoadProteinDb(dbPath, out Dictionary<string, Modification> unknownModifications, MaxConcurrency)
                    .Cast<IBioPolymer>().ToList();

            if (!bioPolymerList.Any())
                Warn("Warning: No protein entries were found in the database");

            return bioPolymerList;
        }

        // Lock for thread-safe modification of the shared static Mods dictionary.
        // LoadOligoDb and LoadProteinDb are called from inside Parallel.ForEach,
        // so concurrent calls to Mods.AddOrUpdateModification must be synchronized.
        private static readonly object ModsLock = new();

        public static IEnumerable<RNA> LoadOligoDb(string fileName, out Dictionary<string, Modification> unknownMods, int maxThreads = 1, string? decoyIdentifier = null)
        {
            unknownMods = null;
            decoyIdentifier ??= GlobalVariables.DecoyIdentifier;
            List<RNA> rnaList;

            string theExtension = Path.GetExtension(fileName).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
            theExtension = compressed ? Path.GetExtension(Path.GetFileNameWithoutExtension(fileName)).ToLowerInvariant() : theExtension;

            if (theExtension.Equals(".fasta") || theExtension.Equals(".fa"))
            {
                rnaList = RnaDbLoader.LoadRnaFasta(fileName, true, DecoyType.None, false, out var dbErrors);
            }
            else
            {
                var headerMods = ProteinDbLoader.GetPtmListFromProteinXml(fileName);
                lock (ModsLock)
                {
                    foreach (var mod in headerMods)
                        Mods.AddOrUpdateModification(mod, true);
                }
                // TODO: Add in variant params when fixed in MzLib. 
                rnaList = RnaDbLoader.LoadRnaXML(fileName, true, DecoyType.None, false, Mods.AllRnaModsList, [], out unknownMods, maxThreads, decoyIdentifier: decoyIdentifier);
            }
            return rnaList.Where(p => p.BaseSequence.Length > 0);
        }

        public static IEnumerable<Protein> LoadProteinDb(string fileName, out Dictionary<string, Modification> um, int maxThreads = 1, string? decoyIdentifier = null)
        {
            um = new Dictionary<string, Modification>();
            decoyIdentifier ??= GlobalVariables.DecoyIdentifier;
            List<Protein> proteinList;

            string theExtension = Path.GetExtension(fileName).ToLowerInvariant();
            bool compressed = theExtension.EndsWith("gz"); // allows for .bgz and .tgz, too which are used on occasion
            theExtension = compressed ? Path.GetExtension(Path.GetFileNameWithoutExtension(fileName)).ToLowerInvariant() : theExtension;

            if (theExtension.Equals(".fasta") || theExtension.Equals(".fa"))
            {
                proteinList = ProteinDbLoader.LoadProteinFasta(fileName, true, DecoyType.None, false, out var dbErrors,
                    ProteinDbLoader.UniprotAccessionRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotFullNameRegex, ProteinDbLoader.UniprotGeneNameRegex,
                    ProteinDbLoader.UniprotOrganismRegex, maxThreads, addTruncations: false);
            }
            else
            {
                var headerMods = ProteinDbLoader.GetPtmListFromProteinXml(fileName);
                lock (ModsLock)
                {
                    foreach (var mod in headerMods)
                        Mods.AddOrUpdateModification(mod, false);
                }

                proteinList = ProteinDbLoader.LoadProteinXML(fileName, true, DecoyType.None, Mods.AllProteinModsList, false, [], out um, maxThreads, 4, 1, addTruncations: false, decoyIdentifier: decoyIdentifier);
            }
            return proteinList.Where(p => p.BaseSequence.Length > 0);
        }

        #endregion

        #region Peptide Status Determination

        /// <summary>
        /// Determines if each peptide is unique (maps to one protein) or shared (maps to multiple proteins).
        /// Also calculates physicochemical properties and generates InSilicoPep objects.
        /// </summary>
        /// <param name="databaseName">Name of the source database file</param>
        /// <param name="databasePeptides">Dictionary mapping proteins to their digested peptides</param>
        /// <param name="userParams">User-specified digestion parameters</param>
        /// <returns>Dictionary mapping proteins to their processed InSilicoPep objects</returns>
        Dictionary<IBioPolymer, List<InSilicoPep>> DeterminePeptideStatus(
            string databaseName,
            Dictionary<IBioPolymer, List<IBioPolymerWithSetMods>> databasePeptides,
            RunParameters userParams)
        {
            // PHASE 1: Determine uniqueness for all peptide sequences
            // ============================================================================

            // Flatten all peptides to determine which sequences are unique vs shared
            var allWithSetMods = databasePeptides
                .SelectMany(kvp => kvp.Value)
                .ToList();

            var peptideGroups = userParams.TreatModifiedPeptidesAsDifferent
                ? allWithSetMods.GroupBy(p => p.FullSequence)
                : allWithSetMods.GroupBy(p => p.BaseSequence);

            var uniquenessLookup = peptideGroups.ToDictionary(
                group => group.Key,
                group => group.Select(p => p.Parent).Distinct().Count() == 1
            );

            // ============================================================================
            // PHASE 2: Batch calculate hydrophobicity, electrophoretic mobility,
            //          retention times, and detectabilities.
            //
            // All four are pure functions of a peptide's full sequence, so we compute them
            // once per DISTINCT full sequence and fan the results back out in PHASE 3. On
            // typical proteomes ~2-3x of peptides are duplicate sequences (the same sequence
            // digested from multiple proteins or overlapping missed-cleavage windows), so
            // deduplicating avoids that much redundant work in the expensive predictors.
            // ============================================================================

            var allPeptides = allWithSetMods.Where(p => p is PeptideWithSetModifications).Cast<PeptideWithSetModifications>().ToList();

            var hydrophobicityBySequence = new Dictionary<string, double>();
            var mobilityBySequence = new Dictionary<string, double>();
            var retentionTimeBySequence = new Dictionary<string, double>();
            var detectabilityBySequence = new Dictionary<string, bool?>();
            var detectabilityProbabilityBySequence = new Dictionary<string, (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)?>();

            // These properties only apply to peptides. If any analyte is a non-peptide
            // (e.g. an oligo), leave the maps empty so PHASE 3 falls back to NaN/null sentinels
            // (so downstream code can distinguish "not calculated" from "calculated as zero").
            if (allPeptides.Count == allWithSetMods.Count && allPeptides.Count > 0)
            {
                var distinctPeptides = new List<PeptideWithSetModifications>();
                var seenSequences = new HashSet<string>();
                foreach (var peptide in allPeptides)
                {
                    if (seenSequences.Add(peptide.FullSequence))
                        distinctPeptides.Add(peptide);
                }

                // PFly detectability is a remote Koina (network) call; start it concurrently so its
                // latency overlaps the CPU-bound local property calculations below.
                var pflyTask = Task.Run(() => BatchCalculateDetectabilitiesPfly(distinctPeptides, userParams.DetectabilityThreshold));

                double[] hydrophobicityValues = BatchCalculateHydrophobicity(distinctPeptides);
                double[] mobilityValues = BatchCalculateElectrophoreticMobility(distinctPeptides);
                double[] retentionTimesChronologer = BatchCalculateRetentionTimesChronologer(distinctPeptides);
                var (pflyDetectabilities, pflyProbabilities) = pflyTask.GetAwaiter().GetResult();

                for (int i = 0; i < distinctPeptides.Count; i++)
                {
                    string sequence = distinctPeptides[i].FullSequence;
                    hydrophobicityBySequence[sequence] = hydrophobicityValues[i];
                    mobilityBySequence[sequence] = mobilityValues[i];
                    retentionTimeBySequence[sequence] = retentionTimesChronologer[i];
                    detectabilityBySequence[sequence] = pflyDetectabilities[i];
                    detectabilityProbabilityBySequence[sequence] = pflyProbabilities[i];
                }
            }

            // PHASE 3: Build InSilicoPep objects
            var inSilicoPeptides = new Dictionary<IBioPolymer, List<InSilicoPep>>();

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

                    // Properties were computed once per distinct full sequence in PHASE 2.
                    // Non-peptide analytes aren't in the maps and fall back to NaN/null.
                    string fullSequence = peptide.FullSequence;
                    double hydrophobicity = hydrophobicityBySequence.TryGetValue(fullSequence, out var hydro) ? hydro : double.NaN;
                    double mobility = mobilityBySequence.TryGetValue(fullSequence, out var mob) ? mob : double.NaN;
                    double retentionTime = retentionTimeBySequence.TryGetValue(fullSequence, out var rt) ? rt : double.NaN;
                    bool? detectability = detectabilityBySequence.TryGetValue(fullSequence, out var det) ? det : null;
                    var detectabilityProbability = detectabilityProbabilityBySequence.TryGetValue(fullSequence, out var prob) ? prob : null;

                    var inSilicoPep = new InSilicoPep(
                        peptide.BaseSequence,
                        peptide.FullSequence,
                        peptide.PreviousResidue,
                        peptide.NextResidue,
                        isUnique,
                        hydrophobicity,
                        mobility,
                        retentionTime,
                        detectability,
                        peptide.Length,
                        peptide.MonoisotopicMass,
                        databaseName,
                        peptide.Parent.Accession,
                        peptide.Parent.Name,
                        peptide.OneBasedStartResidue,
                        peptide.OneBasedEndResidue,
                        peptide.DigestionParams.DigestionAgent.Name,
                        detectabilityProbability
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
        /// Initializes the predictor pool with a single predictor (work is sequential per protease).
        /// Called once per run, not resizable to avoid race conditions.
        /// </summary>
        private void InitializePredictorPool()
        {
            lock (_predictorLock)
            {
                if (_predictorPool != null)
                    return;

                _predictorPool = new ConcurrentBag<ChronologerRetentionTimePredictor>();

                // One predictor suffices: databases and proteases run sequentially, and the batched
                // Chronologer call uses a single instance (it parallelizes encoding internally).
                _predictorPool.Add(new ChronologerRetentionTimePredictor());
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

        /// <summary>
        /// Initializes the PFly detectability model pool with a single model (work is sequential per protease).
        /// </summary>
        private void InitializePflyPool()
        {
            lock (_pflyLock)
            {
                if (_pflyPool != null)
                    return;

                _pflyPool = new ConcurrentBag<PFly2024FineTuned>();

                // One model suffices: detectability is requested once per protease, sequentially.
                _pflyPool.Add(new PFly2024FineTuned());
            }
        }

        /// <summary>
        /// Disposes all models in the PFly pool.
        /// </summary>
        private void DisposePflyPool()
        {
            lock (_pflyLock)
            {
                if (_pflyPool == null)
                    return;

                while (_pflyPool.TryTake(out _))
                {
                    // PFly2024FineTuned does not implement IDisposable
                }

                _pflyPool = null;
            }
        }

        /// <summary>
        /// Checks out a PFly model from the pool. Blocks if none available.
        /// </summary>
        private PFly2024FineTuned CheckoutPfly()
        {
            if (_pflyPool == null)
                throw new InvalidOperationException("PFly pool not initialized.");

            SpinWait spinner = default;
            PFly2024FineTuned? model;
            while (!_pflyPool.TryTake(out model))
            {
                spinner.SpinOnce();
            }

            return model;
        }

        /// <summary>
        /// Returns a PFly model to the pool for reuse.
        /// </summary>
        private void ReturnPfly(PFly2024FineTuned model)
        {
            _pflyPool?.Add(model);
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

            // Use Chronologer's batched API: it encodes the peptides in parallel and runs the
            // Torch model in large batched forward passes (one model lock per chunk) rather than
            // a locked batch-of-1 call per peptide. This is dramatically faster for many peptides.
            // Results come back in input order; -1 is the sentinel for peptides it couldn't predict.
            var predictor = CheckoutPredictor();
            try
            {
                var predictions = predictor.PredictRetentionTimeEquivalents(peptides, maxThreads: MaxConcurrency);
                if (predictions.Count != peptides.Count)
                {
                    Warn($"Chronologer returned {predictions.Count} retention times for {peptides.Count} peptides. Falling back to -1.");
                    Array.Fill(results, -1.0);
                    return results;
                }
                for (int i = 0; i < results.Length; i++)
                {
                    results[i] = predictions[i].PredictedValue ?? -1;
                }
            }
            finally
            {
                ReturnPredictor(predictor);
            }

            return results;
        }

        private (bool?[] Detectabilities, (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)?[] Probabilities) BatchCalculateDetectabilitiesPfly(List<PeptideWithSetModifications> peptides, double detectabilityThreshold)
        {
            if (peptides.Count == 0) return (Array.Empty<bool?>(), Array.Empty<(double, double, double, double)?>());

            var model = CheckoutPfly();
            try
            {
                var inputs = peptides.Select(p => new DetectabilityPredictionInput(p.FullSequence)).ToList();
                List<PeptideDetectabilityPrediction> results = model.Predict(inputs);

                if (results.Count != peptides.Count)
                {
                    Warn($"PFly detectability prediction returned {results.Count} results for {peptides.Count} peptides. Falling back to null values.");
                    return (new bool?[peptides.Count], new (double, double, double, double)?[peptides.Count]);
                }

                var predictedDetectability = results.Select(r => r.DetectabilityProbabilities.HasValue ? (1.0 - r.DetectabilityProbabilities.Value.NotDetectable) >= detectabilityThreshold : (bool?)null).ToArray();
                var predictedProbabilities = results.Select(r => r.DetectabilityProbabilities).ToArray();
                return (predictedDetectability, predictedProbabilities);
            }
            catch (Exception ex)
            {
                Warn($"PFly detectability prediction failed: {ex.Message}. Falling back to null values for all peptides.");
                return (new bool?[peptides.Count], new (double, double, double, double)?[peptides.Count]);
            }
            finally
            {
                ReturnPfly(model);
            }
        }

        /// <summary>
        /// Batch calculates hydrophobicity for a collection of peptides.
        /// </summary>
        private double[] BatchCalculateHydrophobicity(List<PeptideWithSetModifications> peptides)
        {
            var results = new double[peptides.Count];
            if (peptides.Count == 0) return results;

            var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency };

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

            var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency };

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

        private static readonly HashSet<string> ShiftingModifications = new(StringComparer.Ordinal)
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
        private Dictionary<string, Dictionary<IBioPolymer, (double, double)>> CalculateProteinSequenceCoverage(
            Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile)
        {
            // PHASE 1: Aggregate peptides from all databases by protease
            var allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
            var accessionToProtein = new Dictionary<string, IBioPolymer>();

            foreach (var database in peptideByFile)
            {
                foreach (var protease in database.Value)
                {
                    string proteaseName = protease.Key;

                    if (!allDatabasePeptidesByProtease.TryGetValue(proteaseName, out var peptideList))
                    {
                        peptideList = new List<InSilicoPep>();
                        allDatabasePeptidesByProtease[proteaseName] = peptideList;
                    }

                    foreach (var proteinEntry in protease.Value)
                    {
                        peptideList.AddRange(proteinEntry.Value);
                        accessionToProtein[proteinEntry.Key.Accession] = proteinEntry.Key;
                    }
                }
            }

            // PHASE 2: Calculate coverage for each protease-protein combination
            var proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<IBioPolymer, (double, double)>>();

            foreach (var protease in allDatabasePeptidesByProtease)
            {
                string proteaseName = protease.Key;
                var peptidesForProtease = protease.Value;

                var peptidesByProteinAccession = peptidesForProtease
                    .GroupBy(p => p.Protein)
                    .ToDictionary(group => group.Key, group => group.ToList());

                var sequenceCoverages = new Dictionary<IBioPolymer, (double, double)>();

                foreach (var proteinGroup in peptidesByProteinAccession)
                {
                    string proteinAccession = proteinGroup.Key;
                    var peptidesForThisProtein = proteinGroup.Value;

                    if (!accessionToProtein.TryGetValue(proteinAccession, out IBioPolymer? actualProtein))
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
        //digest proteins for each database using the protease and settings provided
        protected Dictionary<IBioPolymer, List<IBioPolymerWithSetMods>> DigestDatabase(List<IBioPolymer> proteinsFromDatabase,
            ProteaseSpecificParameters proteaseSpecificParameters, RunParameters globalDigestionParams)
        {
            // Each protein digests independently, so fan the proteins across all cores.
            var digestedByProtein = new ConcurrentDictionary<IBioPolymer, List<IBioPolymerWithSetMods>>();
            Parallel.ForEach(proteinsFromDatabase, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency }, protein =>
            {
                List<IBioPolymerWithSetMods> peptides = protein.Digest(proteaseSpecificParameters.DigestionParams, proteaseSpecificParameters.FixedMods, proteaseSpecificParameters.VariableMods).ToList();
                if (globalDigestionParams.MaxPeptideMassAllowed != -1 && globalDigestionParams.MinPeptideMassAllowed != -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass >= globalDigestionParams.MinPeptideMassAllowed && p.MonoisotopicMass <= globalDigestionParams.MaxPeptideMassAllowed).ToList();
                }
                else if (globalDigestionParams.MaxPeptideMassAllowed == -1 && globalDigestionParams.MinPeptideMassAllowed != -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass >= globalDigestionParams.MinPeptideMassAllowed).ToList();
                }
                else if (globalDigestionParams.MaxPeptideMassAllowed != -1 && globalDigestionParams.MinPeptideMassAllowed == -1)
                {
                    peptides = peptides.Where(p => p.MonoisotopicMass <= globalDigestionParams.MaxPeptideMassAllowed).ToList();
                }
                digestedByProtein[protein] = peptides;
            });

            // Rebuild in the original protein order so output stays deterministic.
            Dictionary<IBioPolymer, List<IBioPolymerWithSetMods>> peptidesForProtein = new(proteinsFromDatabase.Count);
            foreach (var protein in proteinsFromDatabase)
            {
                peptidesForProtein.Add(protein, digestedByProtein[protein]);
            }

            return peptidesForProtein;
        }

        #endregion

        #region TSV Output

        /// <summary>
        /// Writes peptides to TSV files as results.
        /// </summary>
        protected static void WritePeptidesToTsv(
            Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile,
            string filePath,
            RunParameters userParams)
        {
            const string tab = "\t";
            string header = string.Join(tab,
                "Database", "Protease", "Base Sequence", "Full Sequence", "Previous Amino Acid",
                "Next Amino Acid", "Start Residue", "End Residue", "Length", "Molecular Weight",
                "Protein Accession", "Protein Name", "Unique Peptide (in this database)",
                "Unique Peptide (in all databases)", "Peptide sequence exclusive to this Database",
                "Hydrophobicity", "Electrophoretic Mobility", "Chronologer Retention Time", $"Pfly Detectability (>={userParams.DetectabilityThreshold:0.0##})",
                "PFly NotDetectable Prob", "PFly Low Detectability Prob", "PFly Intermediate Detectability Prob", "PFly High Detectability Prob");

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
                // Large write buffer (UTF-8 without BOM, matching the default so the reload parser's
                // header check still passes) to cut flush overhead when writing many peptides.
                using var output = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(false), 1 << 20);
                output.WriteLine(header);

                int peptidesWrittenToThisFile = 0;
                while (peptidesWrittenToThisFile < peptidesPerFile && peptideIndex < numberOfPeptides)
                {
                    output.WriteLine(allPeptides[peptideIndex].ToString());
                    peptideIndex++;
                    peptidesWrittenToThisFile++;
                }
            }

            // Write digestion parameters to TOML file
            string tomlPath = Path.Combine(filePath, "DigestionParameters.toml");
            RunParameters.ToToml(userParams, tomlPath);
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
                DisposePflyPool();
            }

            _disposed = true;
        }

        #endregion
    }
}
