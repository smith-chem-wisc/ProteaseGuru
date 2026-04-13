using Proteomics;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using PredictionClients.Koina.SupportedModels.RetentionTimeModels;
using System.Text.RegularExpressions;
using PredictionClients.Koina.AbstractClasses;

namespace Tasks;

/// <summary>
/// Exports a spectrum library for a single protein by:
///   1. Digesting with the checked proteases.
///   2. Predicting iRT values via Prosit_2019_irt (Koina).
///   3. Predicting HCD fragment intensities via Prosit_2020_intensity_HCD (Koina),
///      passing the iRT values as retention times.
///   4. Writing an .msp spectral library file alongside the source FASTA using
///      the mzLib SpectralLibrary writer built into the HCD model.
///
/// Important model constraints (from mzLib source):
///   - Both Prosit models: peptide base length 1-30 AA, canonical residues only.
///   - Prosit_2020_intensity_HCD: precursor charges 1-6 only (charge 7 is NOT supported).
///   - Supported modifications: [Common Fixed:Carbamidomethyl on C] and
///     [Common Variable:Oxidation on M].  All other modifications cause a peptide
///     to be silently skipped by the model constructor.
///   - Optimal NCE values: 20, 23, 25, 28, 30, 35.
/// </summary>
public static class SpectrumLibraryExporter
{
    // ── NCE validation ─────────────────────────────────────────────────────────
    // The model accepts any positive integer, but Prosit 2020 is calibrated for
    // 20, 23, 25, 28, 30, 35. We expose a permissive range that covers common usage.
    public const int MinNce = 20;
    public const int MaxNce = 45;

    // ── Charge constraint from Prosit_2020_intensity_HCD.AllowedPrecursorCharges ─
    // The HCD model only supports charges 1-6; charge 7 is explicitly excluded.
    public const int MaxAllowedCharge = 6;

    // ── Regex for stripping mod brackets when measuring base-sequence length ───
    private static readonly Regex ModPattern = new(@"\[[^\]]+\]", RegexOptions.Compiled);

