using MzLibUtil;
using NUnit.Framework;
using Omics.SequenceConversion;
using PredictionClients.Koina.AbstractClasses;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using PredictionClients.Koina.Util;
using ProteaseGuru.Tasks;
using Proteomics.ProteolyticDigestion;

namespace ProteaseGuru.Test;

[TestFixture]
internal class SpectralLibraryTests
{
    private const string MzLibSequence = "PEPTC[Common Fixed:Carbamidomethyl on C]IDEK";
    private const string UnimodSequence = "PEPTC[UNIMOD:4]IDEK";

    #region Sequence spelling

    [Test]
    public static void ModifiedPeptideSerializesToUnimodForTheWire()
    {
        var peptide = new PeptideWithSetModifications(MzLibSequence);

        Assert.That(peptide.Serialize("Unimod"), Is.EqualTo(UnimodSequence));
    }

    [Test]
    public static void UnmodifiedPeptidesAreUnaffectedByTheSpellingDifference()
    {
        var peptide = new PeptideWithSetModifications("PEPTIDEK");

        Assert.That(peptide.Serialize("Unimod"), Is.EqualTo("PEPTIDEK"));
    }

    [Test]
    public static void GeneratorReadsTheOnlyPredictionSequenceThatParsesBack()
    {
        var prediction = PredictionFor(MzLibSequence, UnimodSequence);

        Assert.DoesNotThrow(() => new PeptideWithSetModifications(prediction.FullSequence));

        var ex = Assert.Throws<MzLibException>(
            () => new PeptideWithSetModifications(prediction.ValidatedFullSequence));
        Assert.That(ex!.Message, Does.Contain("Could not find modification"));
    }

    #endregion

    #region Library generation via mzLib

    [Test]
    public static void ModifiedPeptidesProduceLibrarySpectra()
    {
        var model = SeededModel(FragmentIonMappingMode.MapToInputFullSequence, PredictionFor(MzLibSequence, UnimodSequence));

        var spectra = GenerateFrom(model, retentionTime: 12.3);

        Assert.That(spectra, Has.Count.EqualTo(1));
        Assert.That(spectra[0].Sequence, Is.EqualTo(MzLibSequence));
        Assert.That(spectra[0].MatchedFragmentIons, Has.Count.EqualTo(2));
        Assert.That(spectra[0].RetentionTime, Is.EqualTo(12.3).Within(1e-9));
    }

    [Test]
    public static void TheValidatedMappingModeCannotBuildModifiedPeptides()
    {
        // Why production must ask for MapToInputFullSequence: under the validated mode mzLib builds the
        // peptide from the Unimod spelling, which has no parser.
        var model = SeededModel(FragmentIonMappingMode.MapToValidatedFullSequence, PredictionFor(MzLibSequence, UnimodSequence));

        var ex = Assert.Throws<MzLibException>(() => GenerateFrom(model, retentionTime: 12.3));
        Assert.That(ex!.Message, Does.Contain("Could not find modification"));
    }

    [Test]
    public static void FragmentMassesComeFromTheSubmittedPeptideNotTheValidatedOne()
    {
        // With "Exclude Incompatible Peptides" off, mzLib strips mods Prosit cannot model, so the two
        // spellings describe different molecules. b5 of the phosphopeptide is 592.2014; b5 of the
        // stripped analogue is 512.2351. The library must be built from what the user asked about.
        const string Phospho = "PEPS[Common Biological:Phospho on S]TIDEKAC[Common Fixed:Carbamidomethyl on C]K";
        const string StrippedUnimod = "PEPSTIDEKAC[UNIMOD:4]K";

        var prediction = new PeptideFragmentIntensityPrediction(
            FullSequence: Phospho,
            ValidatedFullSequence: StrippedUnimod,
            PrecursorCharge: 2,
            FragmentAnnotations: new List<string> { "b5+1" },
            FragmentMZs: new List<double> { 592.2014 },
            FragmentIntensities: new List<double> { 1.0 });

        var spectra = GenerateFrom(SeededModel(FragmentIonMappingMode.MapToInputFullSequence, prediction), retentionTime: 5);

        Assert.That(spectra[0].Sequence, Is.EqualTo(Phospho));
        Assert.That(spectra[0].MatchedFragmentIons[0].Mz, Is.EqualTo(592.2014).Within(0.001),
            "the written m/z must match the m/z the filters gated on");
    }

