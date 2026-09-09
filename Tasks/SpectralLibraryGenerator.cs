using Omics.SequenceConversion;
using Omics.SpectrumMatch;
using PredictionClients.Koina.AbstractClasses;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using PredictionClients.Koina.Util;
using Proteomics.ProteolyticDigestion;
using Readers.SpectralLibrary;

namespace ProteaseGuru.Tasks
{
    /// <summary>
    /// The fragment intensity models ProteaseGuru can drive.
    /// </summary>
    public enum FragmentIntensityPredictionModel
    {
        Prosit2020IntensityHcd
    }

    /// <summary>
    /// The library formats mzLib can write. mzLib routes on file extension, so these exist to build the
    /// save dialog's filter and default extension, and to keep the two in step.
    /// </summary>
    public enum SpectralLibraryFormat
    {
        Msp,
        Msl
    }

    public static class SpectralLibraryFormats
    {
        public static string Extension(this SpectralLibraryFormat format) => format switch
        {
            SpectralLibraryFormat.Msp => ".msp",
            SpectralLibraryFormat.Msl => ".msl",
            _ => throw new NotSupportedException($"No extension is defined for {format}.")
        };

        public static string FileFilter(this SpectralLibraryFormat format) => format switch
        {
            SpectralLibraryFormat.Msp => "MSP Files (*.msp)|*.msp",
            SpectralLibraryFormat.Msl => "MSL Files (*.msl)|*.msl",
            _ => throw new NotSupportedException($"No file filter is defined for {format}.")
        };
    }

    /// <summary>
    /// Configuration options for spectral library generation
    /// </summary>
    public class SpectralLibraryExportOptions
    {
        // Peptide source filtering options
        public List<string> SelectedProteases { get; set; }
        public List<string> SelectedProteins { get; set; }

        // Prediction model options
        public FragmentIntensityPredictionModel PredictionModel { get; set; } = FragmentIntensityPredictionModel.Prosit2020IntensityHcd;
        public List<int> ChargeStates { get; set; }
        public int CollisionEnergy { get; set; }

        // Peptide filtering options
        public bool ExcludeIncompatiblePeptides { get; set; }
        public bool ExcludeUndetectablePeptides { get; set; }
        public double DetectabilityThreshold { get; set; } = 0.5;

        // Fragment ion filtering options
        public double MinimumMZThreshold { get; set; }
        public double MaximumMZThreshold { get; set; }
        public bool FilterByRelativeIntensity { get; set; }
        public double RelativeIntensityThreshold { get; set; }
        public bool FilterByIntensityRank { get; set; }
        public int? IntensityRankThreshold { get; set; }

        // Output options
        public SpectralLibraryFormat OutputFormat { get; set; } = SpectralLibraryFormat.Msp;
    }

    public class SpectralLibraryGenerator
    {
        /// <summary>Absolute intensity floor, below mzLib's default so the user's own filters govern.</summary>
        internal const double MinimumAbsoluteIntensity = 1e-6;

        private readonly List<SpectralLibraryPeptide> _peptides;
        private readonly SpectralLibraryExportOptions _options;
        private readonly string _outputPath;

        public SpectralLibraryGenerator(
            List<SpectralLibraryPeptide> peptides,
            SpectralLibraryExportOptions options,
            string outputPath)
        {
            _peptides = peptides;
            _options = options;
            _outputPath = outputPath;
        }

