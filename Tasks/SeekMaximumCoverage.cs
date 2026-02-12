using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;

namespace Tasks;

/// <summary>
/// Analyzes protease combinations to find optimal coverage for a protein sequence.
/// Implements greedy and brute-force algorithms for finding minimal protease sets
/// that achieve maximum detectable peptide coverage.
/// </summary>
public class SeekMaximumCoverage
{
    #region Detectability Rules

    /// <summary>
    /// Interface for peptide detectability rules. Implement this interface to add
    /// custom detectability criteria.
    /// </summary>
    public interface IDetectabilityRule
    {
        /// <summary>
        /// Determines if a peptide passes this detectability rule.
        /// </summary>
        /// <param name="peptide">The peptide to evaluate</param>
        /// <returns>True if the peptide is detectable according to this rule</returns>
        bool IsDetectable(PeptideWithSetModifications peptide);

        /// <summary>
        /// Human-readable description of this rule for logging/debugging.
        /// </summary>
        string Description { get; }
    }

    /// <summary>
    /// Rule: Peptide length must be within specified bounds.
    /// </summary>
    public class LengthRule : IDetectabilityRule
    {
        public int MinLength { get; }
        public int MaxLength { get; }

        public LengthRule(int minLength, int maxLength)
        {
            MinLength = minLength;
            MaxLength = maxLength;
        }

        public bool IsDetectable(PeptideWithSetModifications peptide)
            => peptide.Length > MinLength && peptide.Length < MaxLength;

        public string Description => $"Length must be > {MinLength} and < {MaxLength}";
    }

    /// <summary>
    /// Rule: Peptide must contain at least one basic amino acid (K or R).
    /// </summary>
    public class BasicResidueRule : IDetectabilityRule
    {
        private static readonly HashSet<char> BasicResidues = new() { 'K', 'R' };

        public bool IsDetectable(PeptideWithSetModifications peptide)
            => peptide.BaseSequence.Any(aa => BasicResidues.Contains(aa));

        public string Description => "Must contain at least one basic residue (K or R)";
    }

    /// <summary>
    /// Rule: Peptide must not contain specified amino acids (e.g., exclude methionine-containing peptides).
    /// </summary>
    public class ExcludeResiduesRule : IDetectabilityRule
    {
        private readonly HashSet<char> _excludedResidues;

        public ExcludeResiduesRule(IEnumerable<char> excludedResidues)
        {
            _excludedResidues = new HashSet<char>(excludedResidues);
        }

        public bool IsDetectable(PeptideWithSetModifications peptide)
            => !peptide.BaseSequence.Any(aa => _excludedResidues.Contains(aa));

        public string Description => $"Must not contain: {string.Join(", ", _excludedResidues)}";
    }

    /// <summary>
    /// Rule: Peptide mass must be within specified bounds.
    /// </summary>
    public class MassRule : IDetectabilityRule
    {
        public double MinMass { get; }
        public double MaxMass { get; }

        public MassRule(double minMass, double maxMass)
        {
            MinMass = minMass;
            MaxMass = maxMass;
        }

        public bool IsDetectable(PeptideWithSetModifications peptide)
            => peptide.MonoisotopicMass >= MinMass && peptide.MonoisotopicMass <= MaxMass;

        public string Description => $"Mass must be >= {MinMass} and <= {MaxMass} Da";
    }

    /// <summary>
    /// Composite rule that requires ALL sub-rules to pass.
    /// </summary>
    public class CompositeRule : IDetectabilityRule
    {
        private readonly List<IDetectabilityRule> _rules;

        public CompositeRule(IEnumerable<IDetectabilityRule> rules)
        {
            _rules = rules.ToList();
        }

        public CompositeRule(params IDetectabilityRule[] rules)
        {
            _rules = rules.ToList();
        }

        public void AddRule(IDetectabilityRule rule) => _rules.Add(rule);

        public bool IsDetectable(PeptideWithSetModifications peptide)
            => _rules.All(rule => rule.IsDetectable(peptide));

        public string Description => string.Join(" AND ", _rules.Select(r => $"({r.Description})"));
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Configuration options for the coverage analysis.
    /// </summary>
    public class CoverageAnalysisConfig
    {
        /// <summary>
        /// Maximum number of missed cleavages allowed during digestion.
        /// </summary>
        public int MaxMissedCleavages { get; set; } = 2;