    [Test]
    public static void AMissingRetentionTimeIsWrittenAsNoneRatherThanASentinel()
    {
        var model = SeededModel(FragmentIonMappingMode.MapToInputFullSequence, PredictionFor(MzLibSequence, UnimodSequence));

        var spectra = GenerateFrom(model, retentionTime: null);

        Assert.That(spectra[0].RetentionTime, Is.Null);
    }

    #endregion

    #region Model configuration

    [Test]
    public static void TheConfiguredModelMapsFragmentsOntoTheSubmittedSequence()
    {
        var model = GeneratorWith(PermissiveOptions).CreateModel();

        // The validated spelling is Unimod-encoded and has no parser, so mapping onto it cannot build
        // a peptide at all; see TheValidatedMappingModeCannotBuildModifiedPeptides.
        Assert.That(model.FragmentIonMappingMode, Is.EqualTo(FragmentIonMappingMode.MapToInputFullSequence));
    }

    [Test]
    public static void TheIncompatiblePeptideCheckboxPicksTheModHandlingMode()
    {
        var options = PermissiveOptions;

        options.ExcludeIncompatiblePeptides = true;
        Assert.That(GeneratorWith(options).CreateModel().ModHandlingMode,
            Is.EqualTo(SequenceConversionHandlingMode.ReturnNull));

        options.ExcludeIncompatiblePeptides = false;
        Assert.That(GeneratorWith(options).CreateModel().ModHandlingMode,
            Is.EqualTo(SequenceConversionHandlingMode.RemoveIncompatibleElements));
    }

    [Test]
    public static void AnUnsupportedPredictionModelIsRejected()
    {
        var options = PermissiveOptions;
        options.PredictionModel = "Prosit2020IntensityCID";

        Assert.Throws<NotSupportedException>(() => GeneratorWith(options).CreateModel());
    }

    #endregion

    #region Fragment filters

