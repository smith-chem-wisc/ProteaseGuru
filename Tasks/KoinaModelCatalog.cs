using Omics.SequenceConversion;
using PredictionClients.Koina.AbstractClasses;
using PredictionClients.Koina.SupportedModels.FragmentIntensityModels;
using PredictionClients.Koina.SupportedModels.RetentionTimeModels;
using PredictionClients.Koina.Util;

namespace ProteaseGuru.Tasks;

/// <summary>
/// The peptide fragment-intensity models offered by the spectral-library exporter.
/// Models that require modifications ProteaseGuru cannot generate are intentionally excluded.
/// UniSpec is also excluded until its instrument-dependent charge constraints can be represented
/// in the UI without duplicating model-specific validation in ProteaseGuru.
/// </summary>
public enum FragmentIntensityPredictionModel
{
    AlphaPeptDeepMs2Generic,
    Altimeter2024Intensities,
    Ms2PipHcd2021,
    Ms2PipImmunoHcd,
    Ms2PipTimsTof2023,
    Ms2PipTimsTof2024,
    Ms2PipTtof5600,
    Prosit2019Intensity,
    Prosit2020IntensityCid,
    Prosit2020IntensityHcd,
    Prosit2023IntensityTimsTof,
    Prosit2024IntensityCit,
    Prosit2024IntensityPtmsGl,
    Prosit2025Intensity22Ptm,
    Prosit2025Intensity40Ptm,
    Prosit2025IntensityLac,
    Prosit2025IntensityPtm2
}

public enum RetentionTimePredictionModel
{
    AlphaPeptDeepRtGeneric,
    ChronologerRt,
    DeepLcHelaHf,
    Prosit2019Irt,
    Prosit2024IrtCit,
    Prosit2024IrtPtmsGl,
    Prosit2025Irt22Ptm,
    Prosit2025Irt40Ptm,
    Prosit2025IrtLac
}

public sealed class FragmentIntensityModelDefinition
{
    private readonly Func<SequenceConversionHandlingMode, IncompatibleParameterHandlingMode,
        FragmentIonMappingMode, FragmentIntensityModel> _factory;

    internal FragmentIntensityModelDefinition(
        FragmentIntensityPredictionModel id,
        string displayName,
        Func<SequenceConversionHandlingMode, IncompatibleParameterHandlingMode,
            FragmentIonMappingMode, FragmentIntensityModel> factory,
        string? inputScopeNote)
    {
        Id = id;
        DisplayName = displayName;
        InputScopeNote = inputScopeNote;
        _factory = factory;
    }

    public FragmentIntensityPredictionModel Id { get; }
    public string DisplayName { get; }
    public string? InputScopeNote { get; }

    public FragmentIntensityModel Create(
        SequenceConversionHandlingMode modHandlingMode,
        IncompatibleParameterHandlingMode parameterHandlingMode,
        FragmentIonMappingMode fragmentIonMappingMode) =>
        _factory(modHandlingMode, parameterHandlingMode, fragmentIonMappingMode);

    public override string ToString() => DisplayName;
}

public sealed class RetentionTimeModelDefinition
{
    private readonly Func<SequenceConversionHandlingMode, RetentionTimeModel> _factory;

    internal RetentionTimeModelDefinition(
        RetentionTimePredictionModel id,
        string displayName,
        Func<SequenceConversionHandlingMode, RetentionTimeModel> factory,
        string? inputScopeNote)
    {
        Id = id;
        DisplayName = displayName;
        InputScopeNote = inputScopeNote;
        _factory = factory;
    }

    public RetentionTimePredictionModel Id { get; }
    public string DisplayName { get; }
    public string? InputScopeNote { get; }

    public RetentionTimeModel Create(SequenceConversionHandlingMode modHandlingMode) =>
        _factory(modHandlingMode);

    public override string ToString() => DisplayName;
}

