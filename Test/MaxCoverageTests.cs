using NUnit.Framework;
using Proteomics;
using Tasks;

namespace Test;

[TestFixture]
[NonParallelizable]
public class MaxCoverageTests
{
    // Sample protein: human hemoglobin beta chain
    private const string SampleSequence = "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";

    private Protein _testProtein = null!;
    private SeekMaximumCoverage _analyzer = null!;
    private string[] _proteaseNames = null!;

    [SetUp]
    public void SetUp()
    {
        _testProtein = new Protein(SampleSequence, "HBB_HUMAN", name: "Hemoglobin subunit beta");

        _analyzer = SeekMaximumCoverage.WithDefaultRules(
            minLength: 6,
            maxLength: 30,
            requireBasicResidue: true
        );

        // Names must exactly match the "Name" column in proteases.tsv
        _proteaseNames = new[]
        {
            "trypsin|P",
            "chymotrypsin|P",
            "Asp-N",
            "Glu-C",
            "Lys-C|P",
            "Arg-C"
        };
    }

    [Test]
    public void TestCalculateCoverageByProtease()
    {
        // Act
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);

        // Assert
        Assert.That(coverage, Is.Not.Null);
        Assert.That(coverage.Count, Is.EqualTo(_proteaseNames.Length));

        // Each protease should have some coverage
        foreach (var proteaseName in _proteaseNames)
        {
            Assert.That(coverage.ContainsKey(proteaseName), Is.True, $"Missing coverage for {proteaseName}");
            Assert.That(coverage[proteaseName].Count, Is.GreaterThan(0), $"No coverage for {proteaseName}");
        }

        // Print individual coverage for debugging
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
        // Arrange
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);

        // Act
        var result = _analyzer.GreedyMinimumProteaseSet(coverage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.SelectedProteases, Is.Not.Empty);
        Assert.That(result.CoveredResidues.Count, Is.GreaterThan(0));
        Assert.That(result.CoverageFraction, Is.GreaterThan(0));

        // Print results
        TestContext.WriteLine("Greedy Minimum Protease Set:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Selected: {string.Join(", ", result.SelectedProteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoveredResidues.Count}/{result.TotalResidues} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestBestPair()
    {
        // Arrange
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);

        // Act
        var result = _analyzer.BestPair(coverage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Proteases.Count, Is.EqualTo(2));
        Assert.That(result.CoverageCount, Is.GreaterThan(0));

        // Print results
        TestContext.WriteLine("Best Pair:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Proteases: {string.Join(" + ", result.Proteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoverageCount}/{_testProtein.Length} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestBestTriplet()
    {
        // Arrange
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);

        // Act
        var result = _analyzer.BestTriplet(coverage);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Proteases.Count, Is.EqualTo(3));
        Assert.That(result.CoverageCount, Is.GreaterThanOrEqualTo(_analyzer.BestPair(coverage).CoverageCount));

        // Print results
        TestContext.WriteLine("Best Triplet:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Proteases: {string.Join(" + ", result.Proteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoverageCount}/{_testProtein.Length} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestRegionRestrictedCoverage()
    {
        // Arrange
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);
        var region = (Start: 50, End: 100);

        // Act
        var result = _analyzer.GreedyMinimumProteaseSet(coverage, region);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalResidues, Is.EqualTo(51)); // 100 - 50 + 1
        Assert.That(result.CoveredResidues.All(i => i >= region.Start && i <= region.End), Is.True);

        // Print results
        TestContext.WriteLine($"Coverage for Region {region.Start}-{region.End}:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Selected: {string.Join(", ", result.SelectedProteases)}");
        TestContext.WriteLine($"  Coverage: {result.CoveredResidues.Count}/{result.TotalResidues} ({result.CoverageFraction:P1})");
    }

    [Test]
    public void TestCoverageFraction()
    {
        // Arrange
        var coverageSet = new HashSet<int> { 0, 1, 2, 3, 4 }; // 5 residues covered
        int regionSize = 10;

        // Act
        double fraction = SeekMaximumCoverage.CoverageFraction(coverageSet, regionSize);

        // Assert
        Assert.That(fraction, Is.EqualTo(0.5));
    }

    [Test]
    public void TestCoverageFractionEdgeCases()
    {
        // Empty coverage
        Assert.That(SeekMaximumCoverage.CoverageFraction(new HashSet<int>(), 100), Is.EqualTo(0.0));

        // Zero region size
        Assert.That(SeekMaximumCoverage.CoverageFraction(new HashSet<int> { 1, 2, 3 }, 0), Is.EqualTo(0.0));

        // Full coverage
        var fullCoverage = new HashSet<int>(Enumerable.Range(0, 10));
        Assert.That(SeekMaximumCoverage.CoverageFraction(fullCoverage, 10), Is.EqualTo(1.0));
    }

    [Test]
    public void TestDetectabilityRules()
    {
        // Test LengthRule
        var lengthRule = new SeekMaximumCoverage.LengthRule(5, 20);
        Assert.That(lengthRule.Description, Does.Contain("5").And.Contain("20"));

        // Test BasicResidueRule
        var basicRule = new SeekMaximumCoverage.BasicResidueRule();
        Assert.That(basicRule.Description, Does.Contain("K").And.Contain("R"));

        // Test CompositeRule
        var compositeRule = new SeekMaximumCoverage.CompositeRule(lengthRule, basicRule);
        Assert.That(compositeRule.Description, Does.Contain("AND"));
    }

    [Test]
    public void TestTripletBetterThanOrEqualToPair()
    {
        // Arrange
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);

        // Act
        var pairResult = _analyzer.BestPair(coverage);
        var tripletResult = _analyzer.BestTriplet(coverage);

        // Assert - triplet should always be >= pair coverage
        Assert.That(tripletResult.CoverageCount, Is.GreaterThanOrEqualTo(pairResult.CoverageCount),
            "Triplet should achieve at least as much coverage as best pair");
    }

    [Test]
    public void TestGreedyVsBruteForce()
    {
        // Arrange
        var coverage = _analyzer.CalculateCoverageByProtease(_testProtein, _proteaseNames);

        // Act
        var greedyResult = _analyzer.GreedyMinimumProteaseSet(coverage);
        var bestTriplet = _analyzer.BestTriplet(coverage);

        // Print comparison
        TestContext.WriteLine("Greedy vs Brute-Force Comparison:");
        TestContext.WriteLine(new string('-', 50));
        TestContext.WriteLine($"  Greedy ({greedyResult.SelectedProteases.Count} proteases): {greedyResult.CoverageFraction:P1}");
        TestContext.WriteLine($"  Best Triplet (3 proteases): {bestTriplet.CoverageFraction:P1}");
    }
}
