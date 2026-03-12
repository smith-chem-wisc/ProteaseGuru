using Nett;
using Omics.Digestion;
using Omics.Modifications;
using Proteomics.ProteolyticDigestion;

namespace Tasks;

[TreatAsInlineTable]
public class ProteaseSpecificParameters : IEquatable<ProteaseSpecificParameters>
{
    public ProteaseSpecificParameters()
    {
        DigestionParams = new DigestionParams();
        FixedMods = new();
        VariableMods = new();
    }

    public ProteaseSpecificParameters(IDigestionParams digestionParams,
        List<Modification>? fixedMods = null,
        List<Modification>? variableMods = null)
    {
        DigestionParams = digestionParams;
        FixedMods = fixedMods ?? new List<Modification>();
        VariableMods = variableMods ?? new List<Modification>();
    }

    public string DigestionAgentName => DigestionParams.DigestionAgent.Name;
    public IDigestionParams DigestionParams { get; set; }
    public List<Modification> FixedMods { get; set; }
    public List<Modification> VariableMods { get; set; }

    public ProteaseSpecificParameters Clone() => new(DigestionParams.Clone(), [..FixedMods], [..VariableMods]);

    public bool Equals(ProteaseSpecificParameters? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return DigestionParams.MinLength == other.DigestionParams.MinLength
            && DigestionParams.MaxLength == other.DigestionParams.MaxLength
            && DigestionParams.DigestionAgent.Name == other.DigestionParams.DigestionAgent.Name
            && DigestionParams.MaxMissedCleavages == other.DigestionParams.MaxMissedCleavages
            && FixedMods.SequenceEqual(other.FixedMods)
            && VariableMods.SequenceEqual(other.VariableMods);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ProteaseSpecificParameters)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DigestionParams.MinLength, DigestionParams.MaxLength, DigestionParams.DigestionAgent.Name, DigestionParams.MaxMissedCleavages, FixedMods, VariableMods);
    }
}