    /// <summary>
    /// Main entry point. <paramref name="progress"/> receives human-readable status
    /// strings throughout execution. Await this method from the UI thread to avoid
    /// blocking; network calls are dispatched to thread-pool threads internally.
    /// </summary>
    /// <param name="protein">The protein to export.</param>
    /// <param name="proteaseParams">Currently-checked protease-specific parameters.</param>
    /// <param name="chargeStates">
    ///     Precursor charges to generate. Values outside 1-6 are silently dropped
    ///     because Prosit_2020_intensity_HCD does not support charge state 7.
    /// </param>
    /// <param name="nce">Normalised collision energy (recommend 20-45, ideally 25/28/30/35).</param>
    /// <param name="fastaPath">
    ///     Path to the originating FASTA. The library is written to the same directory.
    ///     Falls back to Documents/ProteaseGuru/SpectrumLibraries if null or missing.
    /// </param>
    /// <param name="progress">Optional status-message callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the written .msp file.</returns>
    public static async Task<string> ExportAsync(
        Protein protein,
        IEnumerable<ProteaseSpecificParameters> proteaseParams,
        IReadOnlyList<int> chargeStates,
        int nce,
        string? fastaPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // ── Input guards ───────────────────────────────────────────────────────
        if (protein is null) throw new ArgumentNullException(nameof(protein));
        if (proteaseParams is null) throw new ArgumentNullException(nameof(proteaseParams));
        if (chargeStates is null || chargeStates.Count == 0)
            throw new ArgumentException("At least one charge state must be selected.", nameof(chargeStates));
        if (nce < MinNce || nce > MaxNce)
            throw new ArgumentOutOfRangeException(nameof(nce),
                $"NCE must be between {MinNce} and {MaxNce}.");

        // Clamp charges to the range the HCD model supports (1-6).
        var validCharges = chargeStates
            .Where(z => z >= 1 && z <= MaxAllowedCharge)
            .Distinct()
            .OrderBy(z => z)
            .ToList();

        if (validCharges.Count == 0)
            throw new ArgumentException(
                $"No valid charge states remain after filtering to 1-{MaxAllowedCharge}. " +
                "Prosit_2020_intensity_HCD does not support charge state 7.",
                nameof(chargeStates));

        // ── Step 1: Digest ─────────────────────────────────────────────────────
        progress?.Report("Digesting protein…");
        cancellationToken.ThrowIfCancellationRequested();

        var uniqueMzLibSequences = DigestToUniqueMzLibSequences(protein, proteaseParams);

        if (uniqueMzLibSequences.Count == 0)
            throw new InvalidOperationException(
                "No peptides passed the Prosit filter " +
                "(base length 1-30, canonical residues, supported modifications only). " +
                "Try adjusting missed cleavages or peptide length limits.");

        progress?.Report($"Digest complete: {uniqueMzLibSequences.Count} unique Prosit-compatible peptides.");

        // ── Step 2: Prosit iRT prediction ──────────────────────────────────────
        progress?.Report("Requesting iRT values from Prosit_2019_irt via Koina…");
        cancellationToken.ThrowIfCancellationRequested();

        var irtModelInputs = uniqueMzLibSequences.Select(p => new RetentionTimePredictionInput(FullSequence: p)).ToList();

        var rtModel = new Prosit2019iRT();
        var irtPredictions = await Task.Run(() => rtModel.Predict(irtModelInputs), cancellationToken);

        var seqToIrt = new Dictionary<string, double?>(uniqueMzLibSequences.Count);
        for (int i = 0; i < uniqueMzLibSequences.Count; i++)
        {
            seqToIrt[uniqueMzLibSequences[i]] = irtPredictions[i].PredictedRetentionTime;
        }

        progress?.Report($"iRT predictions received for {seqToIrt.Count} peptides.");
        cancellationToken.ThrowIfCancellationRequested();

        // ── Step 3: Build the inputs for Prosit2020IntensityHCD ───

        var hcdModelInputs = new List<FragmentIntensityPredictionInput>();
        var alignedRts = new List<double?>();

        foreach (var mzLibSeq in uniqueMzLibSequences)
        {
            var irt = seqToIrt.TryGetValue(mzLibSeq, out var rt) ? rt : null;
            foreach (int z in validCharges)
            {
                alignedRts.Add(irt);
                hcdModelInputs.Add(new FragmentIntensityPredictionInput(
                    FullSequence: mzLibSeq,
                    PrecursorCharge: z,
                    CollisionEnergy: nce,
                    InstrumentType: null,
                    FragmentationType: null));
            }
        }

        // ── Step 4: Prosit HCD intensity prediction + library write ────────────
        progress?.Report(
            $"Requesting HCD intensities from Prosit_2020_intensity_HCD " +
            $"({hcdModelInputs.Count} spectra, NCE {nce})…");
        cancellationToken.ThrowIfCancellationRequested();

        string outputPath = BuildOutputPath(protein, fastaPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var hcdModel = new Prosit2020IntensityHCD();
        _ = await Task.Run(() => hcdModel.Predict(hcdModelInputs), cancellationToken);

        var mask = hcdModel.ValidInputsMask.ToList();
        int validCount = mask.Count(b => b);
        int invalidCount = mask.Count - validCount;

        if (validCount == 0)
            throw new InvalidOperationException(
                "All peptide/charge combinations were rejected by Prosit_2020_intensity_HCD.");

        progress?.Report(
            $"HCD prediction complete: {validCount} spectra generated. " +
            $"{invalidCount} invalid inputs were skipped. " +
            $"Writing spectral library to {outputPath}…");
        cancellationToken.ThrowIfCancellationRequested();

        hcdModel.GenerateLibrarySpectraFromPredictions(alignedRts.ToArray(), out var duplicatesWarning, filepath: outputPath);
        if (duplicatesWarning != null)
            progress?.Report($"Library note: {duplicatesWarning.Message}");

        progress?.Report($"Done. {validCount} spectra written to: {outputPath}");

        return outputPath;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Digestion helper
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Digests the protein with every checked protease, deduplicates by FullSequence,
    /// and pre-filters to peptides that Prosit will accept:
    ///   - Base-sequence length 1-30 AA (after stripping modification brackets).
    ///   - Only the 20 canonical amino acids (ACDEFGHIKLMNPQRSTVWY).
    ///   - Only supported modifications: [Common Fixed:Carbamidomethyl on C]
    ///     and [Common Variable:Oxidation on M].
    ///
    /// Pre-filtering here ensures the caller can report a meaningful count before
    /// the network calls, and avoids silent drops inside the model constructors.
    ///
    /// Returns FullSequences in mzLib format, ready to pass directly to
    /// Prosit2019iRT and Prosit2020IntensityHCD constructors.
    /// </summary>
    private static List<string> DigestToUniqueMzLibSequences(
        Protein protein,
        IEnumerable<ProteaseSpecificParameters> proteaseParams)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var pp in proteaseParams)
        {
            var peptides = protein.Digest(pp.DigestionParams, pp.FixedMods, pp.VariableMods);

            foreach (var pep in peptides)
            {
                if (!seen.Add(pep.FullSequence))
                    continue;

                // Strip modification brackets to measure base-sequence length.
                string baseSeq = ModPattern.Replace(pep.FullSequence, string.Empty);

                if (baseSeq.Length < 1 || baseSeq.Length > 30)
                    continue;

                // Only canonical amino acids.
                if (!baseSeq.All(c => "ACDEFGHIKLMNPQRSTVWY".Contains(c)))
                    continue;

                // Only Prosit-supported modifications.
                bool allModsOk = ModPattern
                    .Matches(pep.FullSequence)
                    .All(m =>
                        m.Value == "[Common Fixed:Carbamidomethyl on C]" ||
                        m.Value == "[Common Variable:Oxidation on M]");

                if (!allModsOk)
                    continue;

                result.Add(pep.FullSequence);
            }
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Output-path helper
    // ═════════════════════════════════════════════════════════════════════════

    private static string BuildOutputPath(Protein protein, string? fastaPath)
    {
        string dir;
        if (!string.IsNullOrWhiteSpace(fastaPath) && File.Exists(fastaPath))
        {
            dir = Path.GetDirectoryName(fastaPath)!;
        }
        else
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ProteaseGuru",
                "SpectrumLibraries");
        }

        // Make a filesystem-safe accession string.
        var invalid = Path.GetInvalidFileNameChars();
        string safeName = string.Concat(
            (protein.Accession ?? "protein").Select(c => invalid.Contains(c) ? '_' : c));

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(dir, $"{safeName}_SpecLib_{timestamp}.msp");
    }
}
