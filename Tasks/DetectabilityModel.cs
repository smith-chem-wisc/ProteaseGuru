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
    // The public Koina server answers bursts above roughly 35 requests a second with 429, and one
    // refused request fails the whole prediction. 50 at once with a 1 s pause stays near 30 a second.
    internal static PFly2024FineTuned Create() =>
        new(modHandlingMode: SequenceConversionHandlingMode.RemoveIncompatibleElements,
            maxNumberOfBatchesPerRequest: 50,
            throttlingDelayInMilliseconds: 1000);
}
