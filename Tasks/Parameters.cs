using Omics.Modifications;
using Omics.Digestion;

namespace Tasks
{
    public class ProteaseSpecificParameters(
        IDigestionParams digestionParams,
        List<Modification>? fixedMods = null,
        List<Modification>? variableMods = null)
    {
        public string DigestionAgentName => DigestionParams.DigestionAgent.Name;
        public IDigestionParams DigestionParams { get; set; } = digestionParams;
        public List<Modification> FixedMods { get; set; } = fixedMods ?? new();
        public List<Modification> VariableMods { get; set; } = variableMods ?? new();
    }


    //digestion parameters provided by the user
    public class Parameters
    {
        public string OutputFolder = string.Empty;
        public bool TreatModifiedPeptidesAsDifferent { get; set; } = false;
        public int MinPeptideMassAllowed { get; set; } = -1;
        public int MaxPeptideMassAllowed { get; set; } = -1;
        public List<ProteaseSpecificParameters> ProteaseSpecificParameters { get; set; } = [];
    }
}
