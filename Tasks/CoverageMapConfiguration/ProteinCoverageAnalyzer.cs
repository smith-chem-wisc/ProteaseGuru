using Proteomics;

namespace Tasks.CoverageMapConfiguration
{
    /// <summary>
    /// Analyzes protein digestion results and organizes peptide data for coverage analysis.
    /// This class handles all data organization and calculations, separate from GUI concerns.
    /// </summary>
    public class ProteinCoverageAnalyzer
    {
        #region Properties

        /// <summary>
        /// Master data structure: Database -> Protease -> Protein -> Peptides
        /// Contains all peptide results organized hierarchically
        /// </summary>
        public Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> PeptideByFile { get; }

        /// <summary>
        /// Reorganized peptide data: Protein -> Protease -> Peptides
        /// Used for quick lookup when analyzing coverage
        /// </summary>
        public Dictionary<Protein, Dictionary<string, List<InSilicoPep>>> PeptideByProteaseAndProtein { get; private set; }

        /// <summary>
        /// Maps Protein objects to their analysis representation with categorized peptides
        /// </summary>
        public Dictionary<Protein, ProteinCoverageResult> ProteinCoverageResults { get; private set; }

        /// <summary>
        /// List of all unique protein accessions from all databases
        /// </summary>
        public List<string> ProteinAccessions { get; private set; }

        /// <summary>
        /// List of all proteases used in the digestion
        /// </summary>
        public List<string> Proteases { get; private set; }

        /// <summary>
        /// Sequence coverage statistics: Protease -> Protein -> (total coverage, unique coverage)
        /// </summary>
        public Dictionary<string, Dictionary<Protein, (double, double)>> SequenceCoverageByProtease { get; }

        /// <summary>
        /// Indicates whether multiple databases were analyzed
        /// </summary>
        public bool IsMultiDatabase => PeptideByFile.Keys.Count > 1;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new ProteinCoverageAnalyzer and organizes the peptide data
        /// </summary>
        /// <param name="peptideByFile">Hierarchical peptide data: Database -> Protease -> Protein -> Peptides</param>
        /// <param name="sequenceCoverageByProtease">Pre-calculated sequence coverage statistics</param>
        public ProteinCoverageAnalyzer(
            Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>> peptideByFile,
            Dictionary<string, Dictionary<Protein, (double, double)>> sequenceCoverageByProtease)
        {
            PeptideByFile = peptideByFile ?? throw new ArgumentNullException(nameof(peptideByFile));
            SequenceCoverageByProtease = sequenceCoverageByProtease ?? throw new ArgumentNullException(nameof(sequenceCoverageByProtease));

            // Initialize collections
            PeptideByProteaseAndProtein = new Dictionary<Protein, Dictionary<string, List<InSilicoPep>>>();
            ProteinCoverageResults = new Dictionary<Protein, ProteinCoverageResult>();
            ProteinAccessions = new List<string>();
            Proteases = new List<string>();

            // Organize the data
            OrganizePeptideData();
            ExtractProteases();
        }

        #endregion

        #region Data Organization Methods

        /// <summary>
        /// Organizes peptide data into structures suitable for analysis and display.
        /// Creates protein list and maps proteins to their peptides by protease.
        /// </summary>
        private void OrganizePeptideData()
        {
            // Use HashSet to avoid duplicate protein accessions
            HashSet<string> proteinAccessionSet = new HashSet<string>();

            // Iterate through all databases, proteases, and proteins
            foreach (var db in PeptideByFile)
            {
                foreach (var protease in db.Value)
                {
                    foreach (var protein in protease.Value)
                    {
                        var prot = protein.Key;
                        proteinAccessionSet.Add(prot.Accession);

                        // Add or update protein entry in the reorganized dictionary
                        if (PeptideByProteaseAndProtein.ContainsKey(prot))
                        {
                            // Protein exists - add peptides to existing or new protease entry
                            if (PeptideByProteaseAndProtein[prot].ContainsKey(protease.Key))
                            {
                                PeptideByProteaseAndProtein[prot][protease.Key].AddRange(protein.Value);
                            }
                            else
                            {
                                PeptideByProteaseAndProtein[prot].Add(protease.Key, protein.Value);
                            }
                        }
                        else
                        {
                            // New protein - create new entries
                            var peptidesByProtease = new Dictionary<string, List<InSilicoPep>>
                            {
                                { protease.Key, protein.Value }
                            };
                            PeptideByProteaseAndProtein.Add(prot, peptidesByProtease);

                            // Create coverage result for this protein
                            var coverageResult = new ProteinCoverageResult(prot);
                            ProteinCoverageResults.Add(prot, coverageResult);
                        }

                        // Categorize peptides as unique or shared
                        // When multiple databases are analyzed, use UniqueAllDbs flag
                        // When single database, use the simpler Unique flag
                        var result = ProteinCoverageResults[prot];
                        if (IsMultiDatabase)
                        {
                            result.AllPeptides.AddRange(protein.Value);
                            result.UniquePeptides.AddRange(protein.Value.Where(p => p.UniqueAllDbs));
                            result.SharedPeptides.AddRange(protein.Value.Where(p => !p.UniqueAllDbs));
                        }
                        else
                        {
                            result.AllPeptides.AddRange(protein.Value);
                            result.UniquePeptides.AddRange(protein.Value.Where(p => p.Unique));
                            result.SharedPeptides.AddRange(protein.Value.Where(p => !p.Unique));
                        }
                    }
                }
            }

            // Convert to sorted list for consistent ordering
            ProteinAccessions = proteinAccessionSet.OrderBy(a => a).ToList();
        }

