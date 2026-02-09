using Engine;
using NUnit.Framework;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tasks;
using UsefulProteomicsDatabases;

namespace Test
{
    [TestFixture]
    public class DigestionTests
    {
        /// <summary>
        /// Helper method to find a peptide by its base sequence
        /// </summary>
        private static InSilicoPep GetPeptideBySequence(List<InSilicoPep> peptides, string baseSequence)
        {
            return peptides.FirstOrDefault(p => p.BaseSequence == baseSequence);
        }

        /// <summary>
        /// Helper method to assert peptide properties
        /// </summary>
        private static void AssertPeptideProperties(List<InSilicoPep> peptides, string baseSequence,
            bool expectedUnique, bool expectedUniqueAllDbs, bool expectedSeqOnlyInThisDb)
        {
            var peptide = GetPeptideBySequence(peptides, baseSequence);
            Assert.That(peptide, Is.Not.Null, $"Peptide with sequence {baseSequence} not found");
            Assert.That(peptide.Unique, Is.EqualTo(expectedUnique), $"Peptide {baseSequence} Unique mismatch");
            Assert.That(peptide.UniqueAllDbs, Is.EqualTo(expectedUniqueAllDbs), $"Peptide {baseSequence} UniqueAllDbs mismatch");
            Assert.That(peptide.SeqOnlyInThisDb, Is.EqualTo(expectedSeqOnlyInThisDb), $"Peptide {baseSequence} SeqOnlyInThisDb mismatch");
        }

        [Test]
        public static void SingleDatabase()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"DigestionTest");
            Directory.CreateDirectory(subFolder);

            string databasePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
            DbForDigestion database = new DbForDigestion(databasePath);

            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 1;
            param.MaxPeptideLengthAllowed = 100;
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin (cleave before proline)"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });

            Assert.That(digestionResults.PeptideByFile.Count, Is.EqualTo(1));
            Assert.That(digestionResults.PeptideByFile.Values.Count, Is.EqualTo(1));
            Assert.That(digestionResults.PeptideByFile[database.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));

