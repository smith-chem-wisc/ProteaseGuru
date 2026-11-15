using NUnit.Framework;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Test;

[TestFixture]
[NonParallelizable]
public class MaxCoverageTests
{
    // Sample protein: human hemoglobin beta chain
    private const string SampleSequence = "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";

    private Protein _testProtein = null!;
    private SeekMaximumCoverage _analyzer = null!;
    private List<ProteaseSpecificParameters> _proteaseParams = null!;

    [SetUp]
    public void SetUp()
    {
        _testProtein = new Protein(SampleSequence, "HBB_HUMAN", name: "Hemoglobin subunit beta");

        // No detectability rules needed — length/MC bounds are set directly on DigestionParams,
        // which is exactly how the rest of the codebase (DigestionTask, etc.) configures digestion.
        _analyzer = new SeekMaximumCoverage();

        // Names must exactly match the "Name" column in proteases.tsv
        var proteaseNames = new[]
        {
            "trypsin|P",
            "chymotrypsin|P",
            "Asp-N",
            "Glu-C",
            "Lys-C|P",
            "Arg-C"
        };

        // Build ProteaseSpecificParameters with explicit bounds, consistent with how
        // DigestionConditionsSetupViewModel and DigestionTask construct them.
        _proteaseParams = proteaseNames.Select(name =>
            new ProteaseSpecificParameters(
                new DigestionParams(
                    protease: name,
                    maxMissedCleavages: 2,
                    minPeptideLength: 7,
                    maxPeptideLength: 50)))
            .ToList();
    }

