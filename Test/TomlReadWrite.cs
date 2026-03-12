using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Omics.Modifications;
using Proteomics.ProteolyticDigestion;
using Tasks;
using Transcriptomics.Digestion;

namespace Test;
[TestFixture]
public class TomlReadWrite
{
    [Test]
    public void RoundTrip_RunParams()
    {
        var carbam = Mods.GetModification("Carbamidomethyl on C");
        var ox = Mods.GetModification("Oxidation on M");

        var original = new RunParameters()
        {
            OutputFolder = "TestOutput",
            TreatModifiedPeptidesAsDifferent = true,
            MinPeptideMassAllowed = 500,
            MaxPeptideMassAllowed = 5000,
            ProteaseSpecificParameters =
            [
                new ProteaseSpecificParameters(new DigestionParams(), new List<Modification> { carbam },
                    new List<Modification> { ox }),
                new ProteaseSpecificParameters(new RnaDigestionParams(), new List<Modification> { carbam, ox },
                    new List<Modification> { ox }),
                new ProteaseSpecificParameters(new DigestionParams(), new List<Modification> { carbam }, new()),
                new ProteaseSpecificParameters(new DigestionParams(), new(), new())
            ]
        };

        var path = Path.Combine(Path.GetTempPath(), "test_parameters.toml");
        RunParameters.ToToml(original, path);

        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.ReadAllText(path).Length, Is.GreaterThan(0));

        var deserialized = RunParameters.FromToml(path);
        Assert.That(deserialized.OutputFolder, Is.EqualTo(original.OutputFolder));
        Assert.That(deserialized.TreatModifiedPeptidesAsDifferent, Is.EqualTo(original.TreatModifiedPeptidesAsDifferent));
        Assert.That(deserialized.MinPeptideMassAllowed, Is.EqualTo(original.MinPeptideMassAllowed));
        Assert.That(deserialized.MaxPeptideMassAllowed, Is.EqualTo(original.MaxPeptideMassAllowed));
        Assert.That(deserialized.ProteaseSpecificParameters.Count, Is.EqualTo(original.ProteaseSpecificParameters.Count));

        for (int i = 0; i < original.ProteaseSpecificParameters.Count; i++)
        {
            var originalParams = original.ProteaseSpecificParameters[i];
            var deserializedParams = deserialized.ProteaseSpecificParameters[i];
            Assert.That(deserializedParams.DigestionAgentName, Is.EqualTo(originalParams.DigestionAgentName));
            Assert.That(deserializedParams.FixedMods.Select(m => m.IdWithMotif), Is.EquivalentTo(originalParams.FixedMods.Select(m => m.IdWithMotif)));
            Assert.That(deserializedParams.VariableMods.Select(m => m.IdWithMotif), Is.EquivalentTo(originalParams.VariableMods.Select(m => m.IdWithMotif)));
        }

        File.Delete(path);
    }

    [Test]
    public void RoundTrip_GlobalParams()
    {
        var carbam = Mods.GetModification("Carbamidomethyl on C");
        var ox = Mods.GetModification("Oxidation on M");

        var original = new RunParameters()
        {
            OutputFolder = "TestOutput",
            TreatModifiedPeptidesAsDifferent = true,
            MinPeptideMassAllowed = 500,
            MaxPeptideMassAllowed = 5000,
            ProteaseSpecificParameters =
            [
                new ProteaseSpecificParameters(new DigestionParams(), new List<Modification> { carbam },
                    new List<Modification> { ox }),
                new ProteaseSpecificParameters(new RnaDigestionParams(), new List<Modification> { carbam, ox },
                    new List<Modification> { ox }),
                new ProteaseSpecificParameters(new DigestionParams(), new List<Modification> { carbam }, new()),
                new ProteaseSpecificParameters(new DigestionParams(), new(), new())
            ]
        };

        var globalParams = new GlobalParameters()
        {
            RunParameters = original,
            AskAboutSettingsChangeOnClose = false,
            OverwriteSettingsWithoutAsking = true
        };

        var path = Path.Combine(Path.GetTempPath(), "test_parameters.toml");
        GlobalParameters.ToToml(globalParams, path);

        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.ReadAllText(path).Length, Is.GreaterThan(0));

        var deserialized = GlobalParameters.FromToml(path);
        Assert.That(deserialized.RunParameters.OutputFolder, Is.EqualTo(original.OutputFolder));
        Assert.That(deserialized.RunParameters.TreatModifiedPeptidesAsDifferent, Is.EqualTo(original.TreatModifiedPeptidesAsDifferent));
        Assert.That(deserialized.RunParameters.MinPeptideMassAllowed, Is.EqualTo(original.MinPeptideMassAllowed));
        Assert.That(deserialized.RunParameters.MaxPeptideMassAllowed, Is.EqualTo(original.MaxPeptideMassAllowed));
        Assert.That(deserialized.RunParameters.ProteaseSpecificParameters.Count, Is.EqualTo(original.ProteaseSpecificParameters.Count));

        for (int i = 0; i < original.ProteaseSpecificParameters.Count; i++)
        {
            var originalParams = original.ProteaseSpecificParameters[i];
            var deserializedParams = deserialized.RunParameters.ProteaseSpecificParameters[i];
            Assert.That(deserializedParams.DigestionAgentName, Is.EqualTo(originalParams.DigestionAgentName));
            Assert.That(deserializedParams.FixedMods.Select(m => m.IdWithMotif), Is.EquivalentTo(originalParams.FixedMods.Select(m => m.IdWithMotif)));
            Assert.That(deserializedParams.VariableMods.Select(m => m.IdWithMotif), Is.EquivalentTo(originalParams.VariableMods.Select(m => m.IdWithMotif)));
        }

        Assert.That(deserialized.AskAboutSettingsChangeOnClose, Is.EqualTo(globalParams.AskAboutSettingsChangeOnClose));
        Assert.That(deserialized.OverwriteSettingsWithoutAsking, Is.EqualTo(globalParams.OverwriteSettingsWithoutAsking));

        File.Delete(path);
    }
}