        /// <summary>
        /// Builds the prediction model the options ask for. Separate from generation so the configuration
        /// can be asserted without a Koina round trip.
        /// </summary>
        internal FragmentIntensityModel CreateIntensityModel()
        {
            switch (_options.PredictionModel)
            {
                case FragmentIntensityPredictionModel.Prosit2020IntensityHcd:
                    return new Prosit2020IntensityHCD(
                       modHandlingMode: _options.ExcludeIncompatiblePeptides ? SequenceConversionHandlingMode.ReturnNull : SequenceConversionHandlingMode.RemoveIncompatibleElements,
                       parameterHandlingMode: IncompatibleParameterHandlingMode.ReturnNull,
                       // Input, not validated: the peptide written to the library, the m/z filtered on,
                       // and the m/z written must all describe the molecule the user asked about.
                       fragmentIonMappingMode: FragmentIonMappingMode.MapToInputFullSequence
                       );
                default:
                    throw new NotSupportedException($"Prediction model {_options.PredictionModel} is not supported.");
            }
        }

        /// <summary>
        /// Predicts fragment intensities and writes the library. Cancellation is cooperative between
        /// stages: neither the Koina round trip nor a Chronologer forward pass can be interrupted once
        /// started, so a cancel takes effect at the next stage boundary rather than immediately.
        /// </summary>
        public List<LibrarySpectrum> GenerateLibrary(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var model = CreateIntensityModel();

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Resolving retention times for {_peptides.Count} peptides...");
            var retentionTimes = ResolveRetentionTimes(_peptides);

            var (inputs, rts) = BuildPredictionInputs(retentionTimes);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Predicting fragment intensities for {inputs.Count} spectra. This may take several minutes...");
            model.Predict(inputs);
            ReportRejectedInputs(model, progress);
            ReportAlteredSequences(model, progress);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Filtering fragment ions...");
            ApplyFragmentFilters(model.Predictions, model.ValidInputsMask);

            // Asked not to write: WriteLibrary has to drop emptied spectra first.
            var library = model.GenerateLibrarySpectraFromPredictions(
                alignedRetentionTimes: rts.ToArray(),
                warning: out _,
                filepath: null,
                minIntensityFilter: MinimumAbsoluteIntensity);

            int collapsed = model.ValidInputsMask.Count(valid => valid) - library.Count;
            if (collapsed > 0)
                progress?.Report($"{collapsed} duplicate spectra (same peptide at the same charge) were collapsed.");

            // Last chance to stop: past this point a cancelled export has still written its file, and
            // reporting success for it would be worse than the wasted work.
            cancellationToken.ThrowIfCancellationRequested();

            WriteLibrary(library, progress);
            progress?.Report($"Wrote {library.Count} spectra to {_outputPath}.");

            return library;
        }

        /// <summary>
        /// Retention times keyed by full sequence. Peptides that arrive without one - anything digested
        /// on demand rather than read back from a run - are predicted here, on the same sequence the
        /// intensity model is given, so both describe the same molecule.
        /// </summary>
        internal static Dictionary<string, double?> ResolveRetentionTimes(List<SpectralLibraryPeptide> peptides)
        {
            var known = new Dictionary<string, double?>(StringComparer.Ordinal);
            var toPredict = new List<PeptideWithSetModifications>();

            foreach (var peptide in peptides)
            {
                if (known.ContainsKey(peptide.FullSequence)) continue;

                known[peptide.FullSequence] = peptide.RetentionTime;
                if (peptide.RetentionTime == null)
                    toPredict.Add(new PeptideWithSetModifications(peptide.FullSequence));
            }

            if (toPredict.Count == 0) return known;

            using var session = SharedChronologerPredictor.Open();
            var predictions = session.Predict(toPredict, maxThreads: Environment.ProcessorCount);

            for (int i = 0; i < predictions.Count; i++)
            {
                // Null is how the model reports failure. Negative values are real: Chronologer
                // predicts below zero for hydrophilic peptides (GSGSGSGSK is -0.464).
                known[toPredict[i].FullSequence] = predictions[i].PredictedValue;
            }

            return known;
        }

