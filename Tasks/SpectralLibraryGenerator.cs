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


namespace Tasks
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
        private readonly List<InSilicoPep> _peptides;
        private readonly SpectralLibraryExportOptions _options;
        private readonly string _outputPath;

        public SpectralLibraryGenerator(
            List<InSilicoPep> peptides,
            SpectralLibraryExportOptions options,
            string outputPath)
        {
            _peptides = peptides;
            _options = options;
            _outputPath = outputPath;
        }

        public List<LibrarySpectrum> GenerateLibrary()
        {
            FragmentIntensityModel model;
            switch (_options.PredictionModel)
            {
                case "Prosit2020IntensityHCD":
                    model = new Prosit2020IntensityHCD(
                       modHandlingMode: _options.ExcludeIncompatiblePeptides ? SequenceConversionHandlingMode.ReturnNull : SequenceConversionHandlingMode.RemoveIncompatibleElements,
                       parameterHandlingMode: IncompatibleParameterHandlingMode.ReturnNull,
                       fragmentIonMappingMode: FragmentIonMappingMode.MapToValidatedFullSequence
                       );
                    break;
                default:
                    throw new NotSupportedException($"Prediction model {_options.PredictionModel} is not supported.");
            }

            var inputs = new List<FragmentIntensityPredictionInput>();
            var rts = new List<double>();
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
                rts.AddRange(_peptides.Select(p => p.ChronologerRetentionTime));
            }

            model.Predict(inputs);

            // Write to file based on format
            var library = PredictionsToLibrarySpectra(model, rts);
            WriteLibrary(library);

            return library;
        }

        /// <summary>
        /// Mirrors mzLib's FragmentIntensityModel.GenerateLibrarySpectraFromPredictions, but adds the m/z range,
        /// relative-intensity, and top-N rank filters that the upstream method does not currently support. If those
        /// filters are added upstream, this method can be replaced with a direct call to the library method.
        /// </summary>
        private List<LibrarySpectrum> PredictionsToLibrarySpectra(FragmentIntensityModel model, List<double> retentionTimes)
        {
            // FragmentIntensityModel.Predict realigns Predictions to the full input length, inserting placeholder
            // predictions for inputs that failed validation. Predictions is therefore parallel to ValidInputsMask,
            // so indexing it by the absolute input index is correct.
            Debug.Assert(model.Predictions.Count == model.ValidInputsMask.Length,
                "Predictions are expected to be realigned to the full input length (one entry per input, including invalid inputs).");

            // Pair each valid prediction with its retention time positionally rather than keying a
            // dictionary on the prediction. Keying by prediction relies on the record's list members
            // comparing by reference, which is fragile and would break if two equal predictions collided.
            var validPredictions = model.ValidInputsMask.Select((isValid, index) => (isValid, index))
                .Where(x => x.isValid)
                .Select(x => (Prediction: model.Predictions[x.index], RetentionTime: retentionTimes[x.index]))
                .ToList();

            var predictedSpectra = new List<LibrarySpectrum>();

            foreach (var (prediction, retentionTime) in validPredictions)
            {
                var peptide = new PeptideWithSetModifications(prediction.ValidatedFullSequence);
                List<MatchedFragmentIon> fragmentIons = new();

                List<Product> theoreticalProducts = new();
                peptide.Fragment(MassSpectrometry.DissociationType.HCD, FragmentationTerminus.Both, theoreticalProducts);
                Dictionary<string, double> predictionAnnotationIntensityLookup = new();
                Dictionary<string, Product> tpLookup = theoreticalProducts.ToDictionary(tp => tp.Annotation);
                // DefaultIfEmpty guards against predictions whose fragments were all stripped upstream;
                // Max() throws on an empty sequence. Only consumed when relative-intensity filtering is on.
                var maxFragmentIntensity = prediction.FragmentIntensities.DefaultIfEmpty(0).Max();

                for (int i = 0; i < prediction.FragmentAnnotations.Count; i++)
                {
                    // Skip misannotated fragments and apply the user's m/z range and relative-intensity filters.
                    // The m/z gate compares against the fragment m/z (FragmentMZs), not the predicted intensity.
                    // Impossible ions (intensity -1) are already removed upstream in ResponseToPredictions.
                    if (prediction.FragmentAnnotations[i] == null ||
                        !prediction.FragmentAnnotations[i].Contains("+") ||
                        prediction.FragmentMZs[i] < _options.MinimumMZThreshold ||
                        prediction.FragmentMZs[i] > _options.MaximumMZThreshold ||
                        (_options.FilterByRelativeIntensity &&
                         prediction.FragmentIntensities[i] < maxFragmentIntensity * _options.RelativeIntensityThreshold)
                    )
                    {
                        continue;
                    }
                    predictionAnnotationIntensityLookup[prediction.FragmentAnnotations[i]] = prediction.FragmentIntensities[i];
                }

                foreach (var pa in predictionAnnotationIntensityLookup.Keys)
                {
                    var productTypeAndCharge = pa.Split("+");

                    var tp = tpLookup[productTypeAndCharge[0]]; // Get theoretical product ("b5") from annotation like "b5+1"
                    var charge = int.Parse(productTypeAndCharge[1]); // Get charge ("1") from annotation like "b5+1"
                    // Create a new MatchedFragmentIon for each output
                    var fragmentIon = new MatchedFragmentIon
                    (
                        neutralTheoreticalProduct: tp,
                        experMz: tp.ToMz(charge),
                        experIntensity: predictionAnnotationIntensityLookup[pa],
                        charge: charge
                    );

                    fragmentIons.Add(fragmentIon);
                }

                // Apply intensity rank filtering if enabled. -1 is default indication of no threshold set.
                if (_options.FilterByIntensityRank && _options.IntensityRankThreshold != -1)
                {
                    fragmentIons = _options.FilterByIntensityRank ?
                        fragmentIons.OrderByDescending(fi => fi.Intensity).Take(_options.IntensityRankThreshold).ToList()
                        : fragmentIons;
                }

                var spectrum = new LibrarySpectrum
                (
                    sequence: peptide.FullSequence,
                    precursorMz: peptide.ToMz(prediction.PrecursorCharge),
                    chargeState: prediction.PrecursorCharge,
                    peaks: fragmentIons,
                    rt: retentionTime
                );

                predictedSpectra.Add(spectrum);
            }

            // LibrarySpectrum.Name is "Sequence/ChargeState", so this only collapses genuine duplicates
            // (same peptide at the same charge, e.g. shared across proteins/proteases); distinct charge
            // states of the same peptide are preserved.
            var unique = predictedSpectra.DistinctBy(p => p.Name).ToList();
            return unique;
        }

        private void WriteLibrary(List<LibrarySpectrum> spectra)
        {
            switch (_options.OutputFormat)
            {
                case "MSP":
                    WriteMSP(spectra);
                    break;
            }
        }

        private void WriteMSP(List<LibrarySpectrum> spectra)
        {
            var spectralLibrary = new SpectralLibrary();
            spectralLibrary.Results = spectra;
            spectralLibrary.WriteResults(_outputPath);
        }
    }
}
