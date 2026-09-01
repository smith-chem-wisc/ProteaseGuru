using System.Numerics;
using Omics;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Transcriptomics.Digestion;

namespace ProteaseGuru.Tasks;

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
    private bool PassesMassFilter(IBioPolymerWithSetMods peptide)
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

    /// <summary>
    /// Fixed-length bitset over residue indices. Coverage is a dense set over
    /// [0, protein.Length), so the set-cover math runs as word-parallel bitwise ops
    /// (OR / AND-NOT / popcount) instead of per-residue <see cref="HashSet{T}"/> work.
    /// All bitsets in a single algorithm run share the same universe size, so the
    /// binary operations can assume equal backing-array lengths.
    /// </summary>
    private sealed class Bitset
    {
        private readonly ulong[] _words;

        public Bitset(int size) => _words = new ulong[(size + 63) >> 6];

        private Bitset(ulong[] words) => _words = words;

        public void Set(int index) => _words[index >> 6] |= 1UL << (index & 63);

        public void Clear() => Array.Clear(_words, 0, _words.Length);

        public void OrWith(Bitset other)
        {
            var a = _words;
            var b = other._words;
            for (int i = 0; i < a.Length; i++)
                a[i] |= b[i];
        }

        /// <summary>Count of bits set here but not in <paramref name="other"/>.</summary>
        public int CountAndNot(Bitset other)
        {
            var a = _words;
            var b = other._words;
            int count = 0;
            for (int i = 0; i < a.Length; i++)
                count += BitOperations.PopCount(a[i] & ~b[i]);
            return count;
        }

        public int PopCount()
        {
            int count = 0;
            foreach (var word in _words)
                count += BitOperations.PopCount(word);
            return count;
        }

        public Bitset Clone() => new((ulong[])_words.Clone());

        public HashSet<int> ToHashSet()
        {
            var result = new HashSet<int>();
            for (int w = 0; w < _words.Length; w++)
            {
                ulong word = _words[w];
                while (word != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(word);
                    result.Add((w << 6) + bit);
                    word &= word - 1;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Builds a per-protease <see cref="Bitset"/> from the coverage dictionary, applying
    /// the optional region filter while setting bits. Returns the protease names (in the
    /// dictionary's enumeration order, which the greedy and brute-force tie-breaks rely on),
    /// the bitsets, and the universe size. The universe only sizes the backing arrays; the
    /// denominator coverage fractions are reported against comes from the caller, via
    /// <see cref="ResolveDenominator"/>.
    /// </summary>
    private static (List<string> Names, Bitset[] Sets, int Universe)
        BuildProteaseBitsets(
            Dictionary<string, HashSet<int>> coverageDict,
            (int Start, int End)? region)
    {
        var names = coverageDict.Keys.ToList();

        int universe;
        if (region.HasValue)
        {
            universe = region.Value.End + 1;
        }
        else
        {
            int maxIndex = -1;
            foreach (var set in coverageDict.Values)
                foreach (var i in set)
                    if (i > maxIndex) maxIndex = i;
            universe = maxIndex + 1;
        }

        var sets = new Bitset[names.Count];
        for (int n = 0; n < names.Count; n++)
        {
            var bits = new Bitset(universe);
            if (region.HasValue)
            {
                int start = region.Value.Start;
                int end = region.Value.End;
                foreach (var i in coverageDict[names[n]])
                    if (i >= start && i <= end) bits.Set(i);
            }
            else
            {
                foreach (var i in coverageDict[names[n]])
                    bits.Set(i);
            }
            sets[n] = bits;
        }

        return (names, sets, universe);
    }

    /// <summary>
    /// Picks the residue count a coverage fraction is reported against. A region carries its own
    /// span, so it wins; otherwise the caller-supplied total is used verbatim.
    /// </summary>
    private static int ResolveDenominator(int totalResidues, (int Start, int End)? region)
    {
        if (region.HasValue)
            return region.Value.End - region.Value.Start + 1;

        if (totalResidues < 0)
            throw new ArgumentOutOfRangeException(nameof(totalResidues), totalResidues,
                "Residue count for the coverage fraction cannot be negative.");

        return totalResidues;
    }

    #endregion

    #region STEP 1: Coverage Calculation

    /// <summary>
    /// Digests a protein with a single protease's parameters, returning both the 0-based
    /// covered-residue set and the deduplicated, start-sorted 1-based peptide intervals
    /// from one digest pass.
    ///
    /// This is the digestion primitive the batch methods below build on. The protein is only read
    /// and the results are local, so callers may invoke it concurrently for different proteases on
    /// the same protein. Note that digestion does write to <paramref name="proteaseParam"/>: it
    /// appends the agent's cleavage modification to <see cref="ProteaseSpecificParameters.FixedMods"/>.
    /// That stays safe only while each concurrent call is given its own protease's parameters.
    /// </summary>
    public (HashSet<int> Coverage, List<(int Start, int End)> Intervals) DigestSingle(
        IBioPolymer protein,
        ProteaseSpecificParameters proteaseParam)
    {
        var coveredIndices = new HashSet<int>();
        var rawIntervals = new List<(int, int)>();

        var peptides = protein.Digest(
            proteaseParam.DigestionParams,
            proteaseParam.FixedMods,
            proteaseParam.VariableMods);

        foreach (IBioPolymerWithSetMods peptide in peptides)
        {
            if (!PassesMassFilter(peptide))
                continue;

            int start = peptide.OneBasedStartResidue;
            int end = peptide.OneBasedEndResidue;

            rawIntervals.Add((start, end));

            // OneBasedStartResidue is 1-based; convert to 0-based for coverage sets.
            for (int i = start - 1; i <= end - 1; i++)
                coveredIndices.Add(i);
        }

        // Deduplicate (same span from different mod combos) and sort by start.
        var intervals = rawIntervals.Distinct().OrderBy(t => t.Item1).ToList();
        return (coveredIndices, intervals);
    }

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
        IBioPolymer protein,
        IEnumerable<ProteaseSpecificParameters> proteaseParams)
    {
        var coverage = new Dictionary<string, HashSet<int>>();

        foreach (var proteaseParam in proteaseParams)
            coverage[proteaseParam.DigestionAgentName] = DigestSingle(protein, proteaseParam).Coverage;

        return coverage;
    }

    /// <summary>
    /// Returns the 1-based (Start, End) intervals of every peptide that passes the
    /// mass filter, using exactly the same digest and filter logic as
    /// <see cref="CalculateCoverageByProtease(IBioPolymer, IEnumerable{ProteaseSpecificParameters})"/>.
    /// Use this to draw coverage-map bars that are guaranteed to match the coverage numbers.
    /// </summary>
    /// <returns>
    /// Dictionary mapping protease name → deduplicated, start-sorted list of
    /// (OneBasedStart, OneBasedEnd) intervals.
    /// </returns>
    public Dictionary<string, List<(int Start, int End)>> GetDetectablePeptideIntervals(
        IBioPolymer protein,
        IEnumerable<ProteaseSpecificParameters> proteaseParams)
    {
        var result = new Dictionary<string, List<(int Start, int End)>>();

        foreach (var proteaseParam in proteaseParams)
            result[proteaseParam.DigestionAgentName] = DigestSingle(protein, proteaseParam).Intervals;

        return result;
    }

    /// <summary>
    /// Performs a single digest pass per protease, producing both the 0-based coverage
    /// set (used by combination-search algorithms) and the 1-based interval list (used
    /// for drawing coverage maps) simultaneously.
    ///
    /// Callers that previously invoked <see cref="CalculateCoverageByProtease"/> and
    /// <see cref="GetDetectablePeptideIntervals"/> back-to-back on the same protein/params
    /// should use this method instead to halve the number of digestions performed.
    /// </summary>
    public (Dictionary<string, HashSet<int>> Coverage,
            Dictionary<string, List<(int Start, int End)>> Intervals)
        CalculateCoverageAndIntervals(
            IBioPolymer protein,
            IEnumerable<ProteaseSpecificParameters> proteaseParams)
    {
        var coverage = new Dictionary<string, HashSet<int>>();
        var intervals = new Dictionary<string, List<(int Start, int End)>>();

        foreach (var proteaseParam in proteaseParams)
        {
            var (cov, iv) = DigestSingle(protein, proteaseParam);
            coverage[proteaseParam.DigestionAgentName] = cov;
            intervals[proteaseParam.DigestionAgentName] = iv;
        }

        return (coverage, intervals);
    }

    #endregion

    #region STEP 2: Greedy Set Cover

    /// <summary>
    /// Implements a greedy set cover algorithm to find a minimal set of proteases
    /// that achieves maximum coverage.
    /// </summary>
    /// <param name="totalResidues">
    /// Residue count the coverage fraction is reported against — normally the biopolymer's length.
    /// Required because the coverage sets alone cannot reveal it: residues past the last covered
    /// one leave no trace, so deriving the denominator from the coverage silently drops any
    /// uncovered C-terminus from both numerator and denominator. Ignored when
    /// <paramref name="region"/> is supplied, since a region carries its own span.
    /// </param>
    public SetCoverResult GreedyMinimumProteaseSet(
        Dictionary<string, HashSet<int>> coverageDict,
        int totalResidues,
        (int Start, int End)? region = null)
    {
        var (names, sets, universe) = BuildProteaseBitsets(coverageDict, region);
        int denominator = ResolveDenominator(totalResidues, region);

        var selectedProteases = new List<string>();
        var covered = new Bitset(universe);
        var used = new bool[names.Count];

        while (true)
        {
            int bestIdx = -1;
            int bestNewCoverage = 0;

            for (int j = 0; j < names.Count; j++)
            {
                if (used[j]) continue;

                int gain = sets[j].CountAndNot(covered);
                if (gain > bestNewCoverage)
                {
                    bestNewCoverage = gain;
                    bestIdx = j;
                }
            }

            if (bestIdx == -1 || bestNewCoverage == 0)
                break;

            selectedProteases.Add(names[bestIdx]);
            covered.OrWith(sets[bestIdx]);
            used[bestIdx] = true;
        }

        var coveredResidues = covered.ToHashSet();
        return new SetCoverResult(
            selectedProteases,
            coveredResidues,
            denominator,
            CoverageFraction(coveredResidues, denominator)
        );
    }

    #endregion

    #region STEP 3: Brute-Force Combinations

    /// <summary>Finds the single protease that alone produces the highest sequence coverage.</summary>
    public CombinationResult BestSingle(
        Dictionary<string, HashSet<int>> coverageDict,
        int totalResidues,
        (int Start, int End)? region = null)
        => BestCombination(coverageDict, 1, totalResidues, region);

    /// <summary>Finds the best pair of proteases that maximizes coverage.</summary>
    public CombinationResult BestPair(
        Dictionary<string, HashSet<int>> coverageDict,
        int totalResidues,
        (int Start, int End)? region = null)
        => BestCombination(coverageDict, 2, totalResidues, region);

    /// <summary>Finds the best triplet of proteases that maximizes coverage.</summary>
    public CombinationResult BestTriplet(
        Dictionary<string, HashSet<int>> coverageDict,
        int totalResidues,
        (int Start, int End)? region = null)
        => BestCombination(coverageDict, 3, totalResidues, region);

    /// <summary>Finds the best combination of N proteases that maximizes coverage.</summary>
    /// <param name="totalResidues">
    /// Residue count the coverage fraction is reported against — normally the biopolymer's length.
    /// Required because the coverage sets alone cannot reveal it: residues past the last covered
    /// one leave no trace, so deriving the denominator from the coverage silently drops any
    /// uncovered C-terminus from both numerator and denominator. Ignored when
    /// <paramref name="region"/> is supplied, since a region carries its own span.
    /// </param>
    public CombinationResult BestCombination(
        Dictionary<string, HashSet<int>> coverageDict,
        int combinationSize,
        int totalResidues,
        (int Start, int End)? region = null)
    {
        var (names, sets, universe) = BuildProteaseBitsets(coverageDict, region);
        int denominator = ResolveDenominator(totalResidues, region);

        if (names.Count < combinationSize)
        {
            var allCovered = new Bitset(universe);
            foreach (var bits in sets)
                allCovered.OrWith(bits);

            var union = allCovered.ToHashSet();
            return new CombinationResult(
                names, union, union.Count,
                CoverageFraction(union, denominator));
        }

        int n = names.Count;
        var combo = new int[combinationSize];
        var scratch = new Bitset(universe);

        int bestCount = -1;
        int[]? bestCombo = null;
        Bitset? bestCoverage = null;

        // Enumerate index combinations in lexicographic order (matching the previous
        // generator), unioning each into a reused scratch bitset and keeping the first
        // combination that strictly improves on the best coverage seen so far.
        void Search(int start, int depth)
        {
            if (depth == combinationSize)
            {
                scratch.Clear();
                for (int d = 0; d < combinationSize; d++)
                    scratch.OrWith(sets[combo[d]]);

                int count = scratch.PopCount();
                if (count > bestCount)
                {
                    bestCount = count;
                    bestCombo = (int[])combo.Clone();
                    bestCoverage = scratch.Clone();
                }
                return;
            }

            int last = n - (combinationSize - depth);
            for (int i = start; i <= last; i++)
            {
                combo[depth] = i;
                Search(i + 1, depth + 1);
            }
        }

        Search(0, 0);

        var bestNames = bestCombo == null
            ? new List<string>()
            : bestCombo.Select(i => names[i]).ToList();
        var bestResidues = bestCoverage?.ToHashSet() ?? new HashSet<int>();

        return new CombinationResult(
            bestNames,
            bestResidues,
            bestCount < 0 ? 0 : bestCount,
            CoverageFraction(bestResidues, denominator)
        );
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
