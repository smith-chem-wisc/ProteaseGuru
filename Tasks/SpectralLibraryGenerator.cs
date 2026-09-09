using System.ComponentModel;
using System.Diagnostics;
using Chemistry;
using Omics.Fragmentation;
using Omics.SequenceConversion;
using Omics.SpectrumMatch;
using PredictionClients.Koina.AbstractClasses;
using PredictionClients.Koina.Interfaces;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using PredictionClients.Koina.Util;
using Proteomics.ProteolyticDigestion;
using Readers.SpectralLibrary;


namespace ProteaseGuru.Tasks
{
    /// <summary>
    /// Configuration options for spectral library generation
    /// </summary>
    public class SpectralLibraryExportOptions
    {
        // Peptide source filtering options
        public List<string> SelectedProteases { get; set; }
        public List<string> SelectedProteins { get; set; }

        // Prediction model options
        public string PredictionModel { get; set; }
        public List<int> ChargeStates { get; set; }
        public int CollisionEnergy { get; set; }

        // Peptide filtering options
        public bool ExcludeIncompatiblePeptides { get; set; }
        public bool ExcludeUndetectablePeptides { get; set; }

        // Fragment ion filtering options
        public double MinimumMZThreshold { get; set; }
        public double MaximumMZThreshold { get; set; }
        public bool FilterByRelativeIntensity { get; set; }
        public double RelativeIntensityThreshold { get; set; }
        public bool FilterByIntensityRank { get; set; }
        public int IntensityRankThreshold { get; set; }

        // Output options
        public string OutputFormat { get; set; }
    }

    public class SpectralLibraryGenerator
    {
        /// <summary>Absolute intensity floor, below mzLib's default so the user's own filters govern.</summary>
        private const double MinimumAbsoluteIntensity = 1e-6;

        /// <summary>Set after generation when mzLib reported something worth surfacing.</summary>
        public string? Warning { get; private set; }

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
        internal FragmentIntensityModel CreateModel()
        {
            switch (_options.PredictionModel)
            {
                case "Prosit2020IntensityHCD":
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

        public List<LibrarySpectrum> GenerateLibrary()
        {
            var model = CreateModel();

            var retentionTimes = ResolveRetentionTimes(_peptides);

            var inputs = new List<FragmentIntensityPredictionInput>();
            var rts = new List<double?>();
            foreach (var pc in _options.ChargeStates)
            {
                inputs.AddRange(_peptides.Select(p => new FragmentIntensityPredictionInput(
                    FullSequence: p.FullSequence,
                    PrecursorCharge: pc,
                    CollisionEnergy: _options.CollisionEnergy,
                    InstrumentType: null,
                    FragmentationType: null
                    )
                ));
                rts.AddRange(_peptides.Select(p => retentionTimes[p.FullSequence]));
            }

            model.Predict(inputs);

            ApplyFragmentFilters(model.Predictions);

            // mzLib builds the spectra, collapses duplicates, and writes MSP or MSL by file extension.
            var library = model.GenerateLibrarySpectraFromPredictions(
                alignedRetentionTimes: rts.ToArray(),
                warning: out var warning,
                filepath: _outputPath,
                minIntensityFilter: MinimumAbsoluteIntensity);

            Warning = warning?.Message;

            return library;
        }

        /// <summary>
        /// Retention times keyed by full sequence. Peptides that arrive without one - anything digested
        /// on demand rather than read back from a run - are predicted here, on the same sequence the
        /// intensity model is given, so both describe the same molecule. Chronologer's -1 sentinel stays
        /// null so that a spectrum is written without a retention time rather than with a fake one.
        /// </summary>
        internal Dictionary<string, double?> ResolveRetentionTimes(List<SpectralLibraryPeptide> peptides)
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
                double? predicted = predictions[i].PredictedValue;
                known[toPredict[i].FullSequence] = predicted >= 0 ? predicted : null;
            }

            return known;
        }

        /// <summary>
        /// Removes the fragments the user filtered out, in place, before mzLib turns predictions into
        /// spectra. These three filters -- m/z range, intensity relative to the base peak, and top-N by
        /// rank -- are the only part of library generation ProteaseGuru owns; everything downstream of
        /// here is mzLib's GenerateLibrarySpectraFromPredictions.
        /// </summary>
        internal void ApplyFragmentFilters(IReadOnlyList<PeptideFragmentIntensityPrediction> predictions)
        {
            foreach (var prediction in predictions)
            {
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

                // -1 means no threshold was set.
                if (_options.FilterByIntensityRank && _options.IntensityRankThreshold != -1)
                {
                    keep = keep
                        .OrderByDescending(i => prediction.FragmentIntensities[i])
                        .Take(_options.IntensityRankThreshold)
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