/// <summary>
/// Describes one optional model input. A null Allowed* collection means the input is not used;
/// an empty collection means it is required but unrestricted; a populated collection is the
/// finite set the UI should offer.
/// </summary>
public sealed record KoinaInputDomain<T>(bool IsApplicable, IReadOnlyList<T> AllowedValues)
{
    public bool IsRestricted => AllowedValues.Count > 0;

    internal static KoinaInputDomain<T> From(IReadOnlyCollection<T>? allowedValues) =>
        allowedValues == null
            ? new KoinaInputDomain<T>(false, Array.Empty<T>())
            : new KoinaInputDomain<T>(true, allowedValues.ToArray());
}

public sealed record FragmentIntensityInputOptions(
    IReadOnlyList<int> AllowedPrecursorCharges,
    KoinaInputDomain<int> CollisionEnergies,
    KoinaInputDomain<string> InstrumentTypes,
    KoinaInputDomain<string> FragmentationTypes)
{
    public static FragmentIntensityInputOptions From(FragmentIntensityModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new FragmentIntensityInputOptions(
            model.AllowedPrecursorCharges.OrderBy(charge => charge).ToArray(),
            KoinaInputDomain<int>.From(model.AllowedCollisionEnergies),
            KoinaInputDomain<string>.From(model.AllowedInstrumentTypes),
            KoinaInputDomain<string>.From(model.AllowedFragmentationTypes));
    }
}

/// <summary>
/// The single source of truth for models ProteaseGuru exposes. mzLib remains authoritative for
/// sequence and parameter constraints; definitions here only name and construct supported models.
/// </summary>
public static class KoinaModelCatalog
{
    private const string PtmFocusedInputScopeNote =
        "PTM-focused model. ProteaseGuru submits peptides as generated by the selected digestion " +
        "settings; selecting this model does not add its specialized modifications.";