    [Test]
    public static void FragmentsOutsideTheMzWindowAreDropped()
    {
        var prediction = PredictionFor(MzLibSequence, UnimodSequence);
        var options = PermissiveOptions;
        options.MinimumMZThreshold = 250;

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction });

        // b2 sits at 227.1026, below the window; y2 at 276.1554 survives.
        Assert.That(prediction.FragmentAnnotations, Is.EqualTo(new[] { "y2+1" }));
        Assert.That(prediction.FragmentMZs, Has.Count.EqualTo(1));
        Assert.That(prediction.FragmentIntensities, Has.Count.EqualTo(1));
    }

    [Test]
    public static void FragmentsBelowTheRelativeIntensityThresholdAreDropped()
    {
        var prediction = PredictionFor(MzLibSequence, UnimodSequence);
        var options = PermissiveOptions;
        options.FilterByRelativeIntensity = true;
        options.RelativeIntensityThreshold = 0.75; // base peak is 1.0, so the 0.5 fragment goes

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction });

        Assert.That(prediction.FragmentAnnotations, Is.EqualTo(new[] { "y2+1" }));
        Assert.That(prediction.FragmentIntensities, Is.EqualTo(new[] { 1.0 }));
    }

    [Test]
    public static void RankFilteringKeepsTheMostIntenseAndPreservesOrder()
    {
        var prediction = new PeptideFragmentIntensityPrediction(
            FullSequence: MzLibSequence,
            ValidatedFullSequence: UnimodSequence,
            PrecursorCharge: 2,
            FragmentAnnotations: new List<string> { "b2+1", "y2+1", "b3+1" },
            FragmentMZs: new List<double> { 227.1026, 276.1554, 324.1554 },
            FragmentIntensities: new List<double> { 0.2, 1.0, 0.6 });

        var options = PermissiveOptions;
        options.FilterByIntensityRank = true;
        options.IntensityRankThreshold = 2;

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction });

        Assert.That(prediction.FragmentAnnotations, Is.EqualTo(new[] { "y2+1", "b3+1" }),
            "the two most intense survive, still in m/z order");
        Assert.That(prediction.FragmentMZs, Is.EqualTo(new[] { 276.1554, 324.1554 }));
    }

    [Test]
    public static void PermissiveOptionsDropNothing()
    {
        var prediction = PredictionFor(MzLibSequence, UnimodSequence);

        GeneratorWith(PermissiveOptions).ApplyFragmentFilters(new[] { prediction });

        Assert.That(prediction.FragmentAnnotations, Has.Count.EqualTo(2));
    }

    [Test]
    public static void FilteringAPredictionWithNoFragmentsIsHarmless()
    {
        var prediction = new PeptideFragmentIntensityPrediction(
            FullSequence: MzLibSequence,
            ValidatedFullSequence: UnimodSequence,
            PrecursorCharge: 2,
            FragmentAnnotations: new List<string>(),
            FragmentMZs: new List<double>(),
            FragmentIntensities: new List<double>());

        var options = PermissiveOptions;
        options.FilterByRelativeIntensity = true;
        options.RelativeIntensityThreshold = 0.5;

        Assert.DoesNotThrow(() => GeneratorWith(options).ApplyFragmentFilters(new[] { prediction }));
        Assert.That(prediction.FragmentAnnotations, Is.Empty);
    }

    #endregion

    #region Helpers

    private static List<Omics.SpectrumMatch.LibrarySpectrum> GenerateFrom(SeededHcdModel model, double? retentionTime) =>
        model.GenerateLibrarySpectraFromPredictions(
            alignedRetentionTimes: new[] { retentionTime },
            warning: out _,
            filepath: null,
            minIntensityFilter: 1e-6);

    private static SpectralLibraryGenerator GeneratorWith(SpectralLibraryExportOptions options) =>
        new(new List<SpectralLibraryPeptide>(), options, "unused.msp");

    private static PeptideFragmentIntensityPrediction PredictionFor(string fullSequence, string validatedFullSequence) =>
        new(
            FullSequence: fullSequence,
            ValidatedFullSequence: validatedFullSequence,
            PrecursorCharge: 2,
            FragmentAnnotations: new List<string> { "b2+1", "y2+1" },
            FragmentMZs: new List<double> { 227.1026, 276.1554 },
            FragmentIntensities: new List<double> { 0.5, 1.0 });

    private static SpectralLibraryExportOptions PermissiveOptions => new()
    {
        PredictionModel = "Prosit2020IntensityHCD",
        MinimumMZThreshold = 0,
        MaximumMZThreshold = double.MaxValue,
        FilterByRelativeIntensity = false,
        FilterByIntensityRank = false,
        IntensityRankThreshold = -1,
        OutputFormat = "MSP"
    };

    private static SeededHcdModel SeededModel(FragmentIonMappingMode mode, params PeptideFragmentIntensityPrediction[] predictions) =>
        new(mode, predictions);

    /// <summary>
    /// Stands in for a completed Koina round trip. Predict() populates Predictions and ValidInputsMask
    /// over the network; seeding them directly keeps library generation testable offline.
    /// </summary>
    private sealed class SeededHcdModel : Prosit2020IntensityHCD
    {
        public SeededHcdModel(FragmentIonMappingMode mode, params PeptideFragmentIntensityPrediction[] predictions)
            : base(fragmentIonMappingMode: mode)
        {
            Predictions = predictions.ToList();
            ValidInputsMask = Enumerable.Repeat(true, predictions.Length).ToArray();
        }
    }

    #endregion
}