        /// <summary>
        /// Writes what can be written. A spectrum the user's filters left with no fragment ions is
        /// dropped first: the MSP writer takes Max() over the peaks, so one empty spectrum would throw
        /// and cost the whole export.
        /// </summary>
        /// <summary>
        /// Counts the peptides whose sequence the model had to change in order to predict them. mzLib
        /// records that on each prediction; the library still names the peptide the user asked about.
        /// </summary>
        internal static void ReportAlteredSequences(FragmentIntensityModel model, IProgress<string>? progress)
        {
            int altered = model.ValidInputsMask
                .Select((valid, index) => (valid, index))
                .Where(x => x.valid && model.Predictions[x.index].Warning != null)
                .Select(x => model.Predictions[x.index].FullSequence)
                .Distinct()
                .Count();

            if (altered > 0)
            {
                progress?.Report(
                    $"{altered} peptides carried modifications {model.ModelName} cannot represent. Their " +
                    "intensities were predicted without those modifications, but the library still names " +
                    "the modified peptide.");
            }
        }

        internal void WriteLibrary(List<LibrarySpectrum> spectra, IProgress<string>? progress = null)
        {
            int emptied = spectra.RemoveAll(s => s.MatchedFragmentIons.Count == 0);
            if (emptied > 0)
                progress?.Report($"Dropped {emptied} spectra whose fragment ions were all filtered out.");

            // Only a failure if there was something to write and the filters took all of it. Having no
            // spectra at all is either no selection, which the dialog stops, or total model rejection,
            // which ReportRejectedInputs has already thrown for.
            if (spectra.Count == 0 && emptied > 0)
            {
                throw new InvalidOperationException(
                    $"All {emptied} spectra were left with no fragment ions once filtering was applied, " +
                    "so there is nothing to write. Widen the m/z, relative-intensity and rank filters " +
                    "and export again.");
            }

            switch (_options.OutputFormat)
            {
                case SpectralLibraryFormat.Msp:
                    new SpectralLibrary { Results = spectra }.WriteResults(_outputPath);
                    break;
                case SpectralLibraryFormat.Msl:
                    MslLibrary.SaveFromLibrarySpectra(_outputPath, spectra);
                    break;
                default:
                    throw new NotSupportedException($"Cannot write a spectral library in {_options.OutputFormat} format.");
            }
        }

        /// <summary>
        /// Says how many inputs the model refused, and fails loudly if it refused all of them rather
        /// than writing an empty library. The model decides what it can accept -- its limits are read
        /// back off it here rather than restated -- so this stays correct if those limits change.
        /// </summary>
        internal static void ReportRejectedInputs(FragmentIntensityModel model, IProgress<string>? progress)
        {
            int total = model.ValidInputsMask.Length;
            int rejected = model.ValidInputsMask.Count(valid => !valid);
            if (rejected == 0) return;

            if (rejected == total)
            {
                throw new InvalidOperationException(
                    $"Every peptide was rejected by {model.ModelName}. It accepts base sequences of " +
                    $"{model.MinPeptideLength}-{model.MaxPeptideLength} canonical residues at charges " +
                    $"{string.Join(", ", model.AllowedPrecursorCharges.OrderBy(c => c))}, with modifications " +
                    $"limited to UNIMOD {string.Join(", ", model.AllowedUnimodIds.OrderBy(id => id))}.");
            }

            progress?.Report(
                $"{rejected} of {total} peptide and charge combinations were rejected by {model.ModelName} " +
                "and will not appear in the library.");
        }

        /// <summary>
        /// One prediction input per peptide per charge state, with a retention time array of the same
        /// length and ordering. mzLib pairs the two positionally, so they must stay in lockstep.
        /// </summary>
        internal (List<FragmentIntensityPredictionInput> Inputs, List<double?> RetentionTimes) BuildPredictionInputs(
            Dictionary<string, double?> retentionTimes)
        {
            var inputs = new List<FragmentIntensityPredictionInput>();
            var rts = new List<double?>();

            foreach (var charge in _options.ChargeStates)
            {
                foreach (var peptide in _peptides)
                {
                    inputs.Add(new FragmentIntensityPredictionInput(
                        FullSequence: peptide.FullSequence,
                        PrecursorCharge: charge,
                        CollisionEnergy: _options.CollisionEnergy,
                        InstrumentType: null,
                        FragmentationType: null));
                    rts.Add(retentionTimes[peptide.FullSequence]);
                }
            }

            return (inputs, rts);
        }

