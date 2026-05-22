using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Omics.Digestion;

namespace Engine;
public static class MzLibExtensions
{
    /// <summary>
    /// Converts a list of <see cref="DigestionMotif"/> objects back to the comma-separated
    /// motif string notation used by <see cref="DigestionMotif.ParseDigestionMotifsFromString"/>.
    /// </summary>
    public static string ToMotifString(this IEnumerable<DigestionMotif> motifs)
    {
        return string.Join(",", motifs.Select(m => m.ToMotifString()));
    }

    /// <summary>
    /// Converts a single <see cref="DigestionMotif"/> back to its string notation, e.g. "KR|[P]{Q}".
    /// </summary>
    public static string ToMotifString(this DigestionMotif motif)
    {
        // Re-insert the cut index '|' into the inducing cleavage string
        string inducing = motif.InducingCleavage;
        StringBuilder sb = new StringBuilder(inducing);
        sb.Insert(motif.CutIndex, '|');

        // Replace bare X(s) with {ExcludeFromWildcard} if present
        if (motif.ExcludeFromWildcard != null)
            sb.Replace("X", "{" + motif.ExcludeFromWildcard + "}");

        // Append [PreventingCleavage] if present
        if (motif.PreventingCleavage != null)
            sb.Append("[" + motif.PreventingCleavage + "]");

        return sb.ToString();
    }
}
