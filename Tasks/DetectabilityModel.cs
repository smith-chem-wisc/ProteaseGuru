using Omics.SequenceConversion;
using PredictionClients.Koina.SupportedModels.FlyabilityModels;

namespace ProteaseGuru.Tasks;

/// <summary>
/// PFly accepts no modifications at all, so it has to strip what it cannot represent and assess the
/// backbone. Rejecting instead would leave every modified peptide unassessed, and unassessed reads
/// as undetectable everywhere it is consumed. The digestion run and the export ask the same
/// question, so they build the model the same way.
/// </summary>
internal static class DetectabilityModel
{
    internal static PFly2024FineTuned Create() =>
        new(modHandlingMode: SequenceConversionHandlingMode.RemoveIncompatibleElements);
}