        /// <summary>
        /// Removes the fragments the user filtered out, in place, before mzLib turns predictions into
        /// spectra. These three filters -- m/z range, intensity relative to the base peak, and top-N by
        /// rank -- are the only part of library generation ProteaseGuru owns; everything downstream of
        /// here is mzLib's GenerateLibrarySpectraFromPredictions.
        /// </summary>
        internal void ApplyFragmentFilters(
            IReadOnlyList<PeptideFragmentIntensityPrediction> predictions,
            IReadOnlyList<bool> validInputsMask)
        {
            if (predictions.Count != validInputsMask.Count)
                throw new ArgumentException(
                    $"Expected one mask entry per prediction, got {validInputsMask.Count} for {predictions.Count}.",
                    nameof(validInputsMask));

            for (int p = 0; p < predictions.Count; p++)
            {
                // Predict realigns Predictions to the full input length, inserting placeholders whose
                // three fragment lists are all null for inputs it rejected -- anything over Prosit's
                // 30-residue limit, non-canonical residues, unsupported mods. Only valid entries have
                // fragments to filter, and only they survive into the library.
                if (!validInputsMask[p]) continue;

                var prediction = predictions[p];
                // DefaultIfEmpty guards predictions whose fragments were all stripped upstream, where
                // Max() would throw. Only consumed when relative-intensity filtering is on.
                double maxIntensity = prediction.FragmentIntensities.DefaultIfEmpty(0).Max();

                var keep = new List<int>(prediction.FragmentAnnotations.Count);
                for (int i = 0; i < prediction.FragmentAnnotations.Count; i++)
                {
                    if (prediction.FragmentMZs[i] < _options.MinimumMZThreshold ||
                        prediction.FragmentMZs[i] > _options.MaximumMZThreshold)
                    {
                        continue;
                    }

                    if (_options.FilterByRelativeIntensity &&
                        prediction.FragmentIntensities[i] < maxIntensity * _options.RelativeIntensityThreshold)
                    {
                        continue;
                    }

                    keep.Add(i);
                }

                if (_options.FilterByIntensityRank && _options.IntensityRankThreshold is { } rankLimit)
                {
                    keep = keep
                        .OrderByDescending(i => prediction.FragmentIntensities[i])
                        .Take(rankLimit)
                        .OrderBy(i => i)
                        .ToList();
                }

                RetainFragments(prediction, keep);
            }
        }

        /// <summary>
        /// Rewrites a prediction's three parallel fragment lists down to <paramref name="keep"/>, which
        /// must be ascending. The lists are rewritten rather than replaced because Predictions is not
        /// settable from outside the model.
        /// </summary>
        private static void RetainFragments(PeptideFragmentIntensityPrediction prediction, List<int> keep)
        {
            if (keep.Count == prediction.FragmentAnnotations.Count) return;

            for (int target = 0; target < keep.Count; target++)
            {
                int source = keep[target];
                prediction.FragmentAnnotations[target] = prediction.FragmentAnnotations[source];
                prediction.FragmentMZs[target] = prediction.FragmentMZs[source];
                prediction.FragmentIntensities[target] = prediction.FragmentIntensities[source];
            }

            prediction.FragmentAnnotations.RemoveRange(keep.Count, prediction.FragmentAnnotations.Count - keep.Count);
            prediction.FragmentMZs.RemoveRange(keep.Count, prediction.FragmentMZs.Count - keep.Count);
            prediction.FragmentIntensities.RemoveRange(keep.Count, prediction.FragmentIntensities.Count - keep.Count);
        }

    }
}
