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
    /// Rule: Peptide must not contain specified amino acids.
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

    private readonly IDetectabilityRule? _detectabilityRule;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new SeekMaximumCoverage analyzer with an optional detectability rule.
    /// Digestion parameters (missed cleavages, peptide length bounds) are supplied
    /// per-protease via <see cref="ProteaseSpecificParameters"/> rather than duplicated here.
    /// </summary>
    /// <param name="detectabilityRule">Optional rule to filter peptides. If null, all peptides are used.</param>
    public SeekMaximumCoverage(IDetectabilityRule? detectabilityRule = null)
    {
        _detectabilityRule = detectabilityRule;
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

        return new SeekMaximumCoverage(new CompositeRule(rules));
    }

    #endregion

    #region STEP 1: Coverage Calculation

    /// <summary>
    /// Digests a protein using each protease's own <see cref="ProteaseSpecificParameters"/>,
    /// filters peptides by the detectability rule, and maps valid peptides to 0-based residue indices.
    /// </summary>
    /// <param name="protein">The protein to digest</param>
    /// <param name="proteaseParams">
    /// Per-protease digestion settings. Missed cleavages, peptide length bounds, and
    /// modifications are read from each entry's <see cref="ProteaseSpecificParameters.DigestionParams"/>.
    /// </param>
    /// <returns>Dictionary mapping protease name to set of covered residue indices (0-based)</returns>
    public Dictionary<string, HashSet<int>> CalculateCoverageByProtease(
        Protein protein,
        IEnumerable<ProteaseSpecificParameters> proteaseParams)
    {
        var coverage = new Dictionary<string, HashSet<int>>();

        foreach (var proteaseParam in proteaseParams)
        {
            var coveredIndices = new HashSet<int>();

            var peptides = protein.Digest(
                proteaseParam.DigestionParams,
                proteaseParam.FixedMods,
                proteaseParam.VariableMods);

            foreach (PeptideWithSetModifications peptide in peptides)
            {
                if (_detectabilityRule != null && !_detectabilityRule.IsDetectable(peptide))
                    continue;

                // OneBasedStartResidueInProtein is 1-based, convert to 0-based
                int startIndex = peptide.OneBasedStartResidue - 1;
                int endIndex = peptide.OneBasedEndResidue - 1;

                for (int i = startIndex; i <= endIndex; i++)
                {
                    coveredIndices.Add(i);
                }
            }

            coverage[proteaseParam.DigestionAgentName] = coveredIndices;
        }

        return coverage;
    }

    /// <summary>
    /// Overload that accepts protease names and looks them up in the standard dictionary,
    /// using default digestion parameters.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if any protease name is not found in the dictionary.</exception>
    public Dictionary<string, HashSet<int>> CalculateCoverageByProtease(
        Protein protein,
        IEnumerable<string> proteaseNames)
    {
        var namesList = proteaseNames.ToList();

        var missing = namesList
            .Where(name => !ProteaseDictionary.Dictionary.ContainsKey(name))
            .ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"The following protease name(s) were not found in the dictionary: " +
                $"{string.Join(", ", missing.Select(n => $"'{n}'"))}. " +
                $"Check proteases.tsv for valid names.");
        }

        var proteaseParams = namesList.Select(name =>
        {
            var dp = new DigestionParams(protease: name);
            return new ProteaseSpecificParameters(dp);
        });

        return CalculateCoverageByProtease(protein, proteaseParams);
    }

    /// <summary>
    /// Returns the 1-based (Start, End) intervals of every peptide that passes the
    /// detectability rule, using exactly the same digest and filter logic as
    /// <see cref="CalculateCoverageByProtease(Protein, IEnumerable{ProteaseSpecificParameters})"/>.
    /// Use this to draw coverage-map bars that are guaranteed to match the coverage numbers.
    /// </summary>
    /// <returns>
    /// Dictionary mapping protease name → deduplicated, start-sorted list of
    /// (OneBasedStart, OneBasedEnd) intervals.
    /// </returns>
    public Dictionary<string, List<(int Start, int End)>> GetDetectablePeptideIntervals(
        Protein protein,
        IEnumerable<ProteaseSpecificParameters> proteaseParams)
    {
        var result = new Dictionary<string, List<(int Start, int End)>>();

        foreach (var proteaseParam in proteaseParams)
        {
            var intervals = new List<(int, int)>();

            var peptides = protein.Digest(
                proteaseParam.DigestionParams,
                proteaseParam.FixedMods,
                proteaseParam.VariableMods);

            foreach (PeptideWithSetModifications peptide in peptides)
            {
                if (_detectabilityRule != null && !_detectabilityRule.IsDetectable(peptide))
                    continue;

                intervals.Add((peptide.OneBasedStartResidue, peptide.OneBasedEndResidue));
            }

            // Deduplicate (same span from different mod combos) and sort
            result[proteaseParam.DigestionAgentName] = intervals
                .Distinct()
                .OrderBy(t => t.Item1)
                .ToList();
        }

        return result;
    }

    #endregion

    #region STEP 2: Greedy Set Cover

    /// <summary>
    /// Implements a greedy set cover algorithm to find a minimal set of proteases
    /// that achieves maximum coverage.
    /// </summary>
    public SetCoverResult GreedyMinimumProteaseSet(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
    {
        var workingCoverage = FilterCoverageToRegion(coverageDict, region);

        int totalResidues = region.HasValue
            ? region.Value.End - region.Value.Start + 1
            : workingCoverage.Values.SelectMany(s => s).DefaultIfEmpty(-1).Max() + 1;

        var selectedProteases = new List<string>();
        var totalCovered = new HashSet<int>();

        var remainingCoverage = workingCoverage.ToDictionary(
            kvp => kvp.Key,
            kvp => new HashSet<int>(kvp.Value));

        while (true)
        {
            string? bestProtease = null;
            int bestNewCoverage = 0;
            HashSet<int>? bestNewResidues = null;

            foreach (var kvp in remainingCoverage)
            {
                var newResidues = new HashSet<int>(kvp.Value);
                newResidues.ExceptWith(totalCovered);

                if (newResidues.Count > bestNewCoverage)
                {
                    bestProtease = kvp.Key;
                    bestNewCoverage = newResidues.Count;
                    bestNewResidues = newResidues;
                }
            }

            if (bestProtease == null || bestNewCoverage == 0)
                break;

            selectedProteases.Add(bestProtease);
            totalCovered.UnionWith(bestNewResidues!);
            remainingCoverage.Remove(bestProtease);
        }

        return new SetCoverResult(
            selectedProteases,
            totalCovered,
            totalResidues,
            CoverageFraction(totalCovered, totalResidues)
        );
    }

    private static Dictionary<string, HashSet<int>> FilterCoverageToRegion(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region)
    {
        if (!region.HasValue)
            return coverageDict;

        int start = region.Value.Start;
        int end = region.Value.End;

        return coverageDict.ToDictionary(
            kvp => kvp.Key,
            kvp => new HashSet<int>(kvp.Value.Where(i => i >= start && i <= end))
        );
    }

    #endregion

    #region STEP 3: Brute-Force Combinations

    /// <summary>Finds the single protease that alone produces the highest sequence coverage.</summary>
    public CombinationResult BestSingle(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
        => BestCombination(coverageDict, 1, region);

    /// <summary>Finds the best pair of proteases that maximizes coverage.</summary>
    public CombinationResult BestPair(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
        => BestCombination(coverageDict, 2, region);

    /// <summary>Finds the best triplet of proteases that maximizes coverage.</summary>
    public CombinationResult BestTriplet(
        Dictionary<string, HashSet<int>> coverageDict,
        (int Start, int End)? region = null)
        => BestCombination(coverageDict, 3, region);

    /// <summary>Finds the best combination of N proteases that maximizes coverage.</summary>
    public CombinationResult BestCombination(
        Dictionary<string, HashSet<int>> coverageDict,
        int combinationSize,
        (int Start, int End)? region = null)
    {
        var workingCoverage = FilterCoverageToRegion(coverageDict, region);

        int totalResidues = region.HasValue
            ? region.Value.End - region.Value.Start + 1
            : workingCoverage.Values.SelectMany(s => s).DefaultIfEmpty(-1).Max() + 1;

        var proteaseNames = workingCoverage.Keys.ToList();

        if (proteaseNames.Count < combinationSize)
        {
            var allCovered = new HashSet<int>();
            foreach (var coverage in workingCoverage.Values)
                allCovered.UnionWith(coverage);

            return new CombinationResult(
                proteaseNames, allCovered, allCovered.Count,
                CoverageFraction(allCovered, totalResidues));
        }

        List<string>? bestCombination = null;
        HashSet<int>? bestCoverage = null;
        int bestCoverageCount = -1;

        foreach (var combination in GetCombinations(proteaseNames, combinationSize))
        {
            var combinedCoverage = new HashSet<int>();
            foreach (var protease in combination)
                combinedCoverage.UnionWith(workingCoverage[protease]);

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
                yield return new[] { head }.Concat(combination);
        }
    }

    #endregion

    #region STEP 4: Coverage Fraction

    /// <summary>Calculates the fraction of residues covered (0.0–1.0).</summary>
    public static double CoverageFraction(HashSet<int> coverageSet, int regionSize)
    {
        if (regionSize <= 0)
            return 0.0;

        return (double)coverageSet.Count / regionSize;
    }

    /// <summary>Calculates the coverage fraction as a percentage string.</summary>
    public static string CoveragePercentage(HashSet<int> coverageSet, int regionSize, int decimals = 2)
        => $"{Math.Round(CoverageFraction(coverageSet, regionSize) * 100, decimals)}%";

    #endregion
}
