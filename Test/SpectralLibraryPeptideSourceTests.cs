using NUnit.Framework;
using Omics;
using Omics.SequenceConversion;
using ProteaseGuru.GuiFunctions;
using ProteaseGuru.Tasks;
using ProteaseGuru.Tasks.CoverageMapConfiguration;
using Proteomics;
using Proteomics.ProteolyticDigestion;

namespace ProteaseGuru.Test;

[TestFixture]
[NonParallelizable] // The on-demand source predicts retention times through the shared Chronologer model.
internal class SpectralLibraryPeptideSourceTests
{
    private const string Sequence = "MSFVNGNEIFTAARKQGHYAVGAFNTNNLEWTRKPEPTIDESAMPLERKNTPVLIQVSMGAAKYLVKTLVEEEMR";

    private static Protein TestProtein => new(Sequence, "TESTPROT");

    private static ProteaseSpecificParameters TrypsinParameters => new(
        new DigestionParams(protease: "trypsin|P", maxMissedCleavages: 0, minPeptideLength: 7, maxPeptideLength: 30));

    private static SpectralLibraryExportOptions OptionsFor(IEnumerable<string> proteases, IEnumerable<string> proteins) =>
        new()
        {
            SelectedProteases = proteases.ToList(),
            SelectedProteins = proteins.ToList(),
            PredictionModel = FragmentIntensityPredictionModel.Prosit2020IntensityHcd,
            OutputFormat = SpectralLibraryFormat.Msp
        };

    private static InSilicoPep PeptideWith(
        string fullSequence,
        double? retentionTime,
        bool? detectable,
        int start = 1,
        double? notDetectableProbability = null) =>
        new(fullSequence, fullSequence, 'K', 'A', unique: true, hydrophobicity: 0, electrophoreticMobility: 0,
            chronologerRetentionTime: retentionTime, pflyDetectability: detectable, length: fullSequence.Length,
            molecularWeight: 0, database: "db", protein: "TESTPROT", proteinName: "TESTPROT",
            start: start, end: start + fullSequence.Length - 1, protease: "trypsin|P",
            pflyProbabilities: notDetectableProbability.HasValue
                ? (notDetectableProbability.Value, 0, 0, 1.0 - notDetectableProbability.Value)
                : null);

    private static ResultsBackedPeptideSource SourceOver(params InSilicoPep[] peptides)
    {
        var protein = TestProtein;
        var byFile = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>
        {
            ["db"] = new()
            {
                ["trypsin|P"] = new() { [protein] = peptides.ToList() }
            }
        };
        var coverage = new Dictionary<string, Dictionary<IBioPolymer, (double, double)>>();
        return new ResultsBackedPeptideSource(new ProteinCoverageAnalyzer(byFile, coverage));
    }

    #region Digestion condition modifications

    [Test]
    public static void UntickingCarbamidomethylationRemovesIt()
    {
        var conditions = new DigestionConditionsSetupViewModel(null);
        var proteinProteases = conditions.ProteaseSpecificParameters
            .Where(p => p is { IsRna: false, IsVisible: true })
            .ToList();
        Assert.That(proteinProteases, Is.Not.Empty, "expected at least one visible protein protease");

        conditions.ApplyFixedCarbamidomethylation = true;
        Assert.That(proteinProteases.All(p => p.ProteaseSpecificParams.FixedMods.Any(m => m.IdWithMotif.StartsWith("Carbamidomethyl"))), Is.True);

        conditions.ApplyFixedCarbamidomethylation = false;

        Assert.That(proteinProteases.Any(p => p.ProteaseSpecificParams.FixedMods.Any(m => m.IdWithMotif.StartsWith("Carbamidomethyl"))), Is.False);
    }

    #endregion

    #region Results-backed source

