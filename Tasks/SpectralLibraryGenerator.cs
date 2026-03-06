using System.ComponentModel;
using Chemistry;
using Omics.Fragmentation;
using Omics.SpectrumMatch;
using PredictionClients.Koina.AbstractClasses;
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
        public List<string> SelectedProteases { get; set; }
        public List<string> SelectedProteins { get; set; }
        public string PredictionModel { get; set; }
        public List<int> ChargeStates { get; set; }
        public int CollisionEnergy { get; set; }
        public bool ExcludeIncompatiblePeptides { get; set; }
        public double MinimumIntensityThreshold { get; set; }
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
            var model = new Prosit2020IntensityHCD(
                modHandlingMode: _options.ExcludeIncompatiblePeptides ? IncompatibleModHandlingMode.ReturnNull : IncompatibleModHandlingMode.RemoveIncompatibleMods,
                parameterHandlingMode: IncompatibleParameterHandlingMode.ReturnNull,
                fragmentIonMappingMode: FragmentIonMappingMode.MapToValidatedFullSequence
                );

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
                        prediction.FragmentIntensities[i] < maxFragmentIntensity * _options.MinimumIntensityThreshold ||
                        prediction.FragmentAnnotations[i] == null ||
                        !prediction.FragmentAnnotations[i].Contains("+"))
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