        /// <summary>
        /// Minimum peptide length for digestion (separate from detectability rules).
        /// </summary>
        public int MinPeptideLength { get; set; } = 1;

        /// <summary>
        /// Maximum peptide length for digestion (separate from detectability rules).
        /// </summary>
        public int MaxPeptideLength { get; set; } = 100;

        /// <summary>
        /// The detectability rule(s) to apply. If null, no filtering is applied.
        /// </summary>
        public IDetectabilityRule? DetectabilityRule { get; set; }
    }

    #endregion

    #region Result Types

    /// <summary>
    /// Result of a set cover algorithm.
    /// </summary>
    public record SetCoverResult(
        List<string> SelectedProteases,
        HashSet<int> CoveredResidues,
        int TotalResidues,
        double CoverageFraction
    );

    /// <summary>
    /// Result of a protease combination search.
    /// </summary>
    public record CombinationResult(
        List<string> Proteases,
        HashSet<int> CoveredResidues,
        int CoverageCount,
        double CoverageFraction
    );

    #endregion

    #region Fields

    private readonly CoverageAnalysisConfig _config;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new SeekMaximumCoverage analyzer with the specified configuration.
    /// </summary>
    /// <param name="config">Configuration options. If null, default config is used.</param>
    public SeekMaximumCoverage(CoverageAnalysisConfig? config = null)
    {
        _config = config ?? new CoverageAnalysisConfig();
    }

    /// <summary>
    /// Creates a new SeekMaximumCoverage analyzer with common detectability rules.
    /// </summary>
    /// <param name="minLength">Minimum peptide length (exclusive)</param>
    /// <param name="maxLength">Maximum peptide length (exclusive)</param>
    /// <param name="requireBasicResidue">Require at least one K or R</param>
    public static SeekMaximumCoverage WithDefaultRules(
        int minLength = 6,
        int maxLength = 30,
        bool requireBasicResidue = true)
    {
        var rules = new List<IDetectabilityRule>
        {
            new LengthRule(minLength, maxLength)
        };

        if (requireBasicResidue)
        {
            rules.Add(new BasicResidueRule());
        }

        return new SeekMaximumCoverage(new CoverageAnalysisConfig
        {
            DetectabilityRule = new CompositeRule(rules)
        });
    }

    #endregion

    #region STEP 1: Coverage Calculation

    /// <summary>
    /// Digests a protein with each protease, filters peptides by detectability rules,
    /// and maps each valid peptide back to its residue index positions (0-based).
    /// </summary>
    /// <param name="protein">The protein to digest</param>
    /// <param name="proteases">List of proteases to use</param>
    /// <returns>Dictionary mapping protease name to set of covered residue indices (0-based)</returns>
    /// <remarks>
    /// Handles peptides that appear multiple times in the protein sequence by
    /// finding all occurrences and mapping each to its correct position.
    /// </remarks>
    public Dictionary<string, HashSet<int>> CalculateCoverageByProtease(
        Protein protein,
        IEnumerable<Protease> proteases)
    {
        var coverage = new Dictionary<string, HashSet<int>>();

        foreach (var protease in proteases)
        {
            var coveredIndices = new HashSet<int>();

            // Create digestion parameters
            var digestionParams = new DigestionParams(
                protease: protease.Name,
                maxMissedCleavages: _config.MaxMissedCleavages,
                minPeptideLength: _config.MinPeptideLength,
                maxPeptideLength: _config.MaxPeptideLength);

            // Digest the protein
            var peptides = protein.Digest(digestionParams, new List<Modification>(), new List<Modification>());

            foreach (var peptide in peptides)
            {
                // Apply detectability rules
                if (_config.DetectabilityRule != null && !_config.DetectabilityRule.IsDetectable(peptide))
                {
                    continue;
                }

                // Map peptide to residue indices (0-based)
                // OneBasedStartResidueInProtein is 1-based, convert to 0-based
                int startIndex = peptide.OneBasedStartResidueInProtein - 1;
                int endIndex = peptide.OneBasedEndResidueInProtein - 1;

                // Add all covered residue indices
                for (int i = startIndex; i <= endIndex; i++)
                {
                    coveredIndices.Add(i);
                }
            }

            coverage[protease.Name] = coveredIndices;
        }

        return coverage;
    }