    public static IReadOnlyList<FragmentIntensityModelDefinition> FragmentIntensityModels { get; } =
    [
        Fragment(FragmentIntensityPredictionModel.AlphaPeptDeepMs2Generic, "AlphaPeptDeep MS2 Generic", (m, p, f) => new AlphaPeptDeepMs2Generic(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Altimeter2024Intensities, "Altimeter 2024 Intensities", (m, p, f) => new Altimeter2024Intensities(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Ms2PipHcd2021, "MS2PIP HCD 2021", (m, p, f) => new Ms2PipHCD2021(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Ms2PipImmunoHcd, "MS2PIP Immuno HCD", (m, p, f) => new Ms2PipImmunoHCD(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Ms2PipTimsTof2023, "MS2PIP timsTOF 2023", (m, p, f) => new Ms2PipTimsTOF2023(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Ms2PipTimsTof2024, "MS2PIP timsTOF 2024", (m, p, f) => new Ms2PipTimsTOF2024(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Ms2PipTtof5600, "MS2PIP TTOF 5600", (m, p, f) => new Ms2PipTTOF5600(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Prosit2019Intensity, "Prosit 2019 Intensity", (m, p, f) => new Prosit2019Intensity(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Prosit2020IntensityCid, "Prosit 2020 Intensity CID", (m, p, f) => new Prosit2020IntensityCID(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Prosit2020IntensityHcd, "Prosit 2020 Intensity HCD", (m, p, f) => new Prosit2020IntensityHCD(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Prosit2023IntensityTimsTof, "Prosit 2023 Intensity timsTOF", (m, p, f) => new Prosit2023IntensityTimsTOF(m, p, f)),
        Fragment(FragmentIntensityPredictionModel.Prosit2024IntensityCit, "Prosit 2024 Intensity CIT", (m, p, f) => new Prosit2024IntensityCit(m, p, f), PtmFocusedInputScopeNote),
        Fragment(FragmentIntensityPredictionModel.Prosit2024IntensityPtmsGl, "Prosit 2024 Intensity PTMs GL", (m, p, f) => new Prosit2024IntensityPTMsGl(m, p, f), PtmFocusedInputScopeNote),
        Fragment(FragmentIntensityPredictionModel.Prosit2025Intensity22Ptm, "Prosit 2025 Intensity 22 PTM", (m, p, f) => new Prosit2025Intensity22PTM(m, p, f), PtmFocusedInputScopeNote),
        Fragment(FragmentIntensityPredictionModel.Prosit2025Intensity40Ptm, "Prosit 2025 Intensity 40 PTM", (m, p, f) => new Prosit2025Intensity40PTM(m, p, f), PtmFocusedInputScopeNote),
        Fragment(FragmentIntensityPredictionModel.Prosit2025IntensityLac, "Prosit 2025 Intensity Lactylation", (m, p, f) => new Prosit2025IntensityLac(m, p, f), PtmFocusedInputScopeNote),
        Fragment(FragmentIntensityPredictionModel.Prosit2025IntensityPtm2, "Prosit 2025 Intensity PTM2", (m, p, f) => new Prosit2025IntensityPtm2(m, p, f), PtmFocusedInputScopeNote)
    ];

    public static IReadOnlyList<RetentionTimeModelDefinition> RetentionTimeModels { get; } =
    [
        Retention(RetentionTimePredictionModel.AlphaPeptDeepRtGeneric, "AlphaPeptDeep RT Generic", m => new AlphaPeptDeepRTGeneric(m)),
        Retention(RetentionTimePredictionModel.ChronologerRt, "Chronologer RT", m => new ChronologerRT(m)),
        Retention(RetentionTimePredictionModel.DeepLcHelaHf, "DeepLC HeLa HF", m => new DeeplcHelaHf(m)),
        Retention(RetentionTimePredictionModel.Prosit2019Irt, "Prosit 2019 iRT", m => new Prosit2019iRT(m)),
        Retention(RetentionTimePredictionModel.Prosit2024IrtCit, "Prosit 2024 iRT CIT", m => new Prosit2024iRTCit(m), PtmFocusedInputScopeNote),
        Retention(RetentionTimePredictionModel.Prosit2024IrtPtmsGl, "Prosit 2024 iRT PTMs GL", m => new Prosit2024iRTPTMsGl(m), PtmFocusedInputScopeNote),
        Retention(RetentionTimePredictionModel.Prosit2025Irt22Ptm, "Prosit 2025 iRT 22 PTM", m => new Prosit2025iRT22PTM(m), PtmFocusedInputScopeNote),
        Retention(RetentionTimePredictionModel.Prosit2025Irt40Ptm, "Prosit 2025 iRT 40 PTM", m => new Prosit2025iRT40PTM(m), PtmFocusedInputScopeNote),
        Retention(RetentionTimePredictionModel.Prosit2025IrtLac, "Prosit 2025 iRT Lactylation", m => new Prosit2025iRTLac(m), PtmFocusedInputScopeNote)
    ];

    public static FragmentIntensityModelDefinition FragmentIntensity(FragmentIntensityPredictionModel id) =>
        FragmentIntensityModels.SingleOrDefault(definition => definition.Id == id)
        ?? throw new NotSupportedException($"No fragment-intensity model is registered for {id}.");

    public static RetentionTimeModelDefinition RetentionTime(RetentionTimePredictionModel id) =>
        RetentionTimeModels.SingleOrDefault(definition => definition.Id == id)
        ?? throw new NotSupportedException($"No retention-time model is registered for {id}.");

    private static FragmentIntensityModelDefinition Fragment(
        FragmentIntensityPredictionModel id,
        string displayName,
        Func<SequenceConversionHandlingMode, IncompatibleParameterHandlingMode,
            FragmentIonMappingMode, FragmentIntensityModel> factory,
        string? inputScopeNote = null) =>
        new(id, displayName, factory, inputScopeNote);

    private static RetentionTimeModelDefinition Retention(
        RetentionTimePredictionModel id,
        string displayName,
        Func<SequenceConversionHandlingMode, RetentionTimeModel> factory,
        string? inputScopeNote = null) =>
        new(id, displayName, factory, inputScopeNote);
}
