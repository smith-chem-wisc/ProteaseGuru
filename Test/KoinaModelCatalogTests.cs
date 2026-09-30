using NUnit.Framework;
using Omics.SequenceConversion;
using PredictionClients.Koina.Util;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Test;

[TestFixture]
internal static class KoinaModelCatalogTests
{
    [Test]
    public static void EveryFragmentIntensityEnumValueHasOneCatalogEntry()
    {
        Assert.That(KoinaModelCatalog.FragmentIntensityModels.Select(model => model.Id),
            Is.EquivalentTo(Enum.GetValues<FragmentIntensityPredictionModel>()));
        Assert.That(KoinaModelCatalog.FragmentIntensityModels.Select(model => model.Id), Is.Unique);
        Assert.That(KoinaModelCatalog.FragmentIntensityModels.Select(model => model.DisplayName), Is.Unique);
    }

    [Test]
    public static void EveryRetentionTimeEnumValueHasOneCatalogEntry()
    {
        Assert.That(KoinaModelCatalog.RetentionTimeModels.Select(model => model.Id),
            Is.EquivalentTo(Enum.GetValues<RetentionTimePredictionModel>()));
        Assert.That(KoinaModelCatalog.RetentionTimeModels.Select(model => model.Id), Is.Unique);
        Assert.That(KoinaModelCatalog.RetentionTimeModels.Select(model => model.DisplayName), Is.Unique);
    }

