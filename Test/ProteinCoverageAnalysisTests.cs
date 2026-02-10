using NUnit.Framework;
using Proteomics;
using Tasks;
using Tasks.ProteinCoverageAnalysis;

namespace Test
{
    [TestFixture]
    public class ProteinCoverageAnalysisTests
    {
        #region CoverageMapDataPreparer Tests

        [Test]
        public void SplitSequenceIntoLines_BasicSplit()
        {
            string sequence = "ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJ"; // 36 chars

            var result = CoverageMapDataPreparer.SplitSequenceIntoLines(sequence, 10);

            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(result[0], Is.EqualTo("ABCDEFGHIJ"));
            Assert.That(result[1], Is.EqualTo("KLMNOPQRST"));
            Assert.That(result[2], Is.EqualTo("UVWXYZABCD"));
            Assert.That(result[3], Is.EqualTo("EFGHIJ")); // Remainder
        }

        [Test]
        public void SplitSequenceIntoLines_ExactMultiple()
        {
            string sequence = "ABCDEFGHIJKLMNOPQRST"; // 20 chars

            var result = CoverageMapDataPreparer.SplitSequenceIntoLines(sequence, 10);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo("ABCDEFGHIJ"));
            Assert.That(result[1], Is.EqualTo("KLMNOPQRST"));
        }