            foreach (var entry in digestionResults.PeptideByFile[database.FileName][param.ProteasesForDigestion.First().Name])
            {
                var peptides = entry.Value;

                if (entry.Key.Accession == "testProtein_1")
                {
                    Assert.That(peptides.Count, Is.EqualTo(28));

                    // Shared peptides (found in multiple proteins within this database)
                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, true);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, true);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, true);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, true);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, true);
                    AssertPeptideProperties(peptides, "LVK", false, false, true);

                    // Unique peptides (only in testProtein_1)
                    AssertPeptideProperties(peptides, "QGHYAVGAFNTNNLEWTR", true, true, true);
                    AssertPeptideProperties(peptides, "AILK", true, true, true);
                    AssertPeptideProperties(peptides, "TLVEEEMR", true, true, true);
                }
                else if (entry.Key.Accession == "testProtein_2")
                {
                    Assert.That(peptides.Count, Is.EqualTo(29));

                    // Shared peptides
                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, true);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, true);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, true);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, true);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, true);
                    AssertPeptideProperties(peptides, "LVK", false, false, true);

                    // Unique peptides (only in testProtein_2)
                    AssertPeptideProperties(peptides, "QGHPPGAFNTNNLEWTR", true, true, true);
                    AssertPeptideProperties(peptides, "AIVK", true, true, true);
                    AssertPeptideProperties(peptides, "TLVEPPMR", true, true, true);
                }
            }

            Directory.Delete(subFolder, true);
        }

        [Test]
        public static void MultipleDatabases()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"DigestionTest");
            Directory.CreateDirectory(subFolder);

            string databasePath1 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
            DbForDigestion database1 = new DbForDigestion(databasePath1);

            string databasePath2 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_2.fasta");
            DbForDigestion database2 = new DbForDigestion(databasePath2);

            string databasePath3 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_3.fasta");
            DbForDigestion database3 = new DbForDigestion(databasePath3);

            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 1;
            param.MaxPeptideLengthAllowed = 100;
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin (cleave before proline)"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database1, database2, database3 });

            Assert.That(digestionResults.PeptideByFile.Count, Is.EqualTo(3));
            Assert.That(digestionResults.PeptideByFile.Values.Count, Is.EqualTo(3));
            Assert.That(digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));
            Assert.That(digestionResults.PeptideByFile[database2.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));
            Assert.That(digestionResults.PeptideByFile[database3.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));

            // Database 1 assertions
            foreach (var entry in digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name])
            {
                var peptides = entry.Value;

                if (entry.Key.Accession == "testProtein_1")
                {
                    Assert.That(peptides.Count, Is.EqualTo(28));

                    // Shared peptides (across databases, so SeqOnlyInThisDb = false)
                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, false);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, false);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, false);
                    AssertPeptideProperties(peptides, "LVK", false, false, false);

                    // Unique in this DB but shared across DBs
                    AssertPeptideProperties(peptides, "QGHYAVGAFNTNNLEWTR", true, false, false);
                    AssertPeptideProperties(peptides, "AILK", true, false, false);
                    AssertPeptideProperties(peptides, "TLVEEEMR", true, false, false);
                }
                else if (entry.Key.Accession == "testProtein_2")
                {
                    Assert.That(peptides.Count, Is.EqualTo(29));

                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, false);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, false);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, false);
                    AssertPeptideProperties(peptides, "LVK", false, false, false);

                    AssertPeptideProperties(peptides, "QGHPPGAFNTNNLEWTR", true, false, false);
                    AssertPeptideProperties(peptides, "AIVK", true, true, true); // Truly unique
                    AssertPeptideProperties(peptides, "TLVEPPMR", true, false, false);
                }
            }

            // Database 2 assertions
            foreach (var entry in digestionResults.PeptideByFile[database2.FileName][param.ProteasesForDigestion.First().Name])
            {
                var peptides = entry.Value;

                if (entry.Key.Accession == "testProtein_A")
                {
                    Assert.That(peptides.Count, Is.EqualTo(28));

                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "QGHYAVGAFNTNNLEWTR", true, false, false);
                    AssertPeptideProperties(peptides, "AILK", false, false, false);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, false);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, false);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, false);
                    AssertPeptideProperties(peptides, "LVK", false, false, false);
                    AssertPeptideProperties(peptides, "TLVEEEMR", true, false, false);
                }
                else if (entry.Key.Accession == "testProtein_B")
                {
                    Assert.That(peptides.Count, Is.EqualTo(29));

                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, false);
                    AssertPeptideProperties(peptides, "AILK", false, false, false);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, false);
                    AssertPeptideProperties(peptides, "QGHPPGAFNTNNLEWTR", true, false, false);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, false);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, false);
                    AssertPeptideProperties(peptides, "LVK", false, false, false);
                    AssertPeptideProperties(peptides, "TLVEPPMR", true, false, false);
                }
            }

            // Database 3 assertions
            foreach (var entry in digestionResults.PeptideByFile[database3.FileName][param.ProteasesForDigestion.First().Name])
            {
                var peptides = entry.Value;

                if (entry.Key.Accession == "testProtein_one")
                {
                    Assert.That(peptides.Count, Is.EqualTo(28));

                    AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", true, false, false);
                    AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", true, false, false);
                    AssertPeptideProperties(peptides, "MGHAVVGAFNTNNLEWTR", true, true, true); // Truly unique
                    AssertPeptideProperties(peptides, "AILK", false, false, false);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, false);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", true, false, false);
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, false);
                    AssertPeptideProperties(peptides, "LVK", false, false, false);
                    AssertPeptideProperties(peptides, "TLVEEEMR", true, false, false);
                }
                else if (entry.Key.Accession == "testProtein_two")
                {
                    Assert.That(peptides.Count, Is.EqualTo(29));

                    AssertPeptideProperties(peptides, "MSFVNGNEIFTQER", true, true, true); // Truly unique
                    AssertPeptideProperties(peptides, "QGHPPGAFNTNNLEWTR", true, false, false);
                    AssertPeptideProperties(peptides, "AILK", false, false, false);
                    AssertPeptideProperties(peptides, "AAQEK", false, false, false);
                    AssertPeptideProperties(peptides, "NTPVLIQVSMGAAVR", true, true, true); // Truly unique
                    AssertPeptideProperties(peptides, "YMGDYK", false, false, false);
                    AssertPeptideProperties(peptides, "LVK", false, false, false);
                    AssertPeptideProperties(peptides, "TLVEPPMR", true, false, false);
                }
            }

            Directory.Delete(subFolder, true);
        }

        [Test]
        public static void ProteaseModTest()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"DigestionTest");
            Directory.CreateDirectory(subFolder);

            string databasePath1 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "ProteaseModTest.fasta");
            DbForDigestion database1 = new DbForDigestion(databasePath1);

            var protDic = ProteaseDictionary.LoadProteaseDictionary(Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion", @"proteases.tsv"), GlobalVariables.ProteaseMods);

            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 1;
            param.MaxPeptideLengthAllowed = 100;
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(protDic["CNBr"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database1 });

            foreach (var entry in digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name])
            {
                var peptides = entry.Value;
                Assert.That(peptides.Count, Is.EqualTo(2));
                Assert.That(peptides[0].FullSequence, Is.Not.EqualTo(peptides[1].FullSequence));

                // Check that expected molecular weights are present (order independent)
                var weights = peptides.Select(p => p.MolecularWeight).OrderBy(w => w).ToList();
                Assert.That(weights[0], Is.EqualTo(882.39707781799996).Within(0.0001));
                Assert.That(weights[1], Is.EqualTo(930.400449121).Within(0.0001));
            }

            Directory.Delete(subFolder, true);
        }

        [Test]
        public static void InitiatorMethionineTest()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"DigestionTest");
            Directory.CreateDirectory(subFolder);

            string databasePath1 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
            DbForDigestion database1 = new DbForDigestion(databasePath1);

            string databasePath2 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_2.fasta");
            DbForDigestion database2 = new DbForDigestion(databasePath2);

            string databasePath3 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_3.fasta");
            DbForDigestion database3 = new DbForDigestion(databasePath3);

            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 1;
            param.MaxPeptideLengthAllowed = 100;
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin (cleave before proline)"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database1, database2, database3 });

            Assert.That(digestionResults.PeptideByFile.Count, Is.EqualTo(3));
            Assert.That(digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));
            Assert.That(digestionResults.PeptideByFile[database2.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));
            Assert.That(digestionResults.PeptideByFile[database3.FileName][param.ProteasesForDigestion.First().Name].Count, Is.EqualTo(2));

            // Helper to check initiator methionine cleavage
            void AssertInitiatorMethionine(List<InSilicoPep> peptides, string withMet, string withoutMet)
            {
                var peptideWithMet = peptides.FirstOrDefault(p => p.BaseSequence == withMet);
                var peptideWithoutMet = peptides.FirstOrDefault(p => p.BaseSequence == withoutMet);

                Assert.That(peptideWithMet, Is.Not.Null, $"Peptide {withMet} not found");
                Assert.That(peptideWithoutMet, Is.Not.Null, $"Peptide {withoutMet} not found");

                Assert.That(peptideWithMet.PreviousAA, Is.EqualTo('-'));
                Assert.That(peptideWithMet.StartResidue, Is.EqualTo(1));

                Assert.That(peptideWithoutMet.PreviousAA, Is.EqualTo('M'));
                Assert.That(peptideWithoutMet.StartResidue, Is.EqualTo(2));
            }

            // Database 1
            foreach (var entry in digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_1" || entry.Key.Accession == "testProtein_2")
                {
                    AssertInitiatorMethionine(entry.Value, "MSFVNGNEIFTAAR", "SFVNGNEIFTAAR");
                }
            }

            // Database 2
            foreach (var entry in digestionResults.PeptideByFile[database2.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_A" || entry.Key.Accession == "testProtein_B")
                {
                    AssertInitiatorMethionine(entry.Value, "MSFVNGNEIFTAAR", "SFVNGNEIFTAAR");
                }
            }

            // Database 3
            foreach (var entry in digestionResults.PeptideByFile[database3.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_one")
                {
                    AssertInitiatorMethionine(entry.Value, "MSFVNGNEIFTAAR", "SFVNGNEIFTAAR");
                }
                else if (entry.Key.Accession == "testProtein_two")
                {
                    AssertInitiatorMethionine(entry.Value, "MSFVNGNEIFTQER", "SFVNGNEIFTQER");
                }
            }

            Directory.Delete(subFolder, true);
        }
    }
}
