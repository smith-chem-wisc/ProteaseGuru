using System.Globalization;
using Omics;

namespace ProteaseGuru.Tasks;

/// <summary>
/// The maximum-coverage analysis of one protein, written as two tab-separated tables: a summary with
/// one row per result, and the digestion settings of each checked protease.
/// </summary>
/// <param name="CoverageByProtease">0-based covered residue indices per checked protease.</param>
/// <param name="Results">
/// Best 1, Best 2 up to one short of the Maximum Coverage Set size, the Maximum Coverage Set, and All,
/// in that order; each one gets a map.
/// </param>
public record MaxCoverageReport(
    IBioPolymer Protein,
    IReadOnlyList<ProteaseSpecificParameters> CheckedProteases,
    IReadOnlyDictionary<string, HashSet<int>> CoverageByProtease,
    IReadOnlyList<MaxCoverageReport.LabeledResult> Results)
{
    /// <param name="Label">The Mode column's text.</param>
    /// <param name="FileTag">The map PDF's file name suffix.</param>
    public record LabeledResult(string Label, string FileTag, SeekMaximumCoverage.CombinationResult Result);

    /// <param name="CopyOf">The file of an earlier result with the same proteases in the same order, or null.</param>
    public record MapFile(LabeledResult Result, string FileName, string? CopyOf);

    public const string MaximumCoverageSetLabel = "Maximum Coverage Set";

    public static MaxCoverageReport Compute(SeekMaximumCoverage seeker, IBioPolymer protein,
        IReadOnlyList<ProteaseSpecificParameters> checkedProteases, Dictionary<string, HashSet<int>> coverageByProtease)
    {
        int length = protein.Length;
        var maximumCoverageSet = seeker.MaximumCoverageSet(coverageByProtease, length);

        var results = new List<LabeledResult> { new("Best 1", "Best1", seeker.BestCombination(coverageByProtease, 1, length)) };
        for (int size = 2; size < maximumCoverageSet.Proteases.Count; size++)
            results.Add(new($"Best {size}", $"Best{size}", seeker.BestCombination(coverageByProtease, size, length)));
        results.Add(new(MaximumCoverageSetLabel, "MaximumCoverageSet", maximumCoverageSet));
        results.Add(new("All", "All", seeker.ForMode(MaxCoverageMode.All, coverageByProtease, length)));

        return new MaxCoverageReport(protein, checkedProteases, coverageByProtease, results);
    }

    /// <summary>
    /// Writes the Single rows, every protease ranked by its own coverage, then a row for each result
    /// after Best 1, which is the rank 1 Single row. The rows that choose a set (Single rank 1 and
    /// Best 2 to the Maximum Coverage Set) also carry how many other sets of that size cover as many
    /// residues, and list up to <see cref="SeekMaximumCoverage.MaxListedTiedSets"/> of them.
    /// </summary>
    public void WriteSummary(TextWriter writer)
    {
        writer.WriteLine("Protein\tLength\tMode\tRank\tProteases\tCoverage Fraction\tUncovered Residues\tOther Tied Sets\tTied Combinations");

        // Ranked in the coverage dictionary's order on ties, which is how the search breaks them,
        // so rank 1 is the protease the Best 1 map shows.
        int rank = 1;
        foreach (var (protease, covered) in CoverageByProtease.OrderByDescending(kvp => kvp.Value.Count))
        {
            WriteRow(writer, "Single", rank, [protease], covered, rank == 1 ? Results[0].Result : null);
            rank++;
        }

        foreach (var (label, _, result) in Results.Skip(1).SkipLast(1))
            WriteRow(writer, label, 1, result.Proteases, result.CoveredResidues, result);

        var all = Results[^1];
        WriteRow(writer, all.Label, 1, all.Result.Proteases, all.Result.CoveredResidues, null);
    }

    /// <summary>
    /// One map PDF per result, in <see cref="Results"/> order, named <paramref name="stem"/>_&lt;FileTag&gt;.pdf.
    /// Results often pick the same proteases, so a result whose map was already drawn names that file
    /// in <see cref="MapFile.CopyOf"/>, to be copied rather than drawn again.
    /// </summary>
    public IEnumerable<MapFile> MapFiles(string stem)
    {
        var fileByProteases = new Dictionary<string, string>();
        foreach (var result in Results)
        {
            string fileName = $"{stem}_{result.FileTag}.pdf";
            string proteases = string.Join(" + ", result.Result.Proteases);
            if (fileByProteases.TryGetValue(proteases, out var drawn))
            {
                yield return new MapFile(result, fileName, drawn);
                continue;
            }

            fileByProteases[proteases] = fileName;
            yield return new MapFile(result, fileName, null);
        }
    }

    public void WriteParameters(TextWriter writer)
    {
        writer.WriteLine("Protease\tMax Missed Cleavages\tMin Length\tMax Length\tFixed Mods\tVariable Mods");
        foreach (var p in CheckedProteases)
        {
            writer.WriteLine(string.Join("\t",
                p.DigestionAgentName,
                p.DigestionParams.MaxMissedCleavages,
                p.DigestionParams.MinLength,
                p.DigestionParams.MaxLength,
                FormatMods(p.FixedMods.Select(m => m.IdWithMotif)),
                FormatMods(p.VariableMods.Select(m => m.IdWithMotif))));
        }
    }

    /// <summary>
    /// Returns a folder in <paramref name="parent"/> that doesn't exist yet: MaxCoverage_&lt;accession&gt;,
    /// numbered from _2 when that name is taken, so an earlier export is never overwritten.
    /// </summary>
    public static string NewExportFolder(string parent, string accession)
    {
        string stem = "MaxCoverage_" + string.Join("_", accession.Split(Path.GetInvalidFileNameChars()));
        string folder = Path.Combine(parent, stem);
        for (int n = 2; Directory.Exists(folder); n++)
            folder = Path.Combine(parent, $"{stem}_{n}");
        return folder;
    }

    /// <summary>
    /// Formats ascending, distinct 0-based residue indices as 1-based inclusive ranges, e.g. {0,1,2,6} → "1..3  7".
    /// The ".." keeps Excel from reading a lone range such as "1-7" as a date.
    /// </summary>
    public static string FormatOneBasedRanges(IEnumerable<int> zeroBasedIndices)
    {
        var sorted = zeroBasedIndices.ToList();
        if (sorted.Count == 0) return "none";

        var ranges = new List<string>();
        int runStart = sorted[0];
        for (int i = 1; i <= sorted.Count; i++)
        {
            if (i < sorted.Count && sorted[i] == sorted[i - 1] + 1) continue;
            int runEnd = sorted[i - 1];
            ranges.Add(runStart == runEnd ? $"{runStart + 1}" : $"{runStart + 1}..{runEnd + 1}");
            if (i < sorted.Count) runStart = sorted[i];
        }
        return string.Join("  ", ranges);
    }

    private void WriteRow(TextWriter writer, string mode, int rank, IReadOnlyList<string> proteases, HashSet<int> covered,
        SeekMaximumCoverage.CombinationResult? ties)
    {
        int length = Protein.Length;
        var uncovered = Enumerable.Range(0, length).Where(i => !covered.Contains(i));
        writer.WriteLine(string.Join("\t",
            Protein.Accession,
            length,
            mode,
            rank,
            proteases.Count > 0 ? string.Join(" + ", proteases) : "none",
            SeekMaximumCoverage.CoverageFraction(covered, length).ToString("F4", CultureInfo.InvariantCulture),
            FormatOneBasedRanges(uncovered),
            ties == null ? "" : OtherTiedSets(ties).ToString(CultureInfo.InvariantCulture),
            ties == null ? "" : FormatTiedSets(ties)));
    }

    /// <summary>
    /// The listed tied sets as "A + B; C + D", ending with "... and N more" when more tied than are listed.
    /// </summary>
    private static string FormatTiedSets(SeekMaximumCoverage.CombinationResult result)
    {
        var sets = result.TiedSets.Select(set => string.Join(" + ", set)).ToList();
        int unlisted = OtherTiedSets(result) - result.TiedSets.Count;
        if (unlisted > 0)
            sets.Add($"... and {unlisted} more");
        return string.Join("; ", sets);
    }

    // TiedSetCount includes the reported set; a search that reports no set counts 0.
    private static int OtherTiedSets(SeekMaximumCoverage.CombinationResult result)
        => Math.Max(result.TiedSetCount - 1, 0);

    private static string FormatMods(IEnumerable<string> ids)
    {
        var joined = string.Join(", ", ids);
        return joined.Length > 0 ? joined : "none";
    }
}
