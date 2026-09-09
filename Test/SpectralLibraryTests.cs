using MzLibUtil;
using Omics.Fragmentation;
using Omics.SpectrumMatch;
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
        options.PredictionModel = (FragmentIntensityPredictionModel)999;

        Assert.Throws<NotSupportedException>(() => GeneratorWith(options).CreateModel());
    }

    #endregion

    #region Output formats

    [Test]
    public static void EveryFormatHasAMatchingFilterAndExtension()
    {
        foreach (SpectralLibraryFormat format in Enum.GetValues<SpectralLibraryFormat>())
        {
            // The save dialog's filter and its default extension have to agree, or the file lands with
            // an extension mzLib routes somewhere else.
            Assert.That(format.FileFilter(), Does.Contain("*" + format.Extension()),
                $"{format}'s filter and extension disagree");
        }
    }

    [Test]
    public static void AnUnsupportedFormatIsRejectedRatherThanSilentlyIgnored()
    {
        var bogus = (SpectralLibraryFormat)999;

        Assert.Throws<NotSupportedException>(() => bogus.Extension());
        Assert.Throws<NotSupportedException>(() => bogus.FileFilter());
    }

    [Test]
    public static void ProgressIsReportedAtEachStage()
    {
        var reported = new List<string>();
        var options = PermissiveOptions;
        options.ChargeStates = new List<int>();
        var generator = new SpectralLibraryGenerator(new List<SpectralLibraryPeptide>(), options, "unused.msp");

        generator.GenerateLibrary(new Progress<string>(reported.Add));

        // Progress<T> posts asynchronously, so drain the queue before asserting.
        SpinWait.SpinUntil(() => reported.Count >= 4, TimeSpan.FromSeconds(5));
        Assert.That(reported, Has.Count.GreaterThanOrEqualTo(4));
    }

    [Test]
    public static void CancellationIsHonouredBeforeAnyPredictionStarts()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var generator = new SpectralLibraryGenerator(new List<SpectralLibraryPeptide>(), PermissiveOptions, "unused.msp");

        Assert.Throws<OperationCanceledException>(() => generator.GenerateLibrary(null, cancelled.Token));
    }

    #endregion

    #region Fragment filters

    [Test]
    public static void FragmentsOutsideTheMzWindowAreDropped()
    {
        var prediction = PredictionFor(MzLibSequence, UnimodSequence);
        var options = PermissiveOptions;
        options.MinimumMZThreshold = 250;

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction }, AllValid(1));

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

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction }, AllValid(1));

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

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction }, AllValid(1));

        Assert.That(prediction.FragmentAnnotations, Is.EqualTo(new[] { "y2+1", "b3+1" }),
            "the two most intense survive, still in m/z order");
        Assert.That(prediction.FragmentMZs, Is.EqualTo(new[] { 276.1554, 324.1554 }));
    }

    [Test]
    public static void PermissiveOptionsDropNothing()
    {
        var prediction = PredictionFor(MzLibSequence, UnimodSequence);

        GeneratorWith(PermissiveOptions).ApplyFragmentFilters(new[] { prediction }, AllValid(1));

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

        Assert.DoesNotThrow(() => GeneratorWith(options).ApplyFragmentFilters(new[] { prediction }, AllValid(1)));
        Assert.That(prediction.FragmentAnnotations, Is.Empty);
    }

    #endregion

    #region Alignment and rejected inputs

    [Test]
    public static void RejectingEveryPeptideFailsLoudlyInsteadOfWritingAnEmptyLibrary()
    {
        var model = new SeededHcdModel(FragmentIonMappingMode.MapToInputFullSequence, new[] { false },
            PredictionFor(MzLibSequence, UnimodSequence));

        var ex = Assert.Throws<InvalidOperationException>(
            () => SpectralLibraryGenerator.ReportRejectedInputs(model, null));

        // The limits come off the model, so the message stays true if the model changes.
        Assert.That(ex!.Message, Does.Contain("1-30").And.Contain("UNIMOD"));
    }

    [Test]
    public static void RejectingSomePeptidesIsReportedButNotFatal()
    {
        var reported = new List<string>();
        var model = new SeededHcdModel(FragmentIonMappingMode.MapToInputFullSequence, new[] { true, false },
            PredictionFor(MzLibSequence, UnimodSequence), PredictionFor(MzLibSequence, UnimodSequence));

        Assert.DoesNotThrow(() => SpectralLibraryGenerator.ReportRejectedInputs(model, new Progress<string>(reported.Add)));

        SpinWait.SpinUntil(() => reported.Count > 0, TimeSpan.FromSeconds(5));
        Assert.That(reported.Single(), Does.Contain("1 of 2"));
    }

    [Test]
    public static void AcceptingEveryPeptideReportsNothing()
    {
        var reported = new List<string>();
        var model = SeededModel(FragmentIonMappingMode.MapToInputFullSequence, PredictionFor(MzLibSequence, UnimodSequence));

        SpectralLibraryGenerator.ReportRejectedInputs(model, new Progress<string>(reported.Add));

        Assert.That(reported, Is.Empty);
    }

    [Test]
    public static void PlaceholderPredictionsForRejectedInputsAreSkipped()
    {
        // mzLib realigns Predictions to the full input length, inserting entries whose three fragment
        // lists are all null for inputs it rejected -- over 30 residues, non-canonical, unsupported
        // mods. Walking into one throws and kills the whole export.
        var rejected = new PeptideFragmentIntensityPrediction(
            FullSequence: "PEPTIDEK",
            ValidatedFullSequence: null,
            PrecursorCharge: 2,
            FragmentAnnotations: null,
            FragmentMZs: null,
            FragmentIntensities: null);
        var accepted = PredictionFor(MzLibSequence, UnimodSequence);

        Assert.DoesNotThrow(() => GeneratorWith(PermissiveOptions)
            .ApplyFragmentFilters(new[] { rejected, accepted }, new[] { false, true }));

        Assert.That(accepted.FragmentAnnotations, Has.Count.EqualTo(2), "valid predictions are still filtered");
    }

    [Test]
    public static void RetentionTimesStayInLockstepWithPredictionInputs()
    {
        var options = PermissiveOptions;
        options.ChargeStates = new List<int> { 2, 3 };
        var peptides = new List<SpectralLibraryPeptide>
        {
            new("PEPTIDEK", RetentionTime: 10, IsDetectable: null),
            new("ELVISLIVESK", RetentionTime: 20, IsDetectable: null)
        };
        var generator = new SpectralLibraryGenerator(peptides, options, "unused.msp");

        var (inputs, rts) = generator.BuildPredictionInputs(
            new Dictionary<string, double?> { ["PEPTIDEK"] = 10, ["ELVISLIVESK"] = 20 });

        // mzLib pairs the two arrays positionally, so every input must sit beside its own peptide's
        // retention time. The ordering itself is ours to choose; only the pairing is required.
        Assert.That(inputs, Has.Count.EqualTo(4));
        Assert.That(rts, Has.Count.EqualTo(inputs.Count));
        for (int i = 0; i < inputs.Count; i++)
        {
            double expected = inputs[i].FullSequence == "PEPTIDEK" ? 10 : 20;
            Assert.That(rts[i], Is.EqualTo(expected).Within(1e-9), $"input {i} ({inputs[i].FullSequence}) got the wrong retention time");
        }
        Assert.That(inputs.Select(i => i.PrecursorCharge), Is.EquivalentTo(new[] { 2, 2, 3, 3 }));
    }

    [Test]
    public static void ANegativeRetentionTimeIsAPredictionNotAFailure()
    {
        // Chronologer predicts below zero for hydrophilic peptides; GSGSGSGSK is about -0.464.
        var generator = new SpectralLibraryGenerator(new List<SpectralLibraryPeptide>(), PermissiveOptions, "unused.msp");
        var peptides = new List<SpectralLibraryPeptide> { new("GSGSGSGSK", RetentionTime: null, IsDetectable: null) };

        var resolved = generator.ResolveRetentionTimes(peptides);

        Assert.That(resolved["GSGSGSGSK"], Is.Not.Null);
        Assert.That(resolved["GSGSGSGSK"], Is.LessThan(0));
    }

    [Test]
    public static void RankFilteringKeepsIndicesAscendingSoFragmentsAreNotDuplicated()
    {
        // RetainFragments compacts in place, so a descending keep-set would overwrite a source slot
        // before reading it and silently duplicate a fragment.
        var prediction = new PeptideFragmentIntensityPrediction(
            FullSequence: MzLibSequence,
            ValidatedFullSequence: UnimodSequence,
            PrecursorCharge: 2,
            FragmentAnnotations: new List<string> { "b2+1", "y2+1", "b3+1" },
            FragmentMZs: new List<double> { 227.1026, 276.1554, 324.1554 },
            // Keep-set is {1,0} before sorting: descending intensity picks y2+1 then b2+1. Without
            // the sort, RetainFragments overwrites slot 0 before reading it and duplicates y2+1.
            FragmentIntensities: new List<double> { 0.9, 1.0, 0.1 });

        var options = PermissiveOptions;
        options.FilterByIntensityRank = true;
        options.IntensityRankThreshold = 2;

        GeneratorWith(options).ApplyFragmentFilters(new[] { prediction }, AllValid(1));

        Assert.That(prediction.FragmentAnnotations, Is.EqualTo(new[] { "b2+1", "y2+1" }));
        Assert.That(prediction.FragmentAnnotations, Is.Unique);
        Assert.That(prediction.FragmentMZs, Is.EqualTo(new[] { 227.1026, 276.1554 }));
    }

    #endregion

    #region Writing

    [Test]
    public static void SpectraLeftWithNoFragmentsAreDroppedRatherThanKillingTheWrite()
    {
        // The MSP writer takes Max() over a spectrum's peaks, so an empty one throws
        // "Sequence contains no elements" and the export produces no file at all.
        var withPeaks = SpectrumWith(new MatchedFragmentIon(
            new Product(ProductType.b, FragmentationTerminus.N, 226.0953, 2, 2, 0), 227.1026, 1.0, 1));
        var emptied = SpectrumWith();
        var library = new List<LibrarySpectrum> { withPeaks, emptied };

        string path = Path.Combine(Path.GetTempPath(), $"pgtest_{Guid.NewGuid():N}.msp");
        try
        {
            Assert.DoesNotThrow(() => GeneratorWriting(path).WriteLibrary(library));

            Assert.That(library, Has.Count.EqualTo(1), "the empty spectrum is dropped");
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.ReadAllText(path), Does.Contain("PEPTIDEK"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    #endregion

    #region Helpers

    private static LibrarySpectrum SpectrumWith(params MatchedFragmentIon[] ions) =>
        new("PEPTIDEK", 500.0, 2, ions.ToList(), 10.0);

    private static SpectralLibraryGenerator GeneratorWriting(string path) =>
        new(new List<SpectralLibraryPeptide>(), PermissiveOptions, path);


    private static bool[] AllValid(int count) => Enumerable.Repeat(true, count).ToArray();


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
        PredictionModel = FragmentIntensityPredictionModel.Prosit2020IntensityHcd,
        MinimumMZThreshold = 0,
        MaximumMZThreshold = double.MaxValue,
        FilterByRelativeIntensity = false,
        FilterByIntensityRank = false,
        IntensityRankThreshold = -1,
        OutputFormat = SpectralLibraryFormat.Msp
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
            : this(mode, Enumerable.Repeat(true, predictions.Length).ToArray(), predictions)
        {
        }

        public SeededHcdModel(FragmentIonMappingMode mode, bool[] validInputsMask, params PeptideFragmentIntensityPrediction[] predictions)
            : base(fragmentIonMappingMode: mode)
        {
            Predictions = predictions.ToList();
            ValidInputsMask = validInputsMask;
        }
    }

    #endregion
}
