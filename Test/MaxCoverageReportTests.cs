using System.Globalization;
using NUnit.Framework;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Test;

[TestFixture]
public class MaxCoverageReportTests
{
    // Human hemoglobin beta chain, as in MaxCoverageTests
    private const string SampleSequence = "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";
    private const string SummaryHeader = "Protein\tLength\tMode\tRank\tProteases\tCoverage Fraction\tUncovered Residues\tOther Tied Sets\tTied Combinations";

    private Protein _protein = null!;
    private List<ProteaseSpecificParameters> _proteaseParams = null!;
    private SeekMaximumCoverage _seeker = null!;

    [SetUp]
    public void SetUp()
    {
        _protein = new Protein(SampleSequence, "HBB_HUMAN", name: "HBB_HUMAN", fullName: "Hemoglobin subunit beta");
        _proteaseParams = new[] { "trypsin|P", "chymotrypsin|P", "Asp-N", "Glu-C" }
            .Select(name => new ProteaseSpecificParameters(
                new DigestionParams(protease: name, maxMissedCleavages: 2, minPeptideLength: 7, maxPeptideLength: 50)))
            .ToList();
        _seeker = new SeekMaximumCoverage();
    }

    private MaxCoverageReport Report(List<ProteaseSpecificParameters> proteases)
    {
        var coverage = _seeker.CalculateCoverageByProtease(_protein, proteases);
        return MaxCoverageReport.Compute(_seeker, _protein, proteases, coverage);
    }