        [Test]
        public void SplitSequenceIntoLines_EmptySequence()
        {
            var result = CoverageMapDataPreparer.SplitSequenceIntoLines("", 10);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SplitSequenceIntoLines_NullSequence()
        {
            var result = CoverageMapDataPreparer.SplitSequenceIntoLines(null, 10);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SplitSequenceIntoLines_DefaultResiduesPerLine()
        {
            string sequence = new string('A', 60);

            var result = CoverageMapDataPreparer.SplitSequenceIntoLines(sequence);

            Assert.That(result.Count, Is.EqualTo(3)); // 60 / 25 = 2.4, rounds to 3 lines
            Assert.That(result[0].Length, Is.EqualTo(25));
            Assert.That(result[1].Length, Is.EqualTo(25));
            Assert.That(result[2].Length, Is.EqualTo(10));
        }

        [Test]
        public void CheckPartialMatch_PeptideSpansLine()
        {
            // Peptide ends at residue 30, line is 25 chars, no previous residues
            int result = CoverageMapDataPreparer.CheckPartialMatch(30, 25, 0);

            Assert.That(result, Is.EqualTo(4)); // 30 - 0 - 25 - 1 = 4 remaining
        }

        [Test]
        public void CheckPartialMatch_PeptideFitsOnLine()
        {
            // Peptide ends at residue 20, line is 25 chars
            int result = CoverageMapDataPreparer.CheckPartialMatch(20, 25, 0);

            Assert.That(result, Is.EqualTo(-1)); // Fits on line
        }

        [Test]
        public void CheckPartialMatch_WithAccumulatedIndex()
        {
            // Peptide ends at residue 55, line is 25 chars, 25 previous residues
            int result = CoverageMapDataPreparer.CheckPartialMatch(55, 25, 25);

            Assert.That(result, Is.EqualTo(4)); // 55 - 25 - 25 - 1 = 4 remaining
        }

        #endregion

        #region CoverageMapConfiguration Tests

        [Test]
        public void ProteaseColorPalette_Has29Colors()
        {
            Assert.That(CoverageMapConfiguration.ProteaseColorPalette.Count, Is.EqualTo(29));
        }

        [Test]
        public void GetProteaseColor_ReturnsValidColor()
        {
            var color = CoverageMapConfiguration.GetProteaseColor(0);

            Assert.That(color.R, Is.EqualTo(130));
            Assert.That(color.G, Is.EqualTo(88));
            Assert.That(color.B, Is.EqualTo(159));
        }

        [Test]
        public void GetProteaseColor_WrapsAround()
        {
            var color0 = CoverageMapConfiguration.GetProteaseColor(0);
            var color29 = CoverageMapConfiguration.GetProteaseColor(29);

            Assert.That(color29.R, Is.EqualTo(color0.R));
            Assert.That(color29.G, Is.EqualTo(color0.G));
            Assert.That(color29.B, Is.EqualTo(color0.B));
        }

        [Test]
        public void CreateProteaseColorMap_AssignsUniqueColors()
        {
            var proteases = new[] { "trypsin", "chymotrypsin", "pepsin" };

            var colorMap = CoverageMapConfiguration.CreateProteaseColorMap(proteases);

            Assert.That(colorMap.Count, Is.EqualTo(3));
            Assert.That(colorMap.ContainsKey("trypsin"), Is.True);
            Assert.That(colorMap.ContainsKey("chymotrypsin"), Is.True);
            Assert.That(colorMap.ContainsKey("pepsin"), Is.True);

            // Each should have different colors
            Assert.That(colorMap["trypsin"], Is.Not.EqualTo(colorMap["chymotrypsin"]));
            Assert.That(colorMap["chymotrypsin"], Is.Not.EqualTo(colorMap["pepsin"]));
        }

        [Test]
        public void CreateProteaseColorMap_HandlesDuplicates()
        {
            var proteases = new[] { "trypsin", "trypsin", "pepsin" };

            var colorMap = CoverageMapConfiguration.CreateProteaseColorMap(proteases);

            Assert.That(colorMap.Count, Is.EqualTo(2)); // Only unique names
        }

        [Test]
        public void GetPtmName_KnownMass_ReturnsName()
        {
            var name = CoverageMapConfiguration.GetPtmName(79.9663);

            Assert.That(name, Is.EqualTo("Phosphorylation"));
        }

        [Test]
        public void GetPtmName_UnknownMass_ReturnsNull()
        {
            var name = CoverageMapConfiguration.GetPtmName(999.999);

            Assert.That(name, Is.Null);
        }

        [Test]
        public void GetPtmName_RoundsMass()
        {
            // Phosphorylation mass with slight variation
            var name = CoverageMapConfiguration.GetPtmName(79.96634);

            Assert.That(name, Is.EqualTo("Phosphorylation"));
        }

        [Test]
        public void GetPtmColor_KnownPtm_ReturnsCorrectColor()
        {
            var color = CoverageMapConfiguration.GetPtmColor("Phosphorylation");

            Assert.That(color.R, Is.EqualTo(127)); // Chartreuse
            Assert.That(color.G, Is.EqualTo(255));
            Assert.That(color.B, Is.EqualTo(0));
        }

        [Test]
        public void GetPtmColor_UnknownPtm_ReturnsDefaultOrange()
        {
            var color = CoverageMapConfiguration.GetPtmColor("UnknownModification");

            Assert.That(color.R, Is.EqualTo(255)); // Orange
            Assert.That(color.G, Is.EqualTo(165));
            Assert.That(color.B, Is.EqualTo(0));
        }

        [Test]
        public void GetPtmColor_NullPtm_ReturnsDefaultOrange()
        {
            var color = CoverageMapConfiguration.GetPtmColor(null);

            Assert.That(color.R, Is.EqualTo(255));
            Assert.That(color.G, Is.EqualTo(165));
            Assert.That(color.B, Is.EqualTo(0));
        }

        [Test]
        public void GetPtmColorByMass_KnownMass()
        {
            var color = CoverageMapConfiguration.GetPtmColorByMass(42.0106); // Acetylation

            Assert.That(color.R, Is.EqualTo(0)); // Aqua
            Assert.That(color.G, Is.EqualTo(255));
            Assert.That(color.B, Is.EqualTo(255));
        }

        [Test]
        public void GetPtmColorByMass_UnknownMass()
        {
            var color = CoverageMapConfiguration.GetPtmColorByMass(999.999);

            // Should return "Other" color (Orange)
            Assert.That(color.R, Is.EqualTo(255));
            Assert.That(color.G, Is.EqualTo(165));
            Assert.That(color.B, Is.EqualTo(0));
        }

        #endregion

        #region RgbColor Tests

        [Test]
        public void RgbColor_Constructor_SetsValues()
        {
            var color = new RgbColor(100, 150, 200);

            Assert.That(color.R, Is.EqualTo(100));
            Assert.That(color.G, Is.EqualTo(150));
            Assert.That(color.B, Is.EqualTo(200));
        }

        [Test]
        public void RgbColor_ToString_FormatsCorrectly()
        {
            var color = new RgbColor(100, 150, 200);

            Assert.That(color.ToString(), Is.EqualTo("RGB(100, 150, 200)"));
        }

        [Test]
        public void RgbColor_Equality()
        {
            var color1 = new RgbColor(100, 150, 200);
            var color2 = new RgbColor(100, 150, 200);
            var color3 = new RgbColor(100, 150, 201);

            Assert.That(color1, Is.EqualTo(color2));
            Assert.That(color1, Is.Not.EqualTo(color3));
        }

        #endregion

        #region ProteinCoverageResult Tests

        [Test]
        public void ProteinCoverageResult_Constructor_InitializesLists()
        {
            var protein = new Protein("PEPTIDE", "TestProtein");

            var result = new ProteinCoverageResult(protein);

            Assert.That(result.Protein, Is.EqualTo(protein));
            Assert.That(result.DisplayName, Is.EqualTo("TestProtein"));
            Assert.That(result.AllPeptides, Is.Not.Null);
            Assert.That(result.UniquePeptides, Is.Not.Null);
            Assert.That(result.SharedPeptides, Is.Not.Null);
            Assert.That(result.AllPeptides, Is.Empty);
        }

        [Test]
        public void ProteinCoverageResult_Constructor_UsesNameIfNoAccession()
        {
            var protein = new Protein("PEPTIDE", null, name: "ProteinName");

            var result = new ProteinCoverageResult(protein);

            Assert.That(result.DisplayName, Is.EqualTo("ProteinName"));
        }

        [Test]
        public void ProteinCoverageResult_Constructor_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ProteinCoverageResult(null));
        }

