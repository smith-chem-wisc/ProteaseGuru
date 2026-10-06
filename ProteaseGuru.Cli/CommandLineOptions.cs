using CommandLine;

namespace ProteaseGuru.Cli;

public sealed class CommandLineOptions
{
    [Option('d', "database", Required = true, HelpText = "Database file path(s); supply multiple paths as space-delimited values or repeat --database.")]
    public IEnumerable<string> Databases { get; set; } = Array.Empty<string>();

    [Option('o', "output", Required = true, HelpText = "Output directory for the ProteaseGuru run.")]
    public string OutputDirectory { get; set; } = string.Empty;

    [Option("rna", Default = false, HelpText = "Run in RNA/oligonucleotide mode (default: protein mode).")]
    public bool RnaMode { get; set; }

    [Option("params", HelpText = "Path to a ProteaseGuru DigestionParameters.toml file. Supplied digestion options override TOML values.")]
    public string? ParametersFile { get; set; }

    [Option("protease", HelpText = "Protease/RNase name(s). Required for individual-option mode; supply multiple names as space-delimited values or repeat --protease.")]
    public IEnumerable<string>? Proteases { get; set; }

    [Option("missed-cleavages", HelpText = "Shared maximum missed cleavages. Defaults to 2 without TOML; with TOML, changes its value only when supplied.")]
    public int? MaxMissedCleavages { get; set; }

    [Option("min-length", HelpText = "Minimum peptide/oligo length. Defaults to 7 without TOML; with TOML, changes its value only when supplied.")]
    public int? MinLength { get; set; }

    [Option("max-length", HelpText = "Maximum peptide/oligo length. Defaults to 50 without TOML; with TOML, changes its value only when supplied.")]
    public int? MaxLength { get; set; }

    [Option("min-mass", HelpText = "Minimum peptide/oligo mass in Da; -1 disables this bound. Defaults to -1 without TOML; with TOML, changes its value only when supplied.")]
    public int? MinMass { get; set; }

    [Option("max-mass", HelpText = "Maximum peptide/oligo mass in Da; -1 disables this bound. Defaults to -1 without TOML; with TOML, changes its value only when supplied.")]
    public int? MaxMass { get; set; }

    [Option("detectability-threshold", HelpText = "Pfly detectability threshold from 0 to 1. Defaults to 0.5 without TOML; with TOML, changes its value only when supplied.")]
    public double? DetectabilityThreshold { get; set; }

    [Option("treat-modified-peptides-as-different", HelpText = "Whether modified sequences are distinct for uniqueness. Defaults to false without TOML; with TOML, supply true or false to override.")]
    public bool? TreatModifiedPeptidesAsDifferent { get; set; }
}