    [Test]
    public static void RunResultsCarryTheirRetentionTimeThrough()
    {
        var source = SourceOver(PeptideWith("PEPTIDEK", 42.5, detectable: true));

        var peptides = source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins));

        Assert.That(peptides, Has.Count.EqualTo(1));
        Assert.That(peptides[0].RetentionTime, Is.EqualTo(42.5).Within(1e-9));
    }

    [Test]
    public static void AMissingRetentionTimeStaysMissing()
    {
        var source = SourceOver(PeptideWith("PEPTIDEK", null, detectable: true));

        var peptides = source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins));

        Assert.That(peptides[0].RetentionTime, Is.Null);
    }

    [Test]
    public static void AMissingRetentionTimeSurvivesTheResultsFileRoundTrip()
    {
        // Column 17 is what the previous-results loader reads. A missing value has to come back
        // missing, and has to stay parseable by the Convert.ToDouble the loader uses.
        var written = PeptideWith("PEPTIDEK", null, detectable: true).ToString().Split('	')[17];

        Assert.That(InSilicoPep.RetentionTimeFromStoredValue(Convert.ToDouble(written)), Is.Null);
    }

    [Test]
    public static void ARealRetentionTimeSurvivesTheResultsFileRoundTrip()
    {
        var written = PeptideWith("PEPTIDEK", -0.464, detectable: true).ToString().Split('	')[17];

        Assert.That(InSilicoPep.RetentionTimeFromStoredValue(Convert.ToDouble(written)),
            Is.EqualTo(-0.464).Within(1e-9));
    }

    [TestCase(-1.0, null, TestName = "StoredMinusOneMeantChronologerFailed")]
    [TestCase(double.NaN, null, TestName = "StoredNaNMeantNeverCalculated")]
    [TestCase(-0.464, -0.464, TestName = "StoredNegativeIsARealPrediction")]
    [TestCase(42.5, 42.5, TestName = "StoredPositiveIsARealPrediction")]
    public static void LegacyStoredMarkersAreReadBackAsMissing(double stored, double? expected)
    {
        // Results files written before this column was nullable used both -1 and NaN for "no value".
        Assert.That(InSilicoPep.RetentionTimeFromStoredValue(stored), Is.EqualTo(expected));
    }

    [Test]
    public static void ANegativeRetentionTimeIsCarriedThrough()
    {
        // Chronologer predicts below zero for hydrophilic peptides.
        var source = SourceOver(PeptideWith("PEPTIDEK", -0.464, detectable: true));

        var peptides = source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins));

        Assert.That(peptides[0].RetentionTime, Is.EqualTo(-0.464).Within(1e-9));
    }

    [Test]
    public static void TheSameSequenceAtTwoPositionsCollapsesToOnePeptide()
    {
        // InSilicoPep.Equals keys on start and end, so these stay distinct peptides all the way to the
        // final DistinctBy -- which is the case that guard exists for.
        var source = SourceOver(
            PeptideWith("PEPTIDEK", 10, detectable: true, start: 1),
            PeptideWith("PEPTIDEK", 10, detectable: true, start: 40));

        var peptides = source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins));

        Assert.That(peptides, Has.Count.EqualTo(1));
    }

    [Test]
    public static void UndetectablePeptidesAreDroppedOnlyWhenTheFilterIsOn()
    {
        var source = SourceOver(
            PeptideWith("PEPTIDEK", 10, detectable: true),
            PeptideWith("SAMPLERK", 20, detectable: false));

        var options = OptionsFor(source.AvailableProteases, source.AvailableProteins);
        Assert.That(source.GetPeptides(options), Has.Count.EqualTo(2));

        options.ExcludeUndetectablePeptides = true;
        var filtered = source.GetPeptides(options);

        Assert.That(filtered, Has.Count.EqualTo(1));
        Assert.That(filtered[0].FullSequence, Is.EqualTo("PEPTIDEK"));
    }

    [Test]
    public static void ResultsBackedFilteringUsesTheExportThresholdAndStoredProbabilities()
    {
        var source = SourceOver(
            PeptideWith("PEPTIDEK", 10, detectable: false, notDetectableProbability: 0.2),
            PeptideWith("SAMPLERK", 20, detectable: true, notDetectableProbability: 0.4));
        var options = OptionsFor(source.AvailableProteases, source.AvailableProteins);
        options.ExcludeUndetectablePeptides = true;
        options.DetectabilityThreshold = 0.7;

        var filtered = source.GetPeptides(options);

        Assert.That(filtered.Select(p => p.FullSequence), Is.EqualTo(new[] { "PEPTIDEK" }),
            "stored probabilities should be evaluated against the threshold chosen for this export");
    }

    [Test]
    public static void LegacyResultsWithoutProbabilitiesFallBackToStoredDetectability()
    {
        var source = SourceOver(
            PeptideWith("PEPTIDEK", 10, detectable: true),
            PeptideWith("SAMPLERK", 20, detectable: false));
        var options = OptionsFor(source.AvailableProteases, source.AvailableProteins);
        options.ExcludeUndetectablePeptides = true;
        options.DetectabilityThreshold = 0.9;

        var filtered = source.GetPeptides(options);

        Assert.That(filtered.Select(p => p.FullSequence), Is.EqualTo(new[] { "PEPTIDEK" }));
    }

    [Test]
    public static void GatheringFromRunResultsStopsWhenCancelled()
    {
        var source = SourceOver(PeptideWith("PEPTIDEK", 10, detectable: true));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins), null, cts.Token));
    }

    [Test]
    public static void UnselectedProteinsAndProteasesContributeNothing()
    {
        var source = SourceOver(PeptideWith("PEPTIDEK", 10, detectable: true));

        Assert.That(source.GetPeptides(OptionsFor(source.AvailableProteases, new[] { "OTHERPROT" })), Is.Empty);
        Assert.That(source.GetPeptides(OptionsFor(new[] { "chymotrypsin|P" }, source.AvailableProteins)), Is.Empty);
    }

    #endregion

    #region On-demand source

    [Test]
    public static void DetectabilityModelStripsModificationsItCannotRepresent()
    {
        // PFly's converter allows no modifications, so rejecting instead of stripping would leave
        // every modified peptide unassessed -- and unassessed is dropped as undetectable. One factory
        // serves the digestion run and the export, so this pins both.
        Assert.That(DetectabilityModel.Create().ModHandlingMode,
            Is.EqualTo(SequenceConversionHandlingMode.RemoveIncompatibleElements));
    }

    [Test]
    public static void GatheringStopsBeforeTheDetectabilityRequest()
    {
        // No protein selected, so the digest yields nothing and the cancellation check before the
        // network call is the only thing that can throw.
        var source = new OnDemandDigestPeptideSource(new[] { TestProtein }, new[] { TrypsinParameters });
        var options = OptionsFor(source.AvailableProteases, Array.Empty<string>());
        options.ExcludeUndetectablePeptides = true;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => source.GetPeptides(options, null, cts.Token));
    }

    [TestCase(0.10, 0.5, false, TestName = "BelowThresholdIsNotDetectable")]
    [TestCase(0.50, 0.5, true, TestName = "AtThresholdIsDetectable")]
    [TestCase(0.90, 0.5, true, TestName = "AboveThresholdIsDetectable")]
    [TestCase(0.90, 0.95, false, TestName = "AHigherThresholdExcludesMore")]
    public static void DetectabilityIsJudgedAgainstTheThreshold(double detectable, double threshold, bool expected)
    {
        // PFly reports the probability a peptide is NOT detectable.
        Assert.That(
            OnDemandDigestPeptideSource.IsDetectable((1.0 - detectable, 0, 0, 0), threshold),
            Is.EqualTo(expected));
    }

    [Test]
    public static void APeptidePFlyCouldNotAssessIsNotDetectable()
    {
        Assert.That(OnDemandDigestPeptideSource.IsDetectable(null, 0.5), Is.False);
    }

    [Test]
    public static void GatheringStopsWhenCancelled()
    {
        var source = new OnDemandDigestPeptideSource(new[] { TestProtein }, new[] { TrypsinParameters });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins), null, cts.Token));
    }

    [Test]
    public static void OnDemandDigestionYieldsPeptidesWithoutRetentionTimes()
    {
        var source = new OnDemandDigestPeptideSource(new[] { TestProtein }, new[] { TrypsinParameters });

        var peptides = source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins));

        Assert.That(peptides, Is.Not.Empty);
        Assert.That(peptides.Select(p => p.RetentionTime), Is.All.Null);
    }

    [Test]
    public static void OnDemandDigestionDeduplicatesAcrossProteases()
    {
        var source = new OnDemandDigestPeptideSource(
            new[] { TestProtein },
            new[] { TrypsinParameters, TrypsinParameters });

        var peptides = source.GetPeptides(OptionsFor(source.AvailableProteases, source.AvailableProteins));

        Assert.That(peptides.Select(p => p.FullSequence), Is.Unique);
    }

    [Test]
    public static void OnDemandSourceOffersItsOwnProteasesAndProteins()
    {
        var source = new OnDemandDigestPeptideSource(new[] { TestProtein }, new[] { TrypsinParameters });

        Assert.That(source.AvailableProteases, Is.EqualTo(new[] { "trypsin|P" }));
        Assert.That(source.AvailableProteins, Is.EqualTo(new[] { "TESTPROT" }));
    }

    #endregion

    #region Retention time resolution

    [Test]
    public static void RetentionTimesArePredictedOnlyForPeptidesLackingThem()
    {
                var peptides = new List<SpectralLibraryPeptide>
        {
            new("PEPTIDEK", RetentionTime: 42.5),
            new("ELVISLIVESK", RetentionTime: null)
        };

        var resolved = SpectralLibraryGenerator.ResolveRetentionTimes(peptides);

        Assert.That(resolved["PEPTIDEK"], Is.EqualTo(42.5).Within(1e-9), "an existing retention time must not be re-predicted");
        Assert.That(resolved["ELVISLIVESK"], Is.Not.Null, "a missing retention time must be predicted");
    }

    [Test]
    public static void ResolvingRetentionTimesTouchesNoModelWhenNoneAreMissing()
    {
                var peptides = new List<SpectralLibraryPeptide> { new("PEPTIDEK", RetentionTime: 42.5) };

        var resolved = SpectralLibraryGenerator.ResolveRetentionTimes(peptides);

        Assert.That(resolved["PEPTIDEK"], Is.EqualTo(42.5).Within(1e-9));
        Assert.That(SharedChronologerPredictor.IsModelLoaded, Is.False);
    }

    #endregion
}
