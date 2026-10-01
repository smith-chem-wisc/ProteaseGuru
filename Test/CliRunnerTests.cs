using NUnit.Framework;
using CommandLine;
using Omics.Digestion;
using Omics.Modifications;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Cli;
using ProteaseGuru.Engine;
using ProteaseGuru.Tasks;
using Transcriptomics.Digestion;

namespace ProteaseGuru.Test;

[TestFixture]
public class CliRunnerTests
{
    [Test]
    public void Parser_AllowsExplicitFalseForBooleanTomlOverride()
    {
        CommandLineOptions? parsed = null;
        var result = Parser.Default.ParseArguments<CommandLineOptions>(
            ["--database", "input.fasta", "--output", "results", "--params", "params.toml",
             "--treat-modified-peptides-as-different", "false"]);
        result.WithParsed(options => parsed = options);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.TreatModifiedPeptidesAsDifferent, Is.False);
    }

    [Test]
    public void IndividualOptions_CreateUniformProteinParametersWithGuiDefaults()
    {
        var options = new CommandLineOptions
        {
            Proteases = ["trypsin|P", "Lys-C|P"]
        };

        var parameters = CliRunner.GetParameters(options);

        Assert.That(parameters.ProteaseSpecificParameters, Has.Count.EqualTo(2));
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams),
            Has.All.InstanceOf<DigestionParams>());
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams.MinLength),
            Is.All.EqualTo(7));
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams.MaxLength),
            Is.All.EqualTo(50));
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams.MaxMissedCleavages),
            Is.All.EqualTo(2));
        Assert.That(parameters.ProteaseSpecificParameters.SelectMany(p => p.FixedMods), Is.Empty);
        Assert.That(parameters.ProteaseSpecificParameters.SelectMany(p => p.VariableMods), Is.Empty);
    }

    [Test]
    public void IndividualOptions_CreateUniformRnaParameters()
    {
        var options = new CommandLineOptions
        {
            RnaMode = true,
            Proteases = ["RNase T1", "RNase_MC1"],
            MaxMissedCleavages = 1,
            MinLength = 5,
            MaxLength = 30
        };

        var parameters = CliRunner.GetParameters(options);

        Assert.That(GlobalVariables.AnalyteType, Is.EqualTo(AnalyteType.Oligo));
        Assert.That(parameters.ProteaseSpecificParameters,
            Has.All.Matches<ProteaseSpecificParameters>(p => p.DigestionParams is RnaDigestionParams));
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams.MinLength),
            Is.All.EqualTo(5));
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams.MaxLength),
            Is.All.EqualTo(30));
        Assert.That(parameters.ProteaseSpecificParameters.Select(p => p.DigestionParams.MaxMissedCleavages),
            Is.All.EqualTo(1));
    }

    [Test]
    public void TomlAndProteaseSelection_AreRejectedTogether()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proteaseguru-cli-{Guid.NewGuid():N}.toml");
        try
        {
            RunParameters.ToToml(new RunParameters(), path);
            var options = new CommandLineOptions
            {
                ParametersFile = path,
                Proteases = ["trypsin|P"]
            };

            Assert.That(() => CliRunner.GetParameters(options),
                Throws.ArgumentException.With.Message.Contains("TOML file supplies"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void TomlOptions_OnlyExplicitOverridesReplaceValuesAndKeepOtherSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proteaseguru-cli-{Guid.NewGuid():N}.toml");
        try
        {
            var carbamidomethyl = Mods.GetModification("Carbamidomethyl on C")!;
            var oxidation = Mods.GetModification("Oxidation on M")!;
            var expected = new RunParameters
            {
                MinPeptideMassAllowed = 400,
                MaxPeptideMassAllowed = 2200,
                DetectabilityThreshold = 0.7,
                TreatModifiedPeptidesAsDifferent = true,
                ProteaseSpecificParameters =
                [
                    new ProteaseSpecificParameters(new DigestionParams("trypsin|P", 1, 6, 45), [carbamidomethyl], [oxidation]),
                    new ProteaseSpecificParameters(new DigestionParams("Lys-C|P", 0, 8, 35))
                ]
            };
            RunParameters.ToToml(expected, path);

            var actual = CliRunner.GetParameters(new CommandLineOptions
            {
                ParametersFile = path,
                MaxMissedCleavages = 3,
                TreatModifiedPeptidesAsDifferent = false
            });

            Assert.That(actual.MinPeptideMassAllowed, Is.EqualTo(400));
            Assert.That(actual.MaxPeptideMassAllowed, Is.EqualTo(2200));
            Assert.That(actual.DetectabilityThreshold, Is.EqualTo(0.7));
            Assert.That(actual.TreatModifiedPeptidesAsDifferent, Is.False);
            Assert.That(actual.ProteaseSpecificParameters.Select(p => p.DigestionParams.MaxMissedCleavages),
                Is.All.EqualTo(3));
            Assert.That(actual.ProteaseSpecificParameters.Select(p => p.DigestionParams.MinLength),
                Is.EqualTo(new[] { 6, 8 }));
            Assert.That(actual.ProteaseSpecificParameters[0].FixedMods.Select(m => m.IdWithMotif),
                Is.EqualTo(new[] { carbamidomethyl.IdWithMotif }));
            Assert.That(actual.ProteaseSpecificParameters[0].VariableMods.Select(m => m.IdWithMotif),
                Is.EqualTo(new[] { oxidation.IdWithMotif }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void TomlOptions_LoadTheSavedFullRunParameters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proteaseguru-cli-{Guid.NewGuid():N}.toml");
        try
        {
            var expected = new RunParameters
            {
                MinPeptideMassAllowed = 400,
                MaxPeptideMassAllowed = 2200,
                ProteaseSpecificParameters =
                [
                    new ProteaseSpecificParameters(new DigestionParams("trypsin|P", 1, 6, 45))
                ]
            };
            RunParameters.ToToml(expected, path);

            var actual = CliRunner.GetParameters(new CommandLineOptions { ParametersFile = path });

            Assert.That(actual.MinPeptideMassAllowed, Is.EqualTo(400));
            Assert.That(actual.MaxPeptideMassAllowed, Is.EqualTo(2200));
            Assert.That(actual.ProteaseSpecificParameters.Single().DigestionAgentName, Is.EqualTo("trypsin|P"));
            Assert.That(actual.ProteaseSpecificParameters.Single().DigestionParams.MaxMissedCleavages, Is.EqualTo(1));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void IndividualOptions_RejectUnknownProtease()
    {
        var options = new CommandLineOptions { Proteases = ["not a protease"] };

        Assert.That(() => CliRunner.GetParameters(options),
            Throws.ArgumentException.With.Message.Contains("Unknown protease"));
    }
}
