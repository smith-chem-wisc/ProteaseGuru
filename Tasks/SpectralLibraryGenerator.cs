using System.ComponentModel;
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

            var predictions = model.Predict(inputs);

            // Write to file based on format
            var library = PredictionsToLibrarySpectra(model, rts);
            WriteLibrary(library);

            return library;
        }
        private List<LibrarySpectrum> PredictionsToLibrarySpectra(FragmentIntensityModel model, List<double> retentionTimes)
        {
            var predictionRTs = model.ValidInputsMask.Select((isValid, index) => (isValid, index))
                .Where(x => x.isValid)
                .ToDictionary(x => model.Predictions[x.index], x => retentionTimes[x.index]);

            var predictedSpectra = new List<LibrarySpectrum>();

            foreach (var prediction in predictionRTs.Keys)
            {
                var peptide = new PeptideWithSetModifications(prediction.ValidatedFullSequence);
                List<MatchedFragmentIon> fragmentIons = new();

                List<Product> theoreticalProducts = new();
                peptide.Fragment(MassSpectrometry.DissociationType.HCD, FragmentationTerminus.Both, theoreticalProducts); 
                Dictionary<string, double> predictionAnnotationIntensityLookup = new();
                Dictionary<string, Product> tpLookup = theoreticalProducts.ToDictionary(tp => tp.Annotation);
                var maxFragmentIntensity = prediction.FragmentIntensities.Max();

                for (int i = 0; i < prediction.FragmentAnnotations.Count; i++)
                {
                    if (prediction.FragmentIntensities[i] == -1 ||
                        prediction.FragmentAnnotations[i] == null ||
                        !prediction.FragmentAnnotations[i].Contains("+") ||
                        prediction.FragmentIntensities[i] < _options.MinimumMZThreshold ||
                        prediction.FragmentIntensities[i] > _options.MaximumMZThreshold ||
                        (_options.FilterByRelativeIntensity &&
                         prediction.FragmentIntensities[i] < maxFragmentIntensity * _options.RelativeIntensityThreshold)
                    )
                    {
                        // Skip impossible ions, peaks with near zero intensity, or misannotated fragments to be safe.
                        // The model uses -1 to indicate impossible ions.
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
                    sequence: prediction.FullSequence,
                    precursorMz: peptide.ToMz(prediction.PrecursorCharge),
                    chargeState: prediction.PrecursorCharge,
                    peaks: fragmentIons,
                    rt: predictionRTs[prediction]
                );

                predictedSpectra.Add(spectrum);
            }

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