        [Test]
        public void ProteinCoverageResult_GetUniquePeptideCountsByProtease()
        {
            var protein = new Protein("PEPTIDE", "TestProtein");
            var result = new ProteinCoverageResult(protein);

            // Add mock peptides (would need InSilicoPep instances)
            // This test validates the method exists and returns a dictionary
            var counts = result.GetUniquePeptideCountsByProtease();

            Assert.That(counts, Is.Not.Null);
            Assert.That(counts, Is.Empty); // No peptides added
        }

        #endregion

        #region ProteinCoverageAnalyzer Tests

        [Test]
        public void ProteinCoverageAnalyzer_Constructor_ThrowsOnNullPeptideByFile()
        {
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>();

            Assert.Throws<ArgumentNullException>(() =>
                new ProteinCoverageAnalyzer(null, coverage));
        }

        [Test]
        public void ProteinCoverageAnalyzer_Constructor_ThrowsOnNullCoverage()
        {
            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();

            Assert.Throws<ArgumentNullException>(() =>
                new ProteinCoverageAnalyzer(peptideByFile, null));
        }

        [Test]
        public void ProteinCoverageAnalyzer_EmptyData_InitializesEmpty()
        {
            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>();

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            Assert.That(analyzer.ProteinAccessions, Is.Empty);
            Assert.That(analyzer.Proteases, Is.Empty);
            Assert.That(analyzer.ProteinCoverageResults, Is.Empty);
            Assert.That(analyzer.IsMultiDatabase, Is.False);
        }

        [Test]
        public void ProteinCoverageAnalyzer_SingleDatabase_IsMultiDatabaseFalse()
        {
            var protein = new Protein("PEPTIDE", "TestProtein");
            var peptides = new List<InSilicoPep>();

            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>
            {
                {
                    "Database1.fasta", new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>
                    {
                        {
                            "trypsin", new Dictionary<Protein, List<InSilicoPep>>
                            {
                                { protein, peptides }
                            }
                        }
                    }
                }
            };
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>
            {
                { "trypsin", new Dictionary<Protein, (double, double)> { { protein, (0.5, 0.3) } } }
            };

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            Assert.That(analyzer.IsMultiDatabase, Is.False);
            Assert.That(analyzer.ProteinAccessions.Count, Is.EqualTo(1));
            Assert.That(analyzer.Proteases.Count, Is.EqualTo(1));
        }

