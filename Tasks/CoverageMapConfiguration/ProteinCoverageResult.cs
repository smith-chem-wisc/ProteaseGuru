using Proteomics;
using Tasks;

namespace Tasks.CoverageMapConfiguration
{
    /// <summary>
    /// Holds the coverage analysis results for a single protein.
    /// Contains categorized peptide lists and protein metadata.
    /// </summary>
    public class ProteinCoverageResult
    {
        #region Properties

        /// <summary>
        /// The protein being analyzed
        /// </summary>
        public Protein Protein { get; }

        /// <summary>
        /// Display name for the protein (accession or name)
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// All peptides mapping to this protein across all proteases
        /// </summary>
        public List<InSilicoPep> AllPeptides { get; }

        /// <summary>
        /// Peptides unique to this protein (not shared with other proteins)
        /// </summary>
        public List<InSilicoPep> UniquePeptides { get; }

        /// <summary>
        /// Peptides shared with other proteins
        /// </summary>
        public List<InSilicoPep> SharedPeptides { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new ProteinCoverageResult for the specified protein
        /// </summary>
        /// <param name="protein">The protein to create results for</param>
        public ProteinCoverageResult(Protein protein)
        {
            Protein = protein ?? throw new ArgumentNullException(nameof(protein));
            DisplayName = protein.Accession ?? protein.Name;
            AllPeptides = new List<InSilicoPep>();
            UniquePeptides = new List<InSilicoPep>();
            SharedPeptides = new List<InSilicoPep>();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets unique peptide count grouped by protease
        /// </summary>
        /// <returns>Dictionary of protease name to peptide count</returns>
        public Dictionary<string, int> GetUniquePeptideCountsByProtease()
        {
            return UniquePeptides
                .GroupBy(p => p.Protease)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Gets shared peptide count grouped by protease
        /// </summary>
        /// <returns>Dictionary of protease name to peptide count</returns>
        public Dictionary<string, int> GetSharedPeptideCountsByProtease()
        {
            return SharedPeptides
                .GroupBy(p => p.Protease)
                .ToDictionary(g => g.Key, g => g.ToHashSet().Count);
        }

        #endregion
    }
}
