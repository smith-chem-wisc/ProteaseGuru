namespace Tasks;

//digestion parameters provided by the user
public class RunParameters : ParameterBaseClass<RunParameters>, IEquatable<RunParameters>
{
    public string OutputFolder { get; set; } = string.Empty; // Not included in equality checks
    public bool TreatModifiedPeptidesAsDifferent { get; set; } = false;
    public int MinPeptideMassAllowed { get; set; } = -1;
    public int MaxPeptideMassAllowed { get; set; } = -1;
    public List<ProteaseSpecificParameters> ProteaseSpecificParameters { get; set; } = new();

    public RunParameters Clone()
    {
        return new RunParameters
        {
            OutputFolder = this.OutputFolder,
            TreatModifiedPeptidesAsDifferent = this.TreatModifiedPeptidesAsDifferent,
            MinPeptideMassAllowed = this.MinPeptideMassAllowed,
            MaxPeptideMassAllowed = this.MaxPeptideMassAllowed,
            ProteaseSpecificParameters = this.ProteaseSpecificParameters.Select(p => p.Clone()).ToList()
        };
    }

    public bool Equals(RunParameters? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TreatModifiedPeptidesAsDifferent == other.TreatModifiedPeptidesAsDifferent && MinPeptideMassAllowed == other.MinPeptideMassAllowed && MaxPeptideMassAllowed == other.MaxPeptideMassAllowed && ProteaseSpecificParameters.SequenceEqual(other.ProteaseSpecificParameters);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RunParameters)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TreatModifiedPeptidesAsDifferent, MinPeptideMassAllowed, MaxPeptideMassAllowed, ProteaseSpecificParameters);
    }
}
