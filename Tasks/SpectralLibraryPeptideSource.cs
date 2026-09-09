using Omics;
using Omics.SequenceConversion;
using PredictionClients.Koina.AbstractClasses;
using PredictionClients.Koina.SupportedModels.FlyabilityModels;

namespace ProteaseGuru.Tasks;

/// <summary>
/// A peptide bound for a spectral library, independent of where it came from.
/// <paramref name="RetentionTime"/> is null when the source has none, in which case the generator
/// predicts one. <paramref name="IsDetectable"/> is null when the source has no detectability data.
/// </summary>
public readonly record struct SpectralLibraryPeptide(string FullSequence, double? RetentionTime, bool? IsDetectable);

/// <summary>
/// PFly's converter allows no modifications at all, so it has to strip what it cannot represent and
/// assess the backbone. Rejecting instead would leave every modified peptide unassessed, and
/// unassessed reads as undetectable everywhere it is consumed. The digestion run and the export ask
/// the same question, so they build the model the same way.
/// </summary>
internal static class DetectabilityModel
{
    internal static PFly2024FineTuned Create() =>
        new(modHandlingMode: SequenceConversionHandlingMode.RemoveIncompatibleElements);
}

/// <summary>
/// Supplies the peptides a spectral library is built from, and the protease and protein choices the
/// export dialog offers for them.
/// </summary>
public interface ISpectralLibraryPeptideSource
{
    IReadOnlyList<string> AvailableProteases { get; }

    IReadOnlyList<string> AvailableProteins { get; }

    /// <summary>
    /// Gathering can be long-running -- an on-demand digest, and a detectability round trip when the
    /// filter asks for one -- so it reports progress and observes cancellation.
    /// </summary>
    List<SpectralLibraryPeptide> GetPeptides(
        SpectralLibraryExportOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Draws peptides from a completed digestion run. Retention times were computed during the run, so
/// nothing here needs to predict them.
/// </summary>
public class ResultsBackedPeptideSource : ISpectralLibraryPeptideSource
{
    private readonly CoverageMapConfiguration.ProteinCoverageAnalyzer _analyzer;

    public ResultsBackedPeptideSource(CoverageMapConfiguration.ProteinCoverageAnalyzer analyzer)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public IReadOnlyList<string> AvailableProteases => _analyzer.Proteases;

    public IReadOnlyList<string> AvailableProteins => _analyzer.ProteinAccessions;

    public List<SpectralLibraryPeptide> GetPeptides(
        SpectralLibraryExportOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedProteins = _analyzer.ProteinCoverageResults.Keys
            .Where(p => options.SelectedProteins.Contains(p.Accession));

        var peptides = new HashSet<InSilicoPep>();
        foreach (var protein in selectedProteins)
        {
            foreach (var proteaseName in options.SelectedProteases)
            {
                peptides.UnionWith(_analyzer.GetPeptidesForProteinAndProtease(protein, proteaseName));
            }
        }

        return peptides
            .Where(p => !options.ExcludeUndetectablePeptides || IsDetectableAtThreshold(p, options.DetectabilityThreshold))
            .DistinctBy(p => p.FullSequence)
            .Select(p => new SpectralLibraryPeptide(
                p.FullSequence,
                p.ChronologerRetentionTime,
                p.PflyDetectability))
            .ToList();
    }

    private static bool IsDetectableAtThreshold(InSilicoPep peptide, double threshold)
    {
        if (peptide.PflyProbabilities is { } probabilities)
        {
            return 1.0 - probabilities.NotDetectable >= threshold;
        }

        // Results written before PFly probabilities were persisted can only use the Boolean that was
        // stored with the run. Its original threshold is not recoverable from those legacy files.
        return peptide.PflyDetectability == true;
    }
}

/// <summary>
/// Digests on demand with the protease parameters the caller currently has selected, so a library can
/// be exported before any run has happened and against parameters the run did not use. Peptides carry
/// no retention time; the generator predicts those. Detectability is predicted here, but only when the
/// filter asks for it, since it costs a network round trip and nothing else reads it.
/// </summary>
public class OnDemandDigestPeptideSource : ISpectralLibraryPeptideSource
{
    private readonly IReadOnlyList<IBioPolymer> _proteins;
    private readonly IReadOnlyList<ProteaseSpecificParameters> _proteaseParameters;

    public OnDemandDigestPeptideSource(
        IReadOnlyList<IBioPolymer> proteins,
        IReadOnlyList<ProteaseSpecificParameters> proteaseParameters)
    {
        _proteins = proteins ?? throw new ArgumentNullException(nameof(proteins));
        _proteaseParameters = proteaseParameters ?? throw new ArgumentNullException(nameof(proteaseParameters));
    }

    public IReadOnlyList<string> AvailableProteases =>
        _proteaseParameters.Select(p => p.DigestionAgentName).Distinct().ToList();

    public IReadOnlyList<string> AvailableProteins =>
        _proteins.Select(p => p.Accession).Distinct().ToList();

    public List<SpectralLibraryPeptide> GetPeptides(
        SpectralLibraryExportOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selectedProteins = _proteins
            .Where(p => options.SelectedProteins.Contains(p.Accession))
            .ToList();
        var selectedParameters = _proteaseParameters
            .Where(p => options.SelectedProteases.Contains(p.DigestionAgentName))
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var peptides = new List<SpectralLibraryPeptide>();

        int digested = 0;
        foreach (var protein in selectedProteins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            digested++;
            progress?.Report($"Digesting protein {digested} of {selectedProteins.Count}...");

            foreach (var parameters in selectedParameters)
            {
                foreach (var peptide in protein.Digest(parameters.DigestionParams, parameters.FixedMods, parameters.VariableMods))
                {
                    if (seen.Add(peptide.FullSequence))
                    {
                        peptides.Add(new SpectralLibraryPeptide(peptide.FullSequence, RetentionTime: null, IsDetectable: null));
                    }
                }
            }
        }

        // PFly is intentionally not contacted for an unfiltered export: detectability is not written
        // to the library, so the request would add latency and an avoidable network failure mode.
        if (!options.ExcludeUndetectablePeptides) return peptides;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Predicting detectability for {peptides.Count} peptides...");
        return KeepDetectable(peptides, options.DetectabilityThreshold);
    }

    /// <summary>
    /// Asks PFly which peptides are detectable and drops the rest. A digestion run stores this on each
    /// peptide; digesting on demand has to predict it, which is why it happens only when asked.
    /// </summary>
    private List<SpectralLibraryPeptide> KeepDetectable(
        List<SpectralLibraryPeptide> peptides,
        double detectabilityThreshold)
    {
        if (peptides.Count == 0) return peptides;

        // Predict realigns its result to the input length, so position i answers peptide i.
        var predictions = DetectabilityModel.Create()
            .Predict(peptides.Select(p => new DetectabilityPredictionInput(p.FullSequence)).ToList());

        var detectable = new List<SpectralLibraryPeptide>(peptides.Count);
        for (int i = 0; i < peptides.Count; i++)
        {
            if (IsDetectable(predictions[i].DetectabilityProbabilities, detectabilityThreshold))
            {
                detectable.Add(peptides[i] with { IsDetectable = true });
            }
        }

        return detectable;
    }

    /// <summary>
    /// PFly reports the probability a peptide is <em>not</em> detectable; the filter is stated the
    /// other way round. A peptide it could not assess counts as undetectable, matching how a
    /// digestion run's stored value is read.
    /// </summary>
    internal static bool IsDetectable(
        (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)? probabilities,
        double threshold) =>
        probabilities.HasValue && 1.0 - probabilities.Value.NotDetectable >= threshold;
}
