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
/// Supplies the peptides a spectral library is built from, and the protease and protein choices the
/// export dialog offers for them.
/// </summary>
public interface ISpectralLibraryPeptideSource
{
    IReadOnlyList<string> AvailableProteases { get; }

    IReadOnlyList<string> AvailableProteins { get; }

    List<SpectralLibraryPeptide> GetPeptides(SpectralLibraryExportOptions options);
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

    public List<SpectralLibraryPeptide> GetPeptides(SpectralLibraryExportOptions options)
    {
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

    public List<SpectralLibraryPeptide> GetPeptides(SpectralLibraryExportOptions options)
    {
        var selectedProteins = _proteins.Where(p => options.SelectedProteins.Contains(p.Accession));
        var selectedParameters = _proteaseParameters
            .Where(p => options.SelectedProteases.Contains(p.DigestionAgentName))
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var peptides = new List<SpectralLibraryPeptide>();

        foreach (var protein in selectedProteins)
        {
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
        return options.ExcludeUndetectablePeptides
            ? KeepDetectable(peptides, options.DetectabilityThreshold)
            : peptides;
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

        // PFly accepts no modifications at all -- its converter is built with an empty set of allowed
        // UNIMOD ids -- and defaults to rejecting whatever it cannot represent. Left at that default
        // every modified peptide comes back unassessed, and unassessed is dropped below, so a
        // carbamidomethylated cysteine would be enough to call a peptide undetectable.
        var predictions = new PFly2024FineTuned(SequenceConversionHandlingMode.RemoveIncompatibleElements)
            .Predict(peptides.Select(p => new DetectabilityPredictionInput(p.FullSequence)).ToList());

        if (predictions.Count != peptides.Count)
        {
            throw new InvalidOperationException(
                $"PFly returned {predictions.Count} detectability predictions for {peptides.Count} peptides.");
        }

        var detectable = new List<SpectralLibraryPeptide>(peptides.Count);
        for (int i = 0; i < peptides.Count; i++)
        {
            var probabilities = predictions[i].DetectabilityProbabilities;
            if (probabilities.HasValue && 1.0 - probabilities.Value.NotDetectable >= detectabilityThreshold)
            {
                detectable.Add(peptides[i] with { IsDetectable = true });
            }
        }

        return detectable;
    }
}