    [Test]
    public static void IncompatibleAndDeferredModelsAreDeliberatelyNotOffered()
    {
        var fragmentModels = KoinaModelCatalog.FragmentIntensityModels
            .Select(definition => definition.Create(
                SequenceConversionHandlingMode.ReturnNull,
                IncompatibleParameterHandlingMode.ReturnNull,
                FragmentIonMappingMode.MapToInputFullSequence))
            .ToList();
        var retentionTimeModels = KoinaModelCatalog.RetentionTimeModels
            .Where(definition => !definition.IsLocal)
            .Select(definition => definition.Create(SequenceConversionHandlingMode.ReturnNull))
            .ToList();

        try
        {
            var fragmentTypes = fragmentModels.Select(model => model.GetType().Name).ToArray();
            var retentionTimeTypes = retentionTimeModels.Select(model => model.GetType().Name).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(fragmentTypes, Does.Not.Contain("Ms2PipCIDTMT"));
                Assert.That(fragmentTypes, Does.Not.Contain("Ms2PipITRAQPhospho"));
                Assert.That(fragmentTypes, Does.Not.Contain("Prosit2020IntensityTMT"));
                Assert.That(fragmentTypes, Does.Not.Contain("UniSpec"));
                Assert.That(retentionTimeTypes, Does.Not.Contain("Prosit2020iRTTMT"));
            });
        }
        finally
        {
            foreach (var model in retentionTimeModels) model.Dispose();
        }
    }

    [Test]
    public static void FragmentFactoriesUseTheRequestedHandlingModesAndUniqueKoinaNames()
    {
        var models = KoinaModelCatalog.FragmentIntensityModels
            .Select(definition => definition.Create(
                SequenceConversionHandlingMode.RemoveIncompatibleElements,
                IncompatibleParameterHandlingMode.ReturnNull,
                FragmentIonMappingMode.MapToInputFullSequence))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(models.Select(model => model.ModelName), Is.Unique);
            Assert.That(models.All(model => model.ModHandlingMode == SequenceConversionHandlingMode.RemoveIncompatibleElements), Is.True);
            Assert.That(models.All(model => model.ParameterHandlingMode == IncompatibleParameterHandlingMode.ReturnNull), Is.True);
            Assert.That(models.All(model => model.FragmentIonMappingMode == FragmentIonMappingMode.MapToInputFullSequence), Is.True);
        });
    }

    [Test]
    public static void RetentionFactoriesUseTheRequestedHandlingModeAndUniqueKoinaNames()
    {
        var models = KoinaModelCatalog.RetentionTimeModels
            .Where(definition => !definition.IsLocal)
            .Select(definition => definition.Create(SequenceConversionHandlingMode.RemoveIncompatibleElements))
            .ToList();

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(models.Select(model => model.ModelName), Is.Unique);
                Assert.That(models.All(model => model.ModHandlingMode == SequenceConversionHandlingMode.RemoveIncompatibleElements), Is.True);
            });
        }
        finally
        {
            foreach (var model in models) model.Dispose();
        }
    }

    // Set-level checks pass with two factories swapped, so each id is pinned to the Koina name it must build.
    private static readonly Dictionary<FragmentIntensityPredictionModel, string> FragmentKoinaNames = new()
    {
        [FragmentIntensityPredictionModel.AlphaPeptDeepMs2Generic] = "AlphaPeptDeep_ms2_generic",
        [FragmentIntensityPredictionModel.Altimeter2024Intensities] = "Altimeter_2024_intensities",
        [FragmentIntensityPredictionModel.Ms2PipHcd2021] = "ms2pip_HCD2021",
        [FragmentIntensityPredictionModel.Ms2PipImmunoHcd] = "ms2pip_Immuno_HCD",
        [FragmentIntensityPredictionModel.Ms2PipTimsTof2023] = "ms2pip_timsTOF2023",
        [FragmentIntensityPredictionModel.Ms2PipTimsTof2024] = "ms2pip_timsTOF2024",
        [FragmentIntensityPredictionModel.Ms2PipTtof5600] = "ms2pip_TTOF5600",
        [FragmentIntensityPredictionModel.Prosit2019Intensity] = "Prosit_2019_intensity",
        [FragmentIntensityPredictionModel.Prosit2020IntensityCid] = "Prosit_2020_intensity_CID",
        [FragmentIntensityPredictionModel.Prosit2020IntensityHcd] = "Prosit_2020_intensity_HCD",
        [FragmentIntensityPredictionModel.Prosit2023IntensityTimsTof] = "Prosit_2023_intensity_timsTOF",
        [FragmentIntensityPredictionModel.Prosit2024IntensityCit] = "Prosit_2024_intensity_cit",
        [FragmentIntensityPredictionModel.Prosit2024IntensityPtmsGl] = "Prosit_2024_intensity_PTMs_gl",
        [FragmentIntensityPredictionModel.Prosit2025Intensity22Ptm] = "Prosit_2025_intensity_22PTM",
        [FragmentIntensityPredictionModel.Prosit2025Intensity40Ptm] = "Prosit_2025_intensity_40PTM",
        [FragmentIntensityPredictionModel.Prosit2025IntensityLac] = "Prosit_2025_intensity_lac",
        [FragmentIntensityPredictionModel.Prosit2025IntensityPtm2] = "Prosit_2025_intensity_ptm2"
    };

    private static readonly Dictionary<RetentionTimePredictionModel, string> RetentionTimeKoinaNames = new()
    {
        [RetentionTimePredictionModel.AlphaPeptDeepRtGeneric] = "AlphaPeptDeep_rt_generic",
        [RetentionTimePredictionModel.DeepLcHelaHf] = "Deeplc_hela_hf",
        [RetentionTimePredictionModel.Prosit2019Irt] = "Prosit_2019_irt",
        [RetentionTimePredictionModel.Prosit2024IrtCit] = "Prosit_2024_irt_cit",
        [RetentionTimePredictionModel.Prosit2024IrtPtmsGl] = "Prosit_2024_irt_PTMs_gl",
        [RetentionTimePredictionModel.Prosit2025Irt22Ptm] = "Prosit_2025_irt_22PTM",
        [RetentionTimePredictionModel.Prosit2025Irt40Ptm] = "Prosit_2025_irt_40PTM",
        [RetentionTimePredictionModel.Prosit2025IrtLac] = "Prosit_2025_irt_lac"
    };

    [Test]
    public static void EveryFragmentIntensityIdBuildsTheModelItNames()
    {
        Assert.That(FragmentKoinaNames.Keys, Is.EquivalentTo(Enum.GetValues<FragmentIntensityPredictionModel>()));

        Assert.Multiple(() =>
        {
            foreach (var (id, koinaName) in FragmentKoinaNames)
            {
                var model = KoinaModelCatalog.FragmentIntensity(id).Create(
                    SequenceConversionHandlingMode.ReturnNull,
                    IncompatibleParameterHandlingMode.ReturnNull,
                    FragmentIonMappingMode.MapToInputFullSequence);
                Assert.That(model.ModelName, Is.EqualTo(koinaName), id.ToString());
            }
        });
    }

    [Test]
    public static void EveryRetentionTimeIdBuildsTheModelItNamesExceptLocalChronologer()
    {
        Assert.That(KoinaModelCatalog.RetentionTimeModels.Where(definition => definition.IsLocal).Select(definition => definition.Id),
            Is.EqualTo(new[] { RetentionTimePredictionModel.ChronologerRt }));
        Assert.That(RetentionTimeKoinaNames.Keys.Append(RetentionTimePredictionModel.ChronologerRt),
            Is.EquivalentTo(Enum.GetValues<RetentionTimePredictionModel>()));

        Assert.Multiple(() =>
        {
            foreach (var (id, koinaName) in RetentionTimeKoinaNames)
            {
                using var model = KoinaModelCatalog.RetentionTime(id).Create(SequenceConversionHandlingMode.ReturnNull);
                Assert.That(model.ModelName, Is.EqualTo(koinaName), id.ToString());
            }
        });
    }

    [Test]
    public static void PtmFocusedModelsExplainProteaseGuruInputScope()
    {
        var fragmentModelsWithNotes = KoinaModelCatalog.FragmentIntensityModels
            .Where(definition => definition.InputScopeNote != null)
            .ToArray();
        var retentionModelsWithNotes = KoinaModelCatalog.RetentionTimeModels
            .Where(definition => definition.InputScopeNote != null)
            .ToArray();
        var notes = fragmentModelsWithNotes.Select(definition => definition.InputScopeNote!)
            .Concat(retentionModelsWithNotes.Select(definition => definition.InputScopeNote!));

        Assert.Multiple(() =>
        {
            Assert.That(fragmentModelsWithNotes.Select(definition => definition.Id), Is.EquivalentTo(new[]
            {
                FragmentIntensityPredictionModel.Prosit2024IntensityCit,
                FragmentIntensityPredictionModel.Prosit2024IntensityPtmsGl,
                FragmentIntensityPredictionModel.Prosit2025Intensity22Ptm,
                FragmentIntensityPredictionModel.Prosit2025Intensity40Ptm,
                FragmentIntensityPredictionModel.Prosit2025IntensityLac,
                FragmentIntensityPredictionModel.Prosit2025IntensityPtm2
            }));
            Assert.That(retentionModelsWithNotes.Select(definition => definition.Id), Is.EquivalentTo(new[]
            {
                RetentionTimePredictionModel.Prosit2024IrtCit,
                RetentionTimePredictionModel.Prosit2024IrtPtmsGl,
                RetentionTimePredictionModel.Prosit2025Irt22Ptm,
                RetentionTimePredictionModel.Prosit2025Irt40Ptm,
                RetentionTimePredictionModel.Prosit2025IrtLac
            }));
            Assert.That(notes, Has.All.Contains("does not add its specialized modifications"));
        });
    }

    [Test]
    public static void PrositHcdUsesAnUnrestrictedCollisionEnergyAndNoOtherOptionalInputs()
    {
        var definition = KoinaModelCatalog.FragmentIntensity(FragmentIntensityPredictionModel.Prosit2020IntensityHcd);
        var inputs = FragmentIntensityInputOptions.From(definition.Create(
            SequenceConversionHandlingMode.ReturnNull,
            IncompatibleParameterHandlingMode.ReturnNull,
            FragmentIonMappingMode.MapToInputFullSequence));

        Assert.Multiple(() =>
        {
            Assert.That(inputs.AllowedPrecursorCharges, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(inputs.CollisionEnergies.IsApplicable, Is.True);
            Assert.That(inputs.CollisionEnergies.IsRestricted, Is.False);
            Assert.That(inputs.InstrumentTypes.IsApplicable, Is.False);
            Assert.That(inputs.FragmentationTypes.IsApplicable, Is.False);
        });
    }

    [Test]
    public static void Ms2PipHcdHidesCollisionEnergy()
    {
        var definition = KoinaModelCatalog.FragmentIntensity(FragmentIntensityPredictionModel.Ms2PipHcd2021);
        var inputs = FragmentIntensityInputOptions.From(definition.Create(
            SequenceConversionHandlingMode.ReturnNull,
            IncompatibleParameterHandlingMode.ReturnNull,
            FragmentIonMappingMode.MapToInputFullSequence));

        Assert.That(inputs.CollisionEnergies.IsApplicable, Is.False);
    }

    [Test]
    public static void AltimeterOffersChargeSevenAndItsFiniteCollisionEnergyRange()
    {
        var definition = KoinaModelCatalog.FragmentIntensity(FragmentIntensityPredictionModel.Altimeter2024Intensities);
        var inputs = FragmentIntensityInputOptions.From(definition.Create(
            SequenceConversionHandlingMode.ReturnNull,
            IncompatibleParameterHandlingMode.ReturnNull,
            FragmentIonMappingMode.MapToInputFullSequence));

        Assert.Multiple(() =>
        {
            Assert.That(inputs.AllowedPrecursorCharges, Does.Contain(7));
            Assert.That(inputs.CollisionEnergies.IsRestricted, Is.True);
            Assert.That(inputs.CollisionEnergies.AllowedValues, Is.EquivalentTo(Enumerable.Range(20, 21)));
        });
    }

    [Test]
    public static void LactylationModelShowsEveryInputItRequires()
    {
        var definition = KoinaModelCatalog.FragmentIntensity(FragmentIntensityPredictionModel.Prosit2025IntensityLac);
        var inputs = FragmentIntensityInputOptions.From(definition.Create(
            SequenceConversionHandlingMode.ReturnNull,
            IncompatibleParameterHandlingMode.ReturnNull,
            FragmentIonMappingMode.MapToInputFullSequence));

        Assert.Multiple(() =>
        {
            Assert.That(inputs.CollisionEnergies.IsApplicable, Is.True);
            Assert.That(inputs.InstrumentTypes.AllowedValues, Is.EquivalentTo(new[] { "ECLIPSE", "ASTRAL", "LUMOS" }));
            Assert.That(inputs.FragmentationTypes.AllowedValues, Is.EquivalentTo(new[] { "HCD", "CID" }));
        });
    }
}
