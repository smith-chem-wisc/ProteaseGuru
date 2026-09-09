using MzLibUtil;
using NUnit.Framework;
using Omics.SequenceConversion;
using PredictionClients.Koina.AbstractClasses;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using ProteaseGuru.Tasks;
using Proteomics.ProteolyticDigestion;

namespace ProteaseGuru.Test;

[TestFixture]
internal class SpectralLibraryTests
{
    private const string MzLibSequence = "PEPTC[Common Fixed:Carbamidomethyl on C]IDEK";
    private const string UnimodSequence = "PEPTC[UNIMOD:4]IDEK";

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

    [Test]
    public static void ModifiedPeptidesProduceLibrarySpectra()
    {
        var model = new SeededHcdModel(PredictionFor(MzLibSequence, UnimodSequence));
        var generator = new SpectralLibraryGenerator(new List<InSilicoPep>(), PermissiveOptions, "unused.msp");

        var spectra = generator.PredictionsToLibrarySpectra(model, new List<double> { 12.3 });

        Assert.That(spectra, Has.Count.EqualTo(1));
        Assert.That(spectra[0].Sequence, Is.EqualTo(MzLibSequence));
        Assert.That(spectra[0].MatchedFragmentIons, Has.Count.EqualTo(2));
        Assert.That(spectra[0].RetentionTime, Is.EqualTo(12.3).Within(1e-9));
    }

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

    private sealed class SeededHcdModel : Prosit2020IntensityHCD
    {
        public SeededHcdModel(params PeptideFragmentIntensityPrediction[] predictions)
        {
            Predictions = predictions.ToList();
            ValidInputsMask = Enumerable.Repeat(true, predictions.Length).ToArray();
        }
    }
}
