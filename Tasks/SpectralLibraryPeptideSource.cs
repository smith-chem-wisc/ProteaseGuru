using Omics;

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

    /// <summary>
    /// Whether peptides carry detectability, and so whether the undetectable-peptide filter applies.
    /// </summary>
    bool SupportsDetectabilityFilter { get; }

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

    public bool SupportsDetectabilityFilter => true;

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
            .Where(p => !options.ExcludeUndetectablePeptides || p.PflyDetectability == true)
            .DistinctBy(p => p.FullSequence)
            .Select(p => new SpectralLibraryPeptide(
                p.FullSequence,
                // -1 is the sentinel for peptides Chronologer could not predict during the run.
                p.ChronologerRetentionTime >= 0 ? p.ChronologerRetentionTime : null,
                p.PflyDetectability))
            .ToList();
    }
}

/// <summary>
/// Digests on demand with the protease parameters the caller currently has selected, so a library can
/// be exported before any run has happened and against parameters the run did not use. Peptides carry
/// no retention time or detectability; the generator predicts retention times for them.
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

    public bool SupportsDetectabilityFilter => false;

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

        return peptides;
    }
}
