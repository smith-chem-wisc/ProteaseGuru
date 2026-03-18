using Proteomics;
using Proteomics.ProteolyticDigestion;

namespace Tasks;

/// <summary>
/// Analyzes protease combinations to find optimal coverage for a protein sequence.
/// Implements greedy and brute-force algorithms for finding minimal protease sets
/// that achieve maximum detectable peptide coverage.
///
/// Peptide filtering is handled entirely by the per-protease <see cref="ProteaseSpecificParameters"/>
/// (which encodes missed cleavages, length bounds, and modifications via <see cref="DigestionParams"/>)
/// and by the global <see cref="RunParameters"/> (which encodes peptide mass bounds).
/// No separate detectability-rule infrastructure is needed.
/// </summary>
public class SeekMaximumCoverage
{
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

    /// <summary>
    /// Optional global run parameters. When provided, peptide mass bounds
    /// (<see cref="RunParameters.MinPeptideMassAllowed"/> and
    /// <see cref="RunParameters.MaxPeptideMassAllowed"/>) are applied as an
    /// additional filter after digestion. A value of -1 means the bound is unset.
    /// Length and missed-cleavage bounds are already enforced by each protease's
    /// own <see cref="ProteaseSpecificParameters.DigestionParams"/>.
    /// </summary>
    private readonly RunParameters? _runParameters;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new SeekMaximumCoverage analyzer.
    /// </summary>
    /// <param name="runParameters">
    /// Optional global parameters. When supplied, peptide mass bounds are applied
    /// as an additional post-digestion filter. Pass <c>null</c> (default) to apply
    /// no mass filter.
    /// </param>
    public SeekMaximumCoverage(RunParameters? runParameters = null)
    {
        _runParameters = runParameters;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Returns true if the peptide passes the global mass bounds defined in
    /// <see cref="RunParameters"/>. Always returns true when no parameters are set
    /// or when the relevant bound is -1 (unset).
    /// </summary>
    private bool PassesMassFilter(PeptideWithSetModifications peptide)
    {
        if (_runParameters == null)
            return true;

        if (_runParameters.MinPeptideMassAllowed != -1
            && peptide.MonoisotopicMass < _runParameters.MinPeptideMassAllowed)
            return false;

        if (_runParameters.MaxPeptideMassAllowed != -1
            && peptide.MonoisotopicMass > _runParameters.MaxPeptideMassAllowed)
            return false;

        return true;
    }

    #endregion

    #region STEP 1: Coverage Calculation

    /// <summary>
    /// Digests a protein using each protease's own <see cref="ProteaseSpecificParameters"/>
    /// and maps qualifying peptides to 0-based residue indices.
    ///
    /// Length, missed-cleavage, and modification filters are applied by <see cref="DigestionParams"/>
    /// during digestion. An optional peptide mass filter is applied from <see cref="RunParameters"/>.
    /// </summary>
    /// <param name="protein">The protein to digest.</param>
    /// <param name="proteaseParams">
    /// Per-protease digestion settings. Missed cleavages, peptide length bounds, and
    /// modifications are read from each entry's <see cref="ProteaseSpecificParameters.DigestionParams"/>.
    /// </param>
    /// <returns>Dictionary mapping protease name to set of covered residue indices (0-based).</returns>
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
                if (!PassesMassFilter(peptide))
                    continue;

                // OneBasedStartResidue is 1-based; convert to 0-based
                for (int i = peptide.OneBasedStartResidue - 1; i <= peptide.OneBasedEndResidue - 1; i++)
                    coveredIndices.Add(i);
            }

            coverage[proteaseParam.DigestionAgentName] = coveredIndices;
        }

        return coverage;
    }

    /// <summary>
    /// Returns the 1-based (Start, End) intervals of every peptide that passes the
    /// mass filter, using exactly the same digest and filter logic as
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
                if (!PassesMassFilter(peptide))
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