    private static List<string[]> SummaryRows(MaxCoverageReport report)
    {
        using var writer = new StringWriter();
        report.WriteSummary(writer);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines[0], Is.EqualTo(SummaryHeader));
        return lines.Skip(1).Select(l => l.Split('\t')).ToList();
    }

    // Full coverage takes A + B + C + D, so the summary has Best 2 and Best 3 rows before the
    // Maximum Coverage Set, and A + B + C ties with A + B + D at three.
    private static Dictionary<string, HashSet<int>> CoverageWithFourProteaseMaximum() => new()
    {
        ["A"] = [0, 1, 2, 3, 4],
        ["B"] = [5, 6, 7],
        ["C"] = [8],
        ["D"] = [9],
        ["E"] = [0]
    };

    private static string Fraction(HashSet<int> covered, int length)
        => SeekMaximumCoverage.CoverageFraction(covered, length).ToString("F4", CultureInfo.InvariantCulture);

    [Test]
    public void FormatOneBasedRanges_CollapsesRunsAndShiftsToOneBased()
    {
        Assert.That(MaxCoverageReport.FormatOneBasedRanges([0, 1, 2, 6, 10, 11]), Is.EqualTo("1..3  7  11..12"));
    }

    [Test]
    public void FormatOneBasedRanges_WritesASingleRunThatExcelCannotReadAsADate()
    {
        Assert.That(MaxCoverageReport.FormatOneBasedRanges(Enumerable.Range(0, 7)), Is.EqualTo("1..7"));
    }

    [Test]
    public void FormatOneBasedRanges_EmptyIsNone()
    {
        Assert.That(MaxCoverageReport.FormatOneBasedRanges([]), Is.EqualTo("none"));
    }

    [Test]
    public void Summary_RunsFromTheSinglesThroughTheMaximumCoverageSetToAll()
    {
        var rows = SummaryRows(MaxCoverageReport.Compute(_seeker, _protein, [], CoverageWithFourProteaseMaximum()));

        Assert.That(rows.Select(r => r[2]), Is.EqualTo(
            Enumerable.Repeat("Single", 5).Concat(["Best 2", "Best 3", "Maximum Coverage Set", "All"])));
        Assert.That(rows.Select(r => r[3]), Is.EqualTo(new[] { "1", "2", "3", "4", "5", "1", "1", "1", "1" }));
        Assert.That(rows.Skip(5).Select(r => r[4]),
            Is.EqualTo(new[] { "A + B", "A + B + C", "A + B + C + D", "A + B + C + D + E" }));
        Assert.That(rows.All(r => r[0] == "HBB_HUMAN" && r[1] == SampleSequence.Length.ToString()));
    }

    [Test]
    public void Summary_OnlyTheRowsThatChooseASetCarryTies()
    {
        var rows = SummaryRows(MaxCoverageReport.Compute(_seeker, _protein, [], CoverageWithFourProteaseMaximum()));

        Assert.That(rows.Select(r => r[7]), Is.EqualTo(new[] { "0", "", "", "", "", "0", "1", "0", "" }));
        Assert.That(rows.Select(r => r[8]), Is.EqualTo(new[] { "", "", "", "", "", "", "A + B + D", "", "" }));
    }

    [Test]
    public void Summary_TiedCombinationsStopAtTheCapAndSayHowManyMore()
    {
        int proteases = SeekMaximumCoverage.MaxListedTiedSets + 2;
        var coverage = Enumerable.Range(0, proteases).ToDictionary(i => $"p{i:D2}", _ => new HashSet<int> { 0, 1 });
        var rows = SummaryRows(MaxCoverageReport.Compute(_seeker, _protein, [], coverage));

        string expected = string.Join("; ", Enumerable.Range(1, SeekMaximumCoverage.MaxListedTiedSets).Select(i => $"p{i:D2}"))
            + "; ... and 1 more";
        var rankOne = rows.First(r => r[2] == "Single");
        var maximum = rows.Single(r => r[2] == MaxCoverageReport.MaximumCoverageSetLabel);
        Assert.That(rankOne[7], Is.EqualTo((proteases - 1).ToString()));
        Assert.That(rankOne[8], Is.EqualTo(expected));
        Assert.That(maximum[7], Is.EqualTo((proteases - 1).ToString()));
        Assert.That(maximum[8], Is.EqualTo(expected));
    }

    [Test]
    public void Results_GetOneMapEachInRowOrder()
    {
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], CoverageWithFourProteaseMaximum());

        Assert.That(report.Results.Select(r => r.FileTag),
            Is.EqualTo(new[] { "Best1", "Best2", "Best3", "MaximumCoverageSet", "All" }));
        Assert.That(report.Results[0].Result.Proteases, Is.EqualTo(new[] { "A" }));
    }

    [Test]
    public void MapFiles_DrawEachDistinctSetOnce()
    {
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], CoverageWithFourProteaseMaximum());

        var files = report.MapFiles("MaxCoverage_X").ToList();

        Assert.That(files.Select(f => f.FileName), Is.EqualTo(new[]
        {
            "MaxCoverage_X_Best1.pdf", "MaxCoverage_X_Best2.pdf", "MaxCoverage_X_Best3.pdf",
            "MaxCoverage_X_MaximumCoverageSet.pdf", "MaxCoverage_X_All.pdf"
        }));
        Assert.That(files.Select(f => f.Result), Is.EqualTo(report.Results));
        Assert.That(files.Select(f => f.CopyOf), Has.All.Null);
    }

    [Test]
    public void MapFiles_CopyTheFirstMapOfTheSameProteasesInTheSameOrder()
    {
        var coverage = new Dictionary<string, HashSet<int>> { ["A"] = [0, 1, 2], ["B"] = [3, 4] };
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], coverage);

        var files = report.MapFiles("S").ToList();

        Assert.That(files.Select(f => (f.FileName, f.CopyOf)), Is.EqualTo(new (string, string?)[]
        {
            ("S_Best1.pdf", null),
            ("S_MaximumCoverageSet.pdf", null),
            ("S_All.pdf", "S_MaximumCoverageSet.pdf")
        }));
    }

    [Test]
    public void MapFiles_NameEveryModeTheMapShows()
    {
        var coverage = new Dictionary<string, HashSet<int>> { ["A"] = [0, 1, 2], ["B"] = [3, 4] };
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], coverage);

        var files = report.MapFiles("S").ToList();

        Assert.That(files[0].Modes, Is.EqualTo(new[] { "Best 1" }));
        Assert.That(files[1].Modes, Is.EqualTo(new[] { MaxCoverageReport.MaximumCoverageSetLabel, "All" }));
        Assert.That(files[2].Modes, Is.EqualTo(files[1].Modes));
    }

    [Test]
    public void MapFiles_WithOneProteaseCopyTheBest1Map()
    {
        var report = Report(_proteaseParams.Take(1).ToList());

        var files = report.MapFiles("S").ToList();

        Assert.That(files.Select(f => f.CopyOf), Is.EqualTo(new[] { null, "S_Best1.pdf", "S_Best1.pdf" }));
    }

    [Test]
    public void Summary_SingleRowsRankEveryProteaseByItsOwnCoverage()
    {
        var report = Report(_proteaseParams);
        var single = SummaryRows(report).Where(r => r[2] == "Single").ToList();

        Assert.That(single.Select(r => r[4]), Is.EquivalentTo(_proteaseParams.Select(p => p.DigestionAgentName)));
        foreach (var row in single)
            Assert.That(row[5], Is.EqualTo(Fraction(report.CoverageByProtease[row[4]], SampleSequence.Length)));
        var fractions = single.Select(r => double.Parse(r[5], CultureInfo.InvariantCulture)).ToList();
        Assert.That(fractions, Is.Ordered.Descending);
    }

    [Test]
    public void Summary_WithATwoProteaseMaximumHasNoBestRows()
    {
        var coverage = new Dictionary<string, HashSet<int>>
        {
            ["A"] = [0, 1],
            ["B"] = [2, 3],
            ["C"] = [0]
        };
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], coverage);
        var rows = SummaryRows(report);

        Assert.That(rows.Select(r => r[2]), Is.EqualTo(new[] { "Single", "Single", "Single", "Maximum Coverage Set", "All" }));
        Assert.That(rows[3][4], Is.EqualTo("A + B"));
        Assert.That(report.Results.Select(r => r.FileTag), Is.EqualTo(new[] { "Best1", "MaximumCoverageSet", "All" }));
    }

    [Test]
    public void Summary_SingleRankOneIsTheBestSingleProteaseEvenOnATie()
    {
        // Two proteases with the same coverage: the search keeps the first one in dictionary order.
        var tied = new Dictionary<string, HashSet<int>>
        {
            ["Lys-C|P"] = [0, 1, 2],
            ["trypsin|P"] = [3, 4, 5],
            ["Asp-N"] = [0]
        };
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], tied);
        var single = SummaryRows(report).Where(r => r[2] == "Single").ToList();

        Assert.That(report.Results[0].Result.Proteases, Is.EqualTo(new[] { "Lys-C|P" }));
        Assert.That(single.Select(r => r[4]), Is.EqualTo(new[] { "Lys-C|P", "trypsin|P", "Asp-N" }));
        Assert.That(single[0][7], Is.EqualTo("1"));
        Assert.That(single[0][8], Is.EqualTo("trypsin|P"));
    }

    [Test]
    public void Summary_UncoveredResiduesAreTheOneBasedComplement()
    {
        var coverage = new Dictionary<string, HashSet<int>> { ["trypsin|P"] = [.. Enumerable.Range(2, SampleSequence.Length - 4)] };
        var report = MaxCoverageReport.Compute(_seeker, _protein, [], coverage);
        var trypsin = SummaryRows(report).First(r => r[2] == "Single");

        // Residues 1-2 and the last two are uncovered.
        Assert.That(trypsin[6], Is.EqualTo($"1..2  {SampleSequence.Length - 1}..{SampleSequence.Length}"));
    }

    [Test]
    public void Summary_WithNoCoverageSaysNone()
    {
        var coverage = new Dictionary<string, HashSet<int>> { ["trypsin|P"] = [] };
        var rows = SummaryRows(MaxCoverageReport.Compute(_seeker, _protein, [], coverage));

        var maximum = rows.Single(r => r[2] == MaxCoverageReport.MaximumCoverageSetLabel);
        Assert.That(maximum[4], Is.EqualTo("none"));
        Assert.That(maximum[5], Is.EqualTo("0.0000"));
        Assert.That(maximum[6], Is.EqualTo($"1..{SampleSequence.Length}"));
        Assert.That(maximum[7], Is.EqualTo("0"));
        Assert.That(maximum[8], Is.Empty);
    }

    [Test]
    public void Summary_UsesInvariantDecimalsUnderCommaCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var rows = SummaryRows(Report(_proteaseParams));

            Assert.That(rows.Select(r => r[5]), Has.All.Match(@"^\d\.\d{4}$"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public void Summary_WithOneProteaseFillsEveryModeWithIt()
    {
        var rows = SummaryRows(Report(_proteaseParams.Take(1).ToList()));

        Assert.That(rows.Select(r => r[2]), Is.EqualTo(new[] { "Single", MaxCoverageReport.MaximumCoverageSetLabel, "All" }));
        Assert.That(rows.Select(r => r[4]), Has.All.EqualTo("trypsin|P"));
        Assert.That(rows.Select(r => r[5]).Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Parameters_ListMods()
    {
        var withMods = new ProteaseSpecificParameters(_proteaseParams[0].DigestionParams,
            [Mods.GetModification("Carbamidomethyl on C")],
            [Mods.GetModification("Oxidation on M"), Mods.GetModification("Phosphorylation on S")]);
        using var writer = new StringWriter();
        Report([withMods]).WriteParameters(writer);
        var row = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1].Split('\t');

        Assert.That(row[4], Is.EqualTo("Carbamidomethyl on C"));
        Assert.That(row[5], Is.EqualTo("Oxidation on M, Phosphorylation on S"));
    }

    [Test]
    public void NewExportFolder_ReplacesInvalidCharactersAndNumbersTakenNames()
    {
        string parent = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(parent);
        try
        {
            string first = MaxCoverageReport.NewExportFolder(parent, "sp|P69905|HBA:1");
            Assert.That(Path.GetFileName(first), Is.EqualTo("MaxCoverage_sp_P69905_HBA_1"));

            Directory.CreateDirectory(first);
            string second = MaxCoverageReport.NewExportFolder(parent, "sp|P69905|HBA:1");
            Assert.That(Path.GetFileName(second), Is.EqualTo("MaxCoverage_sp_P69905_HBA_1_2"));

            Directory.CreateDirectory(second);
            Assert.That(Path.GetFileName(MaxCoverageReport.NewExportFolder(parent, "sp|P69905|HBA:1")),
                Is.EqualTo("MaxCoverage_sp_P69905_HBA_1_3"));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Test]
    public void Parameters_ListEveryCheckedProtease()
    {
        using var writer = new StringWriter();
        Report(_proteaseParams).WriteParameters(writer);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[0], Is.EqualTo("Protease\tMax Missed Cleavages\tMin Length\tMax Length\tFixed Mods\tVariable Mods"));
        Assert.That(lines.Skip(1).Select(l => l.Split('\t')[0]), Is.EqualTo(_proteaseParams.Select(p => p.DigestionAgentName)));
        Assert.That(lines[1], Is.EqualTo("trypsin|P\t2\t7\t50\tnone\tnone"));
    }
}