        /// <summary>
        /// Extracts the list of all unique proteases from the data
        /// </summary>
        private void ExtractProteases()
        {
            Proteases = PeptideByFile
                .SelectMany(p => p.Value.Keys)
                .Distinct()
                .ToList();
        }

        #endregion

        #region Coverage Calculation Methods

        /// <summary>
        /// Calculates sequence coverage using only unique peptides for a specific protein
        /// </summary>
        /// <param name="protein">The protein to calculate coverage for</param>
        /// <returns>Enumerable of (protease name, coverage fraction) tuples where fraction is 0.0-1.0 (unrounded)</returns>
        public IEnumerable<(string ProteaseName, double CoverageFraction)> CalculateSequenceCoverageUnique(Protein protein)
        {
            if (!PeptideByProteaseAndProtein.ContainsKey(protein))
            {
                yield break;
            }

            foreach (var proteaseKvp in PeptideByProteaseAndProtein[protein])
            {
                HashSet<int> coveredOneBasedResidues = new HashSet<int>();

                // Filter to unique peptides based on database count
                var uniquePeptides = IsMultiDatabase
                    ? proteaseKvp.Value.Where(p => p.UniqueAllDbs).ToHashSet()
                    : proteaseKvp.Value.Where(p => p.Unique).ToHashSet();

                // Mark all residues covered by unique peptides
                foreach (var peptide in uniquePeptides)
                {
                    for (int i = peptide.StartResidue; i <= peptide.EndResidue; i++)
                    {
                        coveredOneBasedResidues.Add(i);
                    }
                }

                // Return unrounded fraction - let caller handle display formatting
                var fraction = (double)coveredOneBasedResidues.Count / protein.Length;
                yield return (proteaseKvp.Key, fraction);
            }
        }

        /// <summary>
        /// Gets the coverage result for a protein by its accession
        /// </summary>
        /// <param name="accession">The protein accession to look up</param>
        /// <returns>The ProteinCoverageResult or null if not found</returns>
        public ProteinCoverageResult GetCoverageResultByAccession(string accession)
        {
            var protein = ProteinCoverageResults.Keys.FirstOrDefault(p => p.Accession == accession);
            return protein != null ? ProteinCoverageResults[protein] : null;
        }

        /// <summary>
        /// Gets peptides for a specific protein and protease combination
        /// </summary>
        /// <param name="protein">The protein</param>
        /// <param name="proteaseName">The protease name</param>
        /// <returns>List of peptides or empty list if not found</returns>
        public List<InSilicoPep> GetPeptidesForProteinAndProtease(Protein protein, string proteaseName)
        {
            if (PeptideByProteaseAndProtein.TryGetValue(protein, out var proteaseDict))
            {
                if (proteaseDict.TryGetValue(proteaseName, out var peptides))
                {
                    return peptides;
                }
            }
            return new List<InSilicoPep>();
        }

        /// <summary>
        /// Gets all peptides for a protein across all proteases
        /// </summary>
        /// <param name="protein">The protein</param>
        /// <returns>List of all peptides for the protein</returns>
        public List<InSilicoPep> GetAllPeptidesForProtein(Protein protein)
        {
            if (PeptideByProteaseAndProtein.TryGetValue(protein, out var proteaseDict))
            {
                return proteaseDict.Values.SelectMany(p => p).ToList();
            }
            return new List<InSilicoPep>();
        }

        #endregion
    }
}
