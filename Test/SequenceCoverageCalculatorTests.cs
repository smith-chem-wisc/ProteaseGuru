using NUnit.Framework;
using Omics;
using Proteomics;
using Tasks;
using Tasks.CoverageMapConfiguration;

namespace Test
{
    /// <summary>
    /// Pins the behaviour SequenceCoverageCalculator was consolidated from, including the parts that are
    /// warts rather than intentions — an accession with no database entry is skipped, and a shared
    /// accession takes its length from whichever database was seen last. Both are load-bearing for the
    /// callers today, so they are asserted here rather than left to be rediscovered.
    /// </summary>
    [TestFixture]
    public class SequenceCoverageCalculatorTests
    {
        private static InSilicoPep Peptide(string accession, int start, int end, bool unique = true)
            => new InSilicoPep(
                baseSequence: "PEPTIDE", fullSequence: "PEPTIDE", previousAA: '-', nextAA: '-',
                unique: unique, hydrophobicity: 0, electrophoreticMobility: 0,
                chronologerRetentionTime: -1, pflyDetectability: null,
                length: end - start + 1, molecularWeight: 0,
                database: "db", protein: accession, proteinName: accession,
                start: start, end: end, protease: "trypsin");

        private static Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> OneDatabase(
            IBioPolymer protein, params InSilicoPep[] peptides)
            => new()
            {
                ["db"] = new()
                {
                    ["trypsin"] = new() { [protein] = peptides.ToList() }
                }
            };

        [Test]
        public void CoverageIsAPercentOfTheWholeProteinRoundedToTwoDecimals()
        {
            // 179 residues with 116 covered is the real O00762 case: 116/179*100 = 64.8044..., not 64.8044-ish
            // of the accession string, and not a 0-1 fraction.
            var protein = new Protein(new string('A', 179), "O00762");
            var result = SequenceCoverageCalculator.Calculate(OneDatabase(protein, Peptide("O00762", 1, 116)));

            Assert.That(result["trypsin"][protein].Total, Is.EqualTo(64.8));
        }

        [Test]
        public void UniqueCoverageCountsOnlyPeptidesFlaggedUnique()
        {
            var protein = new Protein(new string('A', 100), "P1");
            var result = SequenceCoverageCalculator.Calculate(OneDatabase(
                protein,
                Peptide("P1", 1, 40, unique: true),
                Peptide("P1", 41, 80, unique: false)));

            var coverage = result["trypsin"][protein];
            Assert.That(coverage.Total, Is.EqualTo(80.0), "both peptides count toward total coverage");
            Assert.That(coverage.Unique, Is.EqualTo(40.0), "only the unique peptide counts toward unique coverage");
        }

        [Test]
        public void OverlappingPeptidesDoNotDoubleCountResidues()
        {
            var protein = new Protein(new string('A', 100), "P1");
            var result = SequenceCoverageCalculator.Calculate(OneDatabase(
                protein,
                Peptide("P1", 1, 30),
                Peptide("P1", 20, 50)));

            Assert.That(result["trypsin"][protein].Total, Is.EqualTo(50.0));
        }

        [Test]
        public void ProteinWithNoPeptidesIsAbsentFromTheResult()
        {
            var protein = new Protein(new string('A', 100), "P1");
            var result = SequenceCoverageCalculator.Calculate(OneDatabase(protein));

            Assert.That(result["trypsin"], Is.Empty,
                "the result is built from the peptide side, so a protein with no peptides has no entry at all");
        }

        [Test]
        public void AccessionWithNoDatabaseEntryThrowsNamingTheAccession()
        {
            var protein = new Protein(new string('A', 100), "P1");
            // The peptide claims an accession no loaded database accounts for. Callers file peptides under
            // the same entries this map is built from, so reaching here means something upstream is wrong.
            var ex = Assert.Throws<KeyNotFoundException>(() =>
                SequenceCoverageCalculator.Calculate(OneDatabase(protein, Peptide("GHOST", 1, 50))));

            Assert.That(ex!.Message, Does.Contain("GHOST"));
            Assert.That(ex.Message, Does.Contain("trypsin"));
        }

        [Test]
        public void PeptidesForOneAccessionArePooledAcrossDatabases()
        {
            var protein = new Protein(new string('A', 100), "P1");
            var input = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>
            {
                ["dbA"] = new() { ["trypsin"] = new() { [protein] = new() { Peptide("P1", 1, 20) } } },
                ["dbB"] = new() { ["trypsin"] = new() { [protein] = new() { Peptide("P1", 21, 40) } } }
            };

            var result = SequenceCoverageCalculator.Calculate(input);

            Assert.That(result["trypsin"][protein].Total, Is.EqualTo(40.0),
                "peptides from both databases contribute to one coverage figure");
        }

        [Test]
        public void SharedAccessionTakesItsLengthFromTheLastDatabaseSeen()
        {
            // Two databases carry the same accession with different sequences, so they are distinct
            // IBioPolymer keys but collapse to one accession. The calculator's map is last-writer-wins,
            // so the second database supplies the denominator. Pinned because it is currently relied on,
            // not because it is obviously right — multi-database digestion makes this reachable.
            var shortForm = new Protein(new string('A', 100), "P1");
            var longForm = new Protein(new string('A', 200), "P1");

            var input = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>
            {
                ["dbA"] = new() { ["trypsin"] = new() { [shortForm] = new() { Peptide("P1", 1, 50) } } },
                ["dbB"] = new() { ["trypsin"] = new() { [longForm] = new() { Peptide("P1", 1, 50) } } }
            };

            var result = SequenceCoverageCalculator.Calculate(input);

            Assert.That(result["trypsin"].ContainsKey(longForm), Is.True, "the later entry is the one measured against");
            Assert.That(result["trypsin"].ContainsKey(shortForm), Is.False, "the earlier entry gets no coverage row");
            Assert.That(result["trypsin"][longForm].Total, Is.EqualTo(25.0), "50 residues out of the later entry's 200");
        }

        [Test]
        public void EachProteaseIsReportedSeparately()
        {
            var protein = new Protein(new string('A', 100), "P1");
            var trypticPeptide = Peptide("P1", 1, 30);
            var argCPeptide = new InSilicoPep(
                baseSequence: "PEPTIDE", fullSequence: "PEPTIDE", previousAA: '-', nextAA: '-',
                unique: true, hydrophobicity: 0, electrophoreticMobility: 0,
                chronologerRetentionTime: -1, pflyDetectability: null,
                length: 60, molecularWeight: 0, database: "db", protein: "P1", proteinName: "P1",
                start: 1, end: 60, protease: "Arg-C");

            var input = new Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>>
            {
                ["db"] = new()
                {
                    ["trypsin"] = new() { [protein] = new() { trypticPeptide } },
                    ["Arg-C"] = new() { [protein] = new() { argCPeptide } }
                }
            };

            var result = SequenceCoverageCalculator.Calculate(input);

            Assert.That(result["trypsin"][protein].Total, Is.EqualTo(30.0));
            Assert.That(result["Arg-C"][protein].Total, Is.EqualTo(60.0));
        }
    }
}