    [Test]
    public void TestCalculateCoverageByProtease()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);

        Assert.That(coverage, Is.Not.Null);
        Assert.That(coverage.Count, Is.EqualTo(_proteaseParams.Count));

        foreach (var psp in _proteaseParams)
        {
            Assert.That(coverage.ContainsKey(psp.DigestionAgentName), Is.True,
                $"Missing coverage for {psp.DigestionAgentName}");
            Assert.That(coverage[psp.DigestionAgentName].Count, Is.GreaterThan(0),
                $"No coverage for {psp.DigestionAgentName}");
        }

        TestContext.WriteLine("Individual Protease Coverage:");
        TestContext.WriteLine(new string('-', 50));
        foreach (var kvp in coverage.OrderByDescending(c => c.Value.Count))
        {
            double fraction = SeekMaximumCoverage.CoverageFraction(kvp.Value, _testProtein.Length);
            TestContext.WriteLine($"  {kvp.Key,-45}: {kvp.Value.Count,3}/{_testProtein.Length} ({fraction:P1})");
        }
    }

    [Test]
    public void TestGreedyMinimumProteaseSet()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var result = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.SelectedProteases, Is.Not.Empty);
        Assert.That(result.CoveredResidues.Count, Is.GreaterThan(0));
        Assert.That(result.CoverageFraction, Is.GreaterThan(0));

        TestContext.WriteLine("Greedy Minimum Protease Set:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Selected: {string.Join(", ", result.SelectedProteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoveredResidues.Count}/{result.TotalResidues} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestBestPair()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var result = _analyzer.BestPair(coverage, _testProtein.Length);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Proteases.Count, Is.EqualTo(2));
        Assert.That(result.CoverageCount, Is.GreaterThan(0));

        TestContext.WriteLine("Best Pair:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Proteases: {string.Join(" + ", result.Proteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoverageCount}/{_testProtein.Length} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestBestTriplet()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var result = _analyzer.BestTriplet(coverage, _testProtein.Length);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Proteases.Count, Is.EqualTo(3));
        Assert.That(result.CoverageCount, Is.GreaterThanOrEqualTo(_analyzer.BestPair(coverage, _testProtein.Length).CoverageCount));

        TestContext.WriteLine("Best Triplet:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Proteases: {string.Join(" + ", result.Proteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoverageCount}/{_testProtein.Length} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestRegionRestrictedCoverage()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var region = (Start: 50, End: 100);
        var result = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length, region);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalResidues, Is.EqualTo(51)); // 100 - 50 + 1
        Assert.That(result.CoveredResidues.All(i => i >= region.Start && i <= region.End), Is.True);

        TestContext.WriteLine($"Coverage for Region {region.Start}-{region.End}:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Selected: {string.Join(", ", result.SelectedProteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoveredResidues.Count}/{result.TotalResidues} ({result.CoverageFraction:P1})");
    }

    /// <summary>
    /// The coverage sets carry no record of residues past the last covered one, so a denominator
    /// derived from them silently drops an uncovered C-terminus from both numerator and
    /// denominator — reporting 100% for a protein that is barely half covered. The tail here is
    /// longer than MaxLength and has no cleavage site, so no protease can reach it.
    /// </summary>
    [Test]
    public void CoverageFractionCountsAnUncoveredCTerminus()
    {
        var tailed = new Protein(SampleSequence + new string('A', 120), "TAILED");
        var coverage = _analyzer.CalculateCoverageByProtease(tailed, _proteaseParams);

        int highestCovered = coverage.Values.SelectMany(s => s).Max();
        Assert.That(highestCovered, Is.LessThan(tailed.Length - 1),
            "test needs a protein whose C-terminus no protease covers");

        var result = _analyzer.BestPair(coverage, tailed.Length);

        Assert.That(result.CoverageFraction,
            Is.EqualTo((double)result.CoveredResidues.Count / tailed.Length).Within(1e-9));
        Assert.That(result.CoverageFraction, Is.LessThan(0.75),
            "an uncovered tail this long must drag the reported fraction well below 100%");
    }

    [Test]
    public void GreedyCoverageFractionIsReportedAgainstTheGivenTotal()
    {
        var tailed = new Protein(SampleSequence + new string('A', 120), "TAILED");
        var coverage = _analyzer.CalculateCoverageByProtease(tailed, _proteaseParams);

        var result = _analyzer.GreedyMinimumProteaseSet(coverage, tailed.Length);

        Assert.That(result.TotalResidues, Is.EqualTo(tailed.Length));
        Assert.That(result.CoverageFraction,
            Is.EqualTo((double)result.CoveredResidues.Count / tailed.Length).Within(1e-9));
    }

    [Test]
    public void RegionSpanStillWinsOverTheSuppliedTotal()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);

        var result = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length, (Start: 50, End: 100));

        Assert.That(result.TotalResidues, Is.EqualTo(51));
    }

    [Test]
    public void NegativeTotalResiduesIsRejected()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _analyzer.GreedyMinimumProteaseSet(coverage, -1));
    }

    [Test]
    public void TestDigestSingleParallelMatchesSerial()
    {
        // The analyzer window digests cache-miss proteases concurrently for the same
        // protein instance. Verify that path produces the same per-protease coverage and
        // intervals as a serial digest, and that repeated parallel runs stay consistent.
        var serial = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var serialIntervals = _analyzer.GetDetectablePeptideIntervals(_testProtein, _proteaseParams);

        for (int iteration = 0; iteration < 25; iteration++)
        {
            var results = new (HashSet<int> Coverage, List<(int Start, int End)> Intervals)[_proteaseParams.Count];
            Parallel.For(0, _proteaseParams.Count,
                i => results[i] = _analyzer.DigestSingle(_testProtein, _proteaseParams[i]));

            for (int i = 0; i < _proteaseParams.Count; i++)
            {
                string name = _proteaseParams[i].DigestionAgentName;
                Assert.That(results[i].Coverage, Is.EquivalentTo(serial[name]),
                    $"Parallel coverage diverged for {name} on iteration {iteration}");
                Assert.That(results[i].Intervals, Is.EqualTo(serialIntervals[name]),
                    $"Parallel intervals diverged for {name} on iteration {iteration}");
            }
        }
    }

    [Test]
    public void TestCoverageFraction()
    {
        var coverageSet = new HashSet<int> { 0, 1, 2, 3, 4 };
        double fraction = SeekMaximumCoverage.CoverageFraction(coverageSet, 10);
        Assert.That(fraction, Is.EqualTo(0.5));
    }

    [Test]
    public void TestCoverageFractionEdgeCases()
    {
        Assert.That(SeekMaximumCoverage.CoverageFraction(new HashSet<int>(), 100), Is.EqualTo(0.0));
        Assert.That(SeekMaximumCoverage.CoverageFraction(new HashSet<int> { 1, 2, 3 }, 0), Is.EqualTo(0.0));

        var fullCoverage = new HashSet<int>(Enumerable.Range(0, 10));
        Assert.That(SeekMaximumCoverage.CoverageFraction(fullCoverage, 10), Is.EqualTo(1.0));
    }

    [Test]
    public void TestMassBoundsFilter()
    {
        // Verify that RunParameters mass bounds are respected.
        // A very tight mass window should reduce coverage relative to no filter.
        var runParamsWithFilter = new RunParameters
        {
            MinPeptideMassAllowed = 1000,
            MaxPeptideMassAllowed = 2000
        };
        var analyzerWithFilter = new SeekMaximumCoverage(runParamsWithFilter);

        var unfilteredCoverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var filteredCoverage = analyzerWithFilter.CalculateCoverageByProtease(_testProtein, _proteaseParams);

        foreach (var psp in _proteaseParams)
        {
            Assert.That(
                filteredCoverage[psp.DigestionAgentName].Count,
                Is.LessThanOrEqualTo(unfilteredCoverage[psp.DigestionAgentName].Count),
                $"Filtered coverage should be <= unfiltered for {psp.DigestionAgentName}");
        }
    }

    /// <summary>
    /// DigestSingle is the primitive every coverage number and every coverage-map bar comes from,
    /// and the assertions around it were all relational — a filter that stopped applying, or a
    /// peptide's last residue going missing, satisfied every one of them. These pin the three
    /// pieces of its output against values worked out from the digest itself.
    /// </summary>
    [Test]
    public void DigestSingleCoversEveryResidueOfEveryPeptide()
    {
        var trypsin = _proteaseParams.First(p => p.DigestionAgentName == "trypsin|P");
        var (coverage, intervals) = _analyzer.DigestSingle(_testProtein, trypsin);

        Assert.That(intervals, Is.Not.Empty, "fixture digested to nothing");

        var expected = new HashSet<int>();
        foreach (var (start, end) in intervals)
            for (int i = start - 1; i <= end - 1; i++)
                expected.Add(i);

        Assert.That(coverage, Is.EquivalentTo(expected));

        // Stated separately: an off-by-one at either end of the loop leaves the two sets agreeing
        // with each other, so compare against the spans as well.
        Assert.That(coverage.Min(), Is.EqualTo(intervals.Min(t => t.Start) - 1));
        Assert.That(coverage.Max(), Is.EqualTo(intervals.Max(t => t.End) - 1));
    }

    [Test]
    public void DigestSingleIntervalsAreDeduplicatedAndSortedByStart()
    {
        foreach (var proteaseParam in _proteaseParams)
        {
            var (_, intervals) = _analyzer.DigestSingle(_testProtein, proteaseParam);
            string name = proteaseParam.DigestionAgentName;

            Assert.That(intervals, Is.Unique, $"duplicate spans for {name}");
            Assert.That(intervals.Select(t => t.Start), Is.Ordered, $"spans out of order for {name}");
        }
    }

    /// <summary>
    /// A mass window this tight has to remove peptides, not merely fail to add any. Asserting only
    /// "filtered &lt;= unfiltered" is satisfied by a filter that never runs at all.
    /// </summary>
    [Test]
    public void MassFilterStrictlyNarrowsAtLeastOneProtease()
    {
        var filtered = new SeekMaximumCoverage(new RunParameters
        {
            MinPeptideMassAllowed = 1000,
            MaxPeptideMassAllowed = 2000
        });

        var trypsin = _proteaseParams.First(p => p.DigestionAgentName == "trypsin|P");
        var unfilteredIntervals = _analyzer.DigestSingle(_testProtein, trypsin).Intervals;
        var filteredIntervals = filtered.DigestSingle(_testProtein, trypsin).Intervals;

        Assert.That(filteredIntervals.Count, Is.LessThan(unfilteredIntervals.Count),
            "the mass window removed nothing, so the filter is not being applied");
        Assert.That(filteredIntervals, Is.SubsetOf(unfilteredIntervals),
            "filtering introduced a span the unfiltered digest never produced");
    }

    [Test]
    public void TestTripletBetterThanOrEqualToPair()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var pairResult = _analyzer.BestPair(coverage, _testProtein.Length);
        var tripletResult = _analyzer.BestTriplet(coverage, _testProtein.Length);

        Assert.That(tripletResult.CoverageCount, Is.GreaterThanOrEqualTo(pairResult.CoverageCount),
            "Triplet should achieve at least as much coverage as best pair");
    }

    [Test]
    public void TestGreedyVsBruteForce()
    {
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseParams);
        var greedyResult = _analyzer.GreedyMinimumProteaseSet(coverage, _testProtein.Length);
        var bestTriplet = _analyzer.BestTriplet(coverage, _testProtein.Length);

        TestContext.WriteLine("Greedy vs Brute-Force Comparison:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Greedy ({greedyResult.SelectedProteases.Count} proteases): {greedyResult.CoverageFraction:P1}");
        TestContext.WriteLine($"  Best Triplet (3 proteases): {bestTriplet.CoverageFraction:P1}");
    }
}