        [Test]
        public void ProteinCoverageAnalyzer_MultipleDatabases_IsMultiDatabaseTrue()
        {
            var protein1 = new Protein("PEPTIDE", "TestProtein1");
            var protein2 = new Protein("PEPTIDE", "TestProtein2");

            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>
            {
                {
                    "Database1.fasta", new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>
                    {
                        { "trypsin", new Dictionary<Protein, List<InSilicoPep>> { { protein1, new List<InSilicoPep>() } } }
                    }
                },
                {
                    "Database2.fasta", new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>
                    {
                        { "trypsin", new Dictionary<Protein, List<InSilicoPep>> { { protein2, new List<InSilicoPep>() } } }
                    }
                }
            };
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>
            {
                {
                    "trypsin", new Dictionary<Protein, (double, double)>
                    {
                        { protein1, (0.5, 0.3) },
                        { protein2, (0.6, 0.4) }
                    }
                }
            };

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            Assert.That(analyzer.IsMultiDatabase, Is.True);
        }

        [Test]
        public void ProteinCoverageAnalyzer_GetCoverageResultByAccession_Found()
        {
            var protein = new Protein("PEPTIDE", "TestProtein");

            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>
            {
                {
                    "Database1.fasta", new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>
                    {
                        { "trypsin", new Dictionary<Protein, List<InSilicoPep>> { { protein, new List<InSilicoPep>() } } }
                    }
                }
            };
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>
            {
                { "trypsin", new Dictionary<Protein, (double, double)> { { protein, (0.5, 0.3) } } }
            };

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            var result = analyzer.GetCoverageResultByAccession("TestProtein");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.DisplayName, Is.EqualTo("TestProtein"));
        }

        [Test]
        public void ProteinCoverageAnalyzer_GetCoverageResultByAccession_NotFound()
        {
            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>();
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>();

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            var result = analyzer.GetCoverageResultByAccession("NonExistent");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ProteinCoverageAnalyzer_GetPeptidesForProteinAndProtease_EmptyWhenNotFound()
        {
            var protein = new Protein("PEPTIDE", "TestProtein");

            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>
            {
                {
                    "Database1.fasta", new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>
                    {
                        { "trypsin", new Dictionary<Protein, List<InSilicoPep>> { { protein, new List<InSilicoPep>() } } }
                    }
                }
            };
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>
            {
                { "trypsin", new Dictionary<Protein, (double, double)> { { protein, (0.5, 0.3) } } }
            };

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            // Query for non-existent protease
            var peptides = analyzer.GetPeptidesForProteinAndProtease(protein, "chymotrypsin");

            Assert.That(peptides, Is.Empty);
        }

        [Test]
        public void ProteinCoverageAnalyzer_ProteinAccessions_AreSorted()
        {
            var proteinC = new Protein("PEPTIDE", "ProteinC");
            var proteinA = new Protein("PEPTIDE", "ProteinA");
            var proteinB = new Protein("PEPTIDE", "ProteinB");

            var peptideByFile = new Dictionary<string, Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>>
            {
                {
                    "Database1.fasta", new Dictionary<string, Dictionary<Protein, List<InSilicoPep>>>
                    {
                        {
                            "trypsin", new Dictionary<Protein, List<InSilicoPep>>
                            {
                                { proteinC, new List<InSilicoPep>() },
                                { proteinA, new List<InSilicoPep>() },
                                { proteinB, new List<InSilicoPep>() }
                            }
                        }
                    }
                }
            };
            var coverage = new Dictionary<string, Dictionary<Protein, (double, double)>>
            {
                {
                    "trypsin", new Dictionary<Protein, (double, double)>
                    {
                        { proteinC, (0.5, 0.3) },
                        { proteinA, (0.5, 0.3) },
                        { proteinB, (0.5, 0.3) }
                    }
                }
            };

            var analyzer = new ProteinCoverageAnalyzer(peptideByFile, coverage);

            Assert.That(analyzer.ProteinAccessions[0], Is.EqualTo("ProteinA"));
            Assert.That(analyzer.ProteinAccessions[1], Is.EqualTo("ProteinB"));
            Assert.That(analyzer.ProteinAccessions[2], Is.EqualTo("ProteinC"));
        }

        #endregion
    }
}
