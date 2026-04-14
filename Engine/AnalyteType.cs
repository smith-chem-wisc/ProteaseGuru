using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine;
public enum AnalyteType
{
    Peptide,
    Oligo
}

/// <summary>
/// Accessor methods for specific information about certain analyte types
/// </summary>
public static class AnalyteTypeExtensions
{
    private static readonly Dictionary<AnalyteType, AnalyteTypeData> AnalyteTypes = new()
            {
                { AnalyteType.Peptide, new AnalyteTypeData("Peptide", "Protein", "Protease") },
                { AnalyteType.Oligo, new AnalyteTypeData("Oligo", "Transcript", "RNase") },
            };

    public static string GetUniqueFormLabel(this AnalyteType analyteType) => AnalyteTypes[analyteType].UniqueFormLabel;
    public static string GetBioPolymerLabel(this AnalyteType analyteType) => AnalyteTypes[analyteType].BioPolymerLabel;
    public static string GetDigestionAgentLabel(this AnalyteType analyteType) => AnalyteTypes[analyteType].DigestionAgentLabel;
}

/// <summary>
/// Represents an analyte type and is used to determine the output format of the analyte type.
/// </summary>
internal class AnalyteTypeData(
    string uniqueFormLabel,
    string bioPolymerLabel,
    string digestionAgentLabel)
{

    /// <summary>
    /// Gets the label for unique forms (e.g. Peptide).
    /// </summary>
    internal string UniqueFormLabel { get; init; } = uniqueFormLabel;

    /// <summary>
    /// Gets the label for grouped forms (e.g. Protein).
    /// </summary>
    internal string BioPolymerLabel { get; init; } = bioPolymerLabel;

    /// <summary>
    /// Gets the label for the digestion agent class (e.g. Protease)
    /// </summary>
    internal string DigestionAgentLabel { get; init; } = digestionAgentLabel;
}
