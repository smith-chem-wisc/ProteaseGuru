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
