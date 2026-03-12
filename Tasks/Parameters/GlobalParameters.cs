namespace Tasks;
public class GlobalParameters : ParameterBaseClass<GlobalParameters>, IEquatable<GlobalParameters>
{
    public RunParameters RunParameters { get; set; } = new();
    public bool AskAboutSettingsChangeOnClose { get; set; } = true;
    public bool OverwriteSettingsWithoutAsking { get; set; } = false;

    public GlobalParameters Clone()
    {
        return new GlobalParameters
        {
            RunParameters = this.RunParameters.Clone()
        };
    }

    public bool Equals(GlobalParameters? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(RunParameters, other.RunParameters);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((GlobalParameters)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RunParameters);
    }
}
