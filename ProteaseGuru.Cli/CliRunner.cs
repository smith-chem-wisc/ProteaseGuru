using ProteaseGuru.Engine;
using ProteaseGuru.Tasks;
using Omics.Digestion;
using Proteomics.ProteolyticDigestion;
using Transcriptomics.Digestion;

namespace ProteaseGuru.Cli;

public static class CliRunner
{
    public static int Run(CommandLineOptions options)
    {
        try
        {
            ValidatePaths(options);
            GlobalVariables.AnalyteType = options.RnaMode ? AnalyteType.Oligo : AnalyteType.Peptide;

            var parameters = GetParameters(options);
            var databases = options.Databases.Select(path => new DbForDigestion(Path.GetFullPath(path))).ToList();
            var outputDirectory = Path.GetFullPath(options.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            parameters.OutputFolder = outputDirectory;

            EverythingRunnerEngine.FinishedWritingAllResultsFileHandler += OnResultsWritten;
            DigestionTask.OutLabelStatusHandler += OnStatus;
            DigestionTask.DigestionWarnHandler += OnWarning;
            try
            {
                using var task = new DigestionTask { DigestionParameters = parameters };
                var engine = new EverythingRunnerEngine(
                    [("Task1-Digestion", task)], databases, outputDirectory);
                var result = engine.Run();
                if (result == null)
                    throw new InvalidOperationException("The digestion completed without producing results.");
            }
            finally
            {
                EverythingRunnerEngine.FinishedWritingAllResultsFileHandler -= OnResultsWritten;
                DigestionTask.OutLabelStatusHandler -= OnStatus;
                DigestionTask.DigestionWarnHandler -= OnWarning;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ProteaseGuru CLI failed: {ex.Message}");
            return 1;
        }
    }

    public static RunParameters GetParameters(CommandLineOptions options)
    {
        GlobalVariables.AnalyteType = options.RnaMode ? AnalyteType.Oligo : AnalyteType.Peptide;

        if (options.ParametersFile != null)
        {
            if (options.Proteases?.Any() == true)
                throw new ArgumentException("--protease cannot be combined with --params. The TOML file supplies the selected proteases.");
            if (!File.Exists(options.ParametersFile))
                throw new FileNotFoundException("The digestion TOML file was not found.", options.ParametersFile);

            var fromToml = RunParameters.FromToml(options.ParametersFile);
            if (fromToml.ProteaseSpecificParameters.Count == 0)
                throw new ArgumentException("The TOML file must select at least one protease or RNase.");
            ValidateAnalyteType(fromToml, options.RnaMode);
            ApplyOverrides(fromToml, options);
            ValidateParameterValues(fromToml);
            return fromToml;
        }

        var proteaseNames = options.Proteases?.SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (proteaseNames.Count == 0)
            throw new ArgumentException("Individual-option mode requires at least one --protease. Use --params for per-protease settings and modifications.");

        var proteaseParameters = new List<ProteaseSpecificParameters>();
        foreach (var name in proteaseNames)
        {
            if (options.RnaMode)
            {
                if (!RnaseDictionary.Dictionary.TryGetValue(name, out var rnase))
                    throw new ArgumentException($"Unknown RNase '{name}'. Use a name from the available RNases.");
                proteaseParameters.Add(new ProteaseSpecificParameters(new RnaDigestionParams(
                    rnase.Name, options.MaxMissedCleavages ?? 2, options.MinLength ?? 7, options.MaxLength ?? 50)));
            }
            else
            {
                if (!ProteaseDictionary.Dictionary.TryGetValue(name, out var protease))
                    throw new ArgumentException($"Unknown protease '{name}'. Use a name from the available proteases.");
                proteaseParameters.Add(new ProteaseSpecificParameters(new DigestionParams(
                    protease.Name, options.MaxMissedCleavages ?? 2, options.MinLength ?? 7, options.MaxLength ?? 50)));
            }
        }

        var parameters = new RunParameters
        {
            TreatModifiedPeptidesAsDifferent = options.TreatModifiedPeptidesAsDifferent ?? false,
            MinPeptideMassAllowed = options.MinMass ?? -1,
            MaxPeptideMassAllowed = options.MaxMass ?? -1,
            DetectabilityThreshold = options.DetectabilityThreshold ?? 0.5,
            ProteaseSpecificParameters = proteaseParameters
        };
        ValidateParameterValues(parameters);
        return parameters;
    }

    private static void ApplyOverrides(RunParameters parameters, CommandLineOptions options)
    {
        foreach (var protease in parameters.ProteaseSpecificParameters)
        {
            if (options.MaxMissedCleavages.HasValue)
                protease.DigestionParams.MaxMissedCleavages = options.MaxMissedCleavages.Value;
            if (options.MinLength.HasValue)
                protease.DigestionParams.MinLength = options.MinLength.Value;
            if (options.MaxLength.HasValue)
                protease.DigestionParams.MaxLength = options.MaxLength.Value;
        }

        if (options.MinMass.HasValue)
            parameters.MinPeptideMassAllowed = options.MinMass.Value;
        if (options.MaxMass.HasValue)
            parameters.MaxPeptideMassAllowed = options.MaxMass.Value;
        if (options.DetectabilityThreshold.HasValue)
            parameters.DetectabilityThreshold = options.DetectabilityThreshold.Value;
        if (options.TreatModifiedPeptidesAsDifferent.HasValue)
            parameters.TreatModifiedPeptidesAsDifferent = options.TreatModifiedPeptidesAsDifferent.Value;
    }

    private static void ValidatePaths(CommandLineOptions options)
    {
        var databases = options.Databases.SelectMany(path => path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(path => path.Length > 0).ToList();
        if (databases.Count == 0)
            throw new ArgumentException("At least one --database path is required.");
        options.Databases = databases;

        foreach (var path in databases)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("A database file was not found.", path);
            var lowerPath = path.ToLowerInvariant();
            var supported = new[] { ".xml", ".xml.gz", ".fasta", ".fasta.gz", ".fa", ".fa.gz" };
            if (!supported.Any(extension => lowerPath.EndsWith(extension, StringComparison.Ordinal)))
                throw new ArgumentException($"Unsupported database format '{Path.GetExtension(path)}': {path}");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("An --output directory is required.");
    }

    private static void ValidateAnalyteType(RunParameters parameters, bool rnaMode)
    {
        var invalid = parameters.ProteaseSpecificParameters.FirstOrDefault(p =>
            rnaMode != (p.DigestionParams.DigestionAgent is Rnase));
        if (invalid != null)
            throw new ArgumentException(rnaMode
                ? $"TOML contains protein protease '{invalid.DigestionAgentName}' while --rna is enabled."
                : $"TOML contains RNase '{invalid.DigestionAgentName}'; supply --rna to run in RNA mode.");
    }

    private static void ValidateParameterValues(RunParameters parameters)
    {
        if (parameters.ProteaseSpecificParameters.Any(p => p.DigestionParams.MinLength < 1 || p.DigestionParams.MaxLength < p.DigestionParams.MinLength))
            throw new ArgumentException("Length bounds must satisfy 1 <= min-length <= max-length.");
        if (parameters.ProteaseSpecificParameters.Any(p => p.DigestionParams.MaxMissedCleavages < 0))
            throw new ArgumentException("missed-cleavages must be zero or greater.");
        if (parameters.MinPeptideMassAllowed < -1 || parameters.MaxPeptideMassAllowed < -1 ||
            (parameters.MinPeptideMassAllowed >= 0 && parameters.MaxPeptideMassAllowed >= 0 && parameters.MaxPeptideMassAllowed < parameters.MinPeptideMassAllowed))
            throw new ArgumentException("Mass bounds must be -1 (disabled) or nonnegative, and max-mass must not be less than min-mass.");
        if (parameters.DetectabilityThreshold is < 0 or > 1)
            throw new ArgumentException("detectability-threshold must be between 0 and 1.");
    }

    private static void OnResultsWritten(object? sender, StringEventArgs e) => Console.WriteLine($"Results summary: {e.S}");
    private static void OnStatus(object? sender, StringEventArgs e) => Console.WriteLine(e.S);
    private static void OnWarning(object? sender, StringEventArgs e) => Console.Error.WriteLine($"Warning: {e.S}");
}