    /// <summary>
    /// Overload that accepts protease names and looks them up in the standard dictionary.
    /// </summary>
    public Dictionary<string, HashSet<int>> CalculateCoverageByProtease(
        Protein protein,
        IEnumerable<string> proteaseNames)
    {
        var proteases = proteaseNames
            .Where(name => ProteaseDictionary.Dictionary.ContainsKey(name))
            .Select(name => ProteaseDictionary.Dictionary[name]);

        return CalculateCoverageByProtease(protein, proteases);
    }

    #endregion

    #region STEP 2: Greedy Set Cover

    /// <summary>
    /// Implements a greedy set cover algorithm to find a minimal set of proteases
    /// that achieves maximum coverage.
    /// </summary>
    /// <param name="coverageDict">Dictionary mapping protease name to covered residue indices</param>
    /// <param name="region">Optional tuple (start, end) restricting coverage to a region (0-based, inclusive)</param>
    /// <returns>SetCoverResult containing selected proteases and final coverage</returns>
    /// <remarks>
    /// Algorithm:
    /// 1. Start with empty coverage set
    /// 2. Repeatedly select the protease that covers the most uncovered residues
    /// 3. Stop when no protease can add new coverage
    /// 
    /// Time complexity: O(P * R) where P = number of proteases, R = number of residues
    /// </remarks>
    public SetCoverResult GreedyMinimumProteaseSet(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
    {
        // Filter coverage to region if specified
        var workingCoverage = FilterCoverageToRegion(coverageDict, region);

        // Determine total residues in region
        int totalResidues = region.HasValue
            ? region.Value.End - region.Value.Start + 1
            : workingCoverage.Values.SelectMany(s => s).DefaultIfEmpty(-1).Max() + 1;

        // Track selected proteases and cumulative coverage
        var selectedProteases = new List<string>();
        var totalCovered = new HashSet<int>();

        // Create mutable copy of coverage sets
        var remainingCoverage = workingCoverage.ToDictionary(
            kvp => kvp.Key,
            kvp => new HashSet<int>(kvp.Value));

        // Greedy selection loop
        while (true)
        {
            // Find protease that covers the most NEW residues
            string? bestProtease = null;
            int bestNewCoverage = 0;
            HashSet<int>? bestNewResidues = null;

            foreach (var kvp in remainingCoverage)
            {
                // Calculate residues this protease covers that aren't already covered
                var newResidues = new HashSet<int>(kvp.Value);
                newResidues.ExceptWith(totalCovered);

                if (newResidues.Count > bestNewCoverage)
                {
                    bestProtease = kvp.Key;
                    bestNewCoverage = newResidues.Count;
                    bestNewResidues = newResidues;
                }
            }

            // Stop if no protease adds new coverage
            if (bestProtease == null || bestNewCoverage == 0)
            {
                break;
            }

            // Add best protease to selection
            selectedProteases.Add(bestProtease);
            totalCovered.UnionWith(bestNewResidues!);

            // Remove selected protease from candidates
            remainingCoverage.Remove(bestProtease);
        }

        return new SetCoverResult(
            selectedProteases,
            totalCovered,
            totalResidues,
            CoverageFraction(totalCovered, totalResidues)
        );
    }

    /// <summary>
    /// Filters coverage dictionaries to only include residues within the specified region.
    /// </summary>
    private static Dictionary<string, HashSet<int>> FilterCoverageToRegion(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region)
    {
        if (!region.HasValue)
        {
            return coverageDict;
        }

        int start = region.Value.Start;
        int end = region.Value.End;

        return coverageDict.ToDictionary(
            kvp => kvp.Key,
            kvp => new HashSet<int>(kvp.Value.Where(i => i >= start && i <= end))
        );
    }

    #endregion

    #region STEP 3: Brute-Force Combinations

    /// <summary>
    /// Finds the best pair of proteases that maximizes coverage.
    /// </summary>
    /// <param name="coverageDict">Dictionary mapping protease name to covered residue indices</param>
    /// <param name="region">Optional tuple (start, end) restricting coverage to a region</param>
    /// <returns>CombinationResult with best pair and their combined coverage</returns>
    public CombinationResult BestPair(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
    {
        return BestCombination(coverageDict, 2, region);
    }

    /// <summary>
    /// Finds the best triplet of proteases that maximizes coverage.
    /// </summary>
    /// <param name="coverageDict">Dictionary mapping protease name to covered residue indices</param>
    /// <param name="region">Optional tuple (start, end) restricting coverage to a region</param>
    /// <returns>CombinationResult with best triplet and their combined coverage</returns>
    public CombinationResult BestTriplet(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
    {
        return BestCombination(coverageDict, 3, region);
    }

    /// <summary>
    /// Finds the best combination of N proteases that maximizes coverage.
    /// </summary>
    /// <param name="coverageDict">Dictionary mapping protease name to covered residue indices</param>
    /// <param name="combinationSize">Number of proteases in each combination</param>
    /// <param name="region">Optional tuple (start, end) restricting coverage to a region</param>
    /// <returns>CombinationResult with best combination and their combined coverage</returns>
    /// <remarks>
    /// Uses brute-force enumeration of all combinations.
    /// Time complexity: O(C(P, N) * R) where P = proteases, N = combination size, R = residues
    /// </remarks>
    public CombinationResult BestCombination(
        Dictionary<string, HashSet<int>> coverageDict,
        int combinationSize,
        (int Start, int End)? region = null)
    {
        // Filter coverage to region if specified
        var workingCoverage = FilterCoverageToRegion(coverageDict, region);

        // Determine total residues
        int totalResidues = region.HasValue
            ? region.Value.End - region.Value.Start + 1
            : workingCoverage.Values.SelectMany(s => s).DefaultIfEmpty(-1).Max() + 1;

        var proteaseNames = workingCoverage.Keys.ToList();

        // Handle edge cases
        if (proteaseNames.Count < combinationSize)
        {
            // Return all proteases if we don't have enough
            var allCovered = new HashSet<int>();
            foreach (var coverage in workingCoverage.Values)
            {
                allCovered.UnionWith(coverage);
            }

            return new CombinationResult(
                proteaseNames,
                allCovered,
                allCovered.Count,
                CoverageFraction(allCovered, totalResidues)
            );
        }

        // Track best combination
        List<string>? bestCombination = null;
        HashSet<int>? bestCoverage = null;
        int bestCoverageCount = -1;

        // Enumerate all combinations of size N
        foreach (var combination in GetCombinations(proteaseNames, combinationSize))
        {
            // Calculate combined coverage
            var combinedCoverage = new HashSet<int>();
            foreach (var protease in combination)
            {
                combinedCoverage.UnionWith(workingCoverage[protease]);
            }

            // Check if this is the best so far
            if (combinedCoverage.Count > bestCoverageCount)
            {
                bestCoverageCount = combinedCoverage.Count;
                bestCoverage = combinedCoverage;
                bestCombination = combination.ToList();
            }
        }

        return new CombinationResult(
            bestCombination ?? new List<string>(),
            bestCoverage ?? new HashSet<int>(),
            bestCoverageCount,
            CoverageFraction(bestCoverage ?? new HashSet<int>(), totalResidues)
        );
    }

    /// <summary>
    /// Generates all combinations of a given size from a list.
    /// </summary>
    private static IEnumerable<IEnumerable<T>> GetCombinations<T>(List<T> list, int length)
    {
        if (length == 0)
        {
            yield return Enumerable.Empty<T>();
            yield break;
        }

        for (int i = 0; i <= list.Count - length; i++)
        {
            var head = list[i];
            var tail = list.Skip(i + 1).ToList();

            foreach (var combination in GetCombinations(tail, length - 1))
            {
                yield return new[] { head }.Concat(combination);
            }
        }
    }

    #endregion

    #region STEP 4: Coverage Fraction

    /// <summary>
    /// Calculates the fraction of residues covered.
    /// </summary>
    /// <param name="coverageSet">Set of covered residue indices</param>
    /// <param name="regionSize">Total number of residues in the region</param>
    /// <returns>Coverage fraction as a value between 0.0 and 1.0</returns>
    public static double CoverageFraction(HashSet<int> coverageSet, int regionSize)
    {
        if (regionSize <= 0)
        {
            return 0.0;
        }

        return (double)coverageSet.Count / regionSize;
    }

    /// <summary>
    /// Calculates the coverage fraction as a percentage string.
    /// </summary>
    public static string CoveragePercentage(HashSet<int> coverageSet, int regionSize, int decimals = 2)
    {
        return $"{Math.Round(CoverageFraction(coverageSet, regionSize) * 100, decimals)}%";
    }

    #endregion

}
