using NUnit.Framework;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Test;

/// <summary>
/// Pins the bitset set-cover rewrite against the HashSet implementation it replaced. The reference
/// methods below are that implementation, ported unchanged, so a divergence in the production
/// algorithm shows up here as a mismatch.
///
/// These assert the selected proteases and the whole covered-residue set rather than counts: a
/// count-only check still passes when the two disagree about which residues are covered, and those
/// residues drive the coverage percentage the analyzer puts on screen.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SeekMaximumCoverageEquivalenceTests
{
    private const string SampleSequence = "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";

    private Protein _testProtein = null!;
    private SeekMaximumCoverage _analyzer = null!;
    private List<ProteaseSpecificParameters> _proteaseParams = null!;

    [SetUp]
    public void SetUp()
    {
        _testProtein = new Protein(SampleSequence, "HBB_HUMAN", name: "Hemoglobin subunit beta");
        _analyzer = new SeekMaximumCoverage();
        _proteaseParams = new[] { "trypsin|P", "chymotrypsin|P", "Asp-N", "Glu-C", "Lys-C|P", "Arg-C" }
            .Select(name => new ProteaseSpecificParameters(
                new DigestionParams(protease: name, maxMissedCleavages: 2,
                    minPeptideLength: 7, maxPeptideLength: 50)))
            .ToList();
    }

    private Dictionary<string, HashSet<int>> RealCoverage() =>
        _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);

    /// <summary>
    /// Bits are packed 64 to a word, so sets that stop or start either side of a word boundary are
    /// where an off-by-one in the packing shows up. 63/64 is the first boundary, 127/128 the second,
    /// and the last set runs to the final residue so a dropped top word is visible.
    /// </summary>
    private static Dictionary<string, HashSet<int>> WordBoundaryCoverage(int length) => new()
    {
        ["endsBeforeBoundary"] = new HashSet<int>(Enumerable.Range(0, 64)),
        ["startsOnBoundary"] = new HashSet<int>(Enumerable.Range(64, 64)),
        ["straddlesBoundary"] = new HashSet<int> { 63, 64, 127, 128 },
        ["runsToTheEnd"] = new HashSet<int>(Enumerable.Range(120, length - 120)),
    };

    [Test]
    public void GreedyMatchesTheHashSetImplementation()
    {
        var coverage = RealCoverage();

        var actual = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length);
        var (expectedProteases, expectedResidues) = ReferenceGreedy(coverage, null);

        Assert.That(actual.SelectedProteases, Is.EqualTo(expectedProteases));
        Assert.That(actual.CoveredResidues, Is.EquivalentTo(expectedResidues));
    }

    [Test]
    public void GreedyMatchesOverAWordBoundaryLayout()
    {
        var coverage = WordBoundaryCoverage(_testProtein.Length);

        var actual = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length);
        var (expectedProteases, expectedResidues) = ReferenceGreedy(coverage, null);

        Assert.That(actual.SelectedProteases, Is.EqualTo(expectedProteases));
        Assert.That(actual.CoveredResidues, Is.EquivalentTo(expectedResidues));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void BestCombinationMatchesTheHashSetImplementation(int combinationSize)
    {
        var coverage = RealCoverage();

        var actual = _analyzer.BestCombination(coverage, combinationSize, _testProtein.Length);
        var (expectedProteases, expectedResidues) = ReferenceBestCombination(coverage, combinationSize, null);

        Assert.That(actual.Proteases, Is.EqualTo(expectedProteases));
        Assert.That(actual.CoveredResidues, Is.EquivalentTo(expectedResidues));
        Assert.That(actual.CoverageCount, Is.EqualTo(expectedResidues.Count),
            "the scored count and the covered set disagree");
    }

    [TestCase(2)]
    [TestCase(3)]
    public void BestCombinationMatchesOverAWordBoundaryLayout(int combinationSize)
    {
        var coverage = WordBoundaryCoverage(_testProtein.Length);

        var actual = _analyzer.BestCombination(coverage, combinationSize, _testProtein.Length);
        var (expectedProteases, expectedResidues) = ReferenceBestCombination(coverage, combinationSize, null);

        Assert.That(actual.Proteases, Is.EqualTo(expectedProteases));
        Assert.That(actual.CoveredResidues, Is.EquivalentTo(expectedResidues));
        Assert.That(actual.CoverageCount, Is.EqualTo(expectedResidues.Count),
            "the scored count and the covered set disagree");
    }

    /// <summary>
    /// The region path had no old-vs-new check anywhere. Bounds that fall inside a word exercise
    /// the filtering the bitset build does while setting bits.
    /// </summary>
    [TestCase(50, 100)]
    [TestCase(0, 63)]
    [TestCase(64, 127)]
    [TestCase(60, 70)]
    public void RegionRestrictedResultsMatchTheHashSetImplementation(int start, int end)
    {
        var coverage = RealCoverage();
        var region = (Start: start, End: end);

        var greedy = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length, region);
        var (expectedGreedyProteases, expectedGreedyResidues) = ReferenceGreedy(coverage, region);
        Assert.That(greedy.SelectedProteases, Is.EqualTo(expectedGreedyProteases));
        Assert.That(greedy.CoveredResidues, Is.EquivalentTo(expectedGreedyResidues));

        var pair = _analyzer.BestCombination(coverage, 2, _testProtein.Length, region);
        var (expectedPairProteases, expectedPairResidues) = ReferenceBestCombination(coverage, 2, region);
        Assert.That(pair.Proteases, Is.EqualTo(expectedPairProteases));
        Assert.That(pair.CoveredResidues, Is.EquivalentTo(expectedPairResidues));
        Assert.That(pair.CoverageCount, Is.EqualTo(expectedPairResidues.Count),
            "the scored count and the covered set disagree");
    }

    #region Reference implementation (the pre-bitset code, unchanged)

    private static Dictionary<string, HashSet<int>> FilterToRegion(
        Dictionary<string, HashSet<int>> coverageDict, (int Start, int End)? region)
    {
        if (!region.HasValue)
            return coverageDict;

        int start = region.Value.Start;
        int end = region.Value.End;

        return coverageDict.ToDictionary(
            kvp => kvp.Key,
            kvp => new HashSet<int>(kvp.Value.Where(i => i >= start && i <= end)));
    }

    private static (List<string> Proteases, HashSet<int> Residues) ReferenceGreedy(
        Dictionary<string, HashSet<int>> coverageDict, (int Start, int End)? region)
    {
        var workingCoverage = FilterToRegion(coverageDict, region);

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

        return (selectedProteases, totalCovered);
    }

    private static (List<string> Proteases, HashSet<int> Residues) ReferenceBestCombination(
        Dictionary<string, HashSet<int>> coverageDict, int combinationSize, (int Start, int End)? region)
    {
        var workingCoverage = FilterToRegion(coverageDict, region);
        var proteaseNames = workingCoverage.Keys.ToList();

        if (proteaseNames.Count < combinationSize)
        {
            var allCovered = new HashSet<int>();
            foreach (var coverage in workingCoverage.Values)
                allCovered.UnionWith(coverage);
            return (proteaseNames, allCovered);
        }

        List<string>? bestCombination = null;
        HashSet<int>? bestCoverage = null;
        int bestCoverageCount = -1;

        foreach (var combination in Combinations(proteaseNames, combinationSize))
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

        return (bestCombination ?? new List<string>(), bestCoverage ?? new HashSet<int>());
    }

    private static IEnumerable<IEnumerable<T>> Combinations<T>(List<T> list, int length)
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

            foreach (var combination in Combinations(tail, length - 1))
                yield return new[] { head }.Concat(combination);
        }
    }

    #endregion
}
