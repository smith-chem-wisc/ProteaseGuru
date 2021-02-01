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
    internal static class DigestionTests
    {

            //[OneTimeSetUp]
            //public static void Setup()
            //{

            //}
            [Test]
        public static void SingleDatabase()
        {
            Loaders.LoadElements();
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
            Assert.AreEqual(1, digestionResults.PeptideByFile.Count);
            Assert.AreEqual(1, digestionResults.PeptideByFile.Values.Count);
            Assert.AreEqual(2, digestionResults.PeptideByFile[database.FileName][param.ProteasesForDigestion.First().Name].Count);
            foreach (var entry in digestionResults.PeptideByFile[database.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_1")
                {
                    Assert.AreEqual(28, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHYAVGAFNTNNLEWTR", entry.Value[2].BaseSequence);
                    Assert.IsTrue(entry.Value[2].Unique);
                    Assert.IsTrue(entry.Value[2].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("AILK", entry.Value[3].BaseSequence);
                    Assert.IsTrue(entry.Value[3].Unique);
                    Assert.IsTrue(entry.Value[3].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[5].BaseSequence);
                    Assert.IsFalse(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[6].BaseSequence);
                    Assert.IsFalse(entry.Value[6].Unique);
                    Assert.IsFalse(entry.Value[6].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[6].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[7].BaseSequence);
                    Assert.IsFalse(entry.Value[7].Unique);
                    Assert.IsFalse(entry.Value[7].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[7].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[8].BaseSequence);
                    Assert.IsFalse(entry.Value[8].Unique);
                    Assert.IsFalse(entry.Value[8].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[8].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEEEMR", entry.Value[9].BaseSequence);
                    Assert.IsTrue(entry.Value[9].Unique);
                    Assert.IsTrue(entry.Value[9].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[9].SeqOnlyInThisDb);


                }
                else if (entry.Key.Accession == "testProtein_2")
                {
                    Assert.AreEqual(29, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[2].BaseSequence);
                    Assert.IsFalse(entry.Value[2].Unique);
                    Assert.IsFalse(entry.Value[2].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHPPGAFNTNNLEWTR", entry.Value[23].BaseSequence);
                    Assert.IsTrue(entry.Value[23].Unique);
                    Assert.IsTrue(entry.Value[23].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[23].SeqOnlyInThisDb);

                    Assert.AreEqual("AIVK", entry.Value[24].BaseSequence);
                    Assert.IsTrue(entry.Value[24].Unique);
                    Assert.IsTrue(entry.Value[24].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[24].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[3].BaseSequence);
                    Assert.IsFalse(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[4].BaseSequence);
                    Assert.IsFalse(entry.Value[4].Unique);
                    Assert.IsFalse(entry.Value[4].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[4].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[5].BaseSequence);
                    Assert.IsFalse(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEPPMR", entry.Value[25].BaseSequence);
                    Assert.IsTrue(entry.Value[25].Unique);
                    Assert.IsTrue(entry.Value[25].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[25].SeqOnlyInThisDb);

                }
            }


            Directory.Delete(subFolder, true);
        }

        [Test]
        public static void MultipleDatabases()
        {
            Loaders.LoadElements();
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
            Assert.AreEqual(3, digestionResults.PeptideByFile.Count);
            Assert.AreEqual(3, digestionResults.PeptideByFile.Values.Count);
            Assert.AreEqual(2, digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name].Count);
            Assert.AreEqual(2, digestionResults.PeptideByFile[database2.FileName][param.ProteasesForDigestion.First().Name].Count);
            Assert.AreEqual(2, digestionResults.PeptideByFile[database3.FileName][param.ProteasesForDigestion.First().Name].Count);
            foreach (var entry in digestionResults.PeptideByFile[database1.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_1")
                {
                    Assert.AreEqual(28, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHYAVGAFNTNNLEWTR", entry.Value[2].BaseSequence);
                    Assert.IsTrue(entry.Value[2].Unique);
                    Assert.IsFalse(entry.Value[2].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("AILK", entry.Value[3].BaseSequence);
                    Assert.IsTrue(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[5].BaseSequence);
                    Assert.IsFalse(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[6].BaseSequence);
                    Assert.IsFalse(entry.Value[6].Unique);
                    Assert.IsFalse(entry.Value[6].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[6].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[7].BaseSequence);
                    Assert.IsFalse(entry.Value[7].Unique);
                    Assert.IsFalse(entry.Value[7].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[7].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[8].BaseSequence);
                    Assert.IsFalse(entry.Value[8].Unique);
                    Assert.IsFalse(entry.Value[8].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[8].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEEEMR", entry.Value[9].BaseSequence);
                    Assert.IsTrue(entry.Value[9].Unique);
                    Assert.IsFalse(entry.Value[9].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[9].SeqOnlyInThisDb);


                }
                else if (entry.Key.Accession == "testProtein_2")
                {
                    Assert.AreEqual(29, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[2].BaseSequence);
                    Assert.IsFalse(entry.Value[2].Unique);
                    Assert.IsFalse(entry.Value[2].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHPPGAFNTNNLEWTR", entry.Value[23].BaseSequence);
                    Assert.IsTrue(entry.Value[23].Unique);
                    Assert.IsFalse(entry.Value[23].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[23].SeqOnlyInThisDb);

                    Assert.AreEqual("AIVK", entry.Value[24].BaseSequence);
                    Assert.IsTrue(entry.Value[24].Unique);
                    Assert.IsTrue(entry.Value[24].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[24].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[3].BaseSequence);
                    Assert.IsFalse(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[4].BaseSequence);
                    Assert.IsFalse(entry.Value[4].Unique);
                    Assert.IsFalse(entry.Value[4].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[4].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[5].BaseSequence);
                    Assert.IsFalse(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEPPMR", entry.Value[25].BaseSequence);
                    Assert.IsTrue(entry.Value[25].Unique);
                    Assert.IsFalse(entry.Value[25].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[25].SeqOnlyInThisDb);

                }
            }

            foreach (var entry in digestionResults.PeptideByFile[database2.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_A")
                {
                    Assert.AreEqual(28, entry.Value.Count);
                    
                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHYAVGAFNTNNLEWTR", entry.Value[2].BaseSequence);
                    Assert.IsTrue(entry.Value[2].Unique);
                    Assert.IsFalse(entry.Value[2].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("AILK", entry.Value[3].BaseSequence);
                    Assert.IsFalse(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[4].BaseSequence);
                    Assert.IsFalse(entry.Value[4].Unique);
                    Assert.IsFalse(entry.Value[4].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[4].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[5].BaseSequence);
                    Assert.IsFalse(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[6].BaseSequence);
                    Assert.IsFalse(entry.Value[6].Unique);
                    Assert.IsFalse(entry.Value[6].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[6].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[7].BaseSequence);
                    Assert.IsFalse(entry.Value[7].Unique);
                    Assert.IsFalse(entry.Value[7].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[7].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEEEMR", entry.Value[8].BaseSequence);
                    Assert.IsTrue(entry.Value[8].Unique);
                    Assert.IsFalse(entry.Value[8].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[8].SeqOnlyInThisDb);


                }
                else if (entry.Key.Accession == "testProtein_B")
                {
                    Assert.AreEqual(29, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("AILK", entry.Value[2].BaseSequence);
                    Assert.IsFalse(entry.Value[2].Unique);
                    Assert.IsFalse(entry.Value[2].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[3].BaseSequence);
                    Assert.IsFalse(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHPPGAFNTNNLEWTR", entry.Value[25].BaseSequence);
                    Assert.IsTrue(entry.Value[25].Unique);
                    Assert.IsFalse(entry.Value[25].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[25].SeqOnlyInThisDb);                   

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[4].BaseSequence);
                    Assert.IsFalse(entry.Value[4].Unique);
                    Assert.IsFalse(entry.Value[4].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[4].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[5].BaseSequence);
                    Assert.IsFalse(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[6].BaseSequence);
                    Assert.IsFalse(entry.Value[6].Unique);
                    Assert.IsFalse(entry.Value[6].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[6].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEPPMR", entry.Value[26].BaseSequence);
                    Assert.IsTrue(entry.Value[26].Unique);
                    Assert.IsFalse(entry.Value[26].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[26].SeqOnlyInThisDb);

                }
            }

            foreach (var entry in digestionResults.PeptideByFile[database3.FileName][param.ProteasesForDigestion.First().Name])
            {
                if (entry.Key.Accession == "testProtein_one")
                {
                    Assert.AreEqual(28, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTAAR", entry.Value[0].BaseSequence);
                    Assert.IsTrue(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("SFVNGNEIFTAAR", entry.Value[1].BaseSequence);
                    Assert.IsTrue(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("MGHAVVGAFNTNNLEWTR", entry.Value[2].BaseSequence);
                    Assert.IsTrue(entry.Value[2].Unique);
                    Assert.IsTrue(entry.Value[2].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("AILK", entry.Value[3].BaseSequence);
                    Assert.IsFalse(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[4].BaseSequence);
                    Assert.IsFalse(entry.Value[4].Unique);
                    Assert.IsFalse(entry.Value[4].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[4].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAK", entry.Value[5].BaseSequence);
                    Assert.IsTrue(entry.Value[5].Unique);
                    Assert.IsFalse(entry.Value[5].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[5].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[6].BaseSequence);
                    Assert.IsFalse(entry.Value[6].Unique);
                    Assert.IsFalse(entry.Value[6].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[6].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[7].BaseSequence);
                    Assert.IsFalse(entry.Value[7].Unique);
                    Assert.IsFalse(entry.Value[7].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[7].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEEEMR", entry.Value[8].BaseSequence);
                    Assert.IsTrue(entry.Value[8].Unique);
                    Assert.IsFalse(entry.Value[8].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[8].SeqOnlyInThisDb);


                }
                else if (entry.Key.Accession == "testProtein_two")
                {
                    Assert.AreEqual(29, entry.Value.Count);

                    Assert.AreEqual("MSFVNGNEIFTQER", entry.Value[19].BaseSequence);
                    Assert.IsTrue(entry.Value[19].Unique);
                    Assert.IsTrue(entry.Value[19].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[19].SeqOnlyInThisDb);

                    Assert.AreEqual("QGHPPGAFNTNNLEWTR", entry.Value[21].BaseSequence);
                    Assert.IsTrue(entry.Value[21].Unique);
                    Assert.IsFalse(entry.Value[21].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[21].SeqOnlyInThisDb);

                    Assert.AreEqual("AILK", entry.Value[0].BaseSequence);
                    Assert.IsFalse(entry.Value[0].Unique);
                    Assert.IsFalse(entry.Value[0].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[0].SeqOnlyInThisDb);

                    Assert.AreEqual("AAQEK", entry.Value[1].BaseSequence);
                    Assert.IsFalse(entry.Value[1].Unique);
                    Assert.IsFalse(entry.Value[1].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[1].SeqOnlyInThisDb);

                    Assert.AreEqual("NTPVLIQVSMGAAVR", entry.Value[22].BaseSequence);
                    Assert.IsTrue(entry.Value[22].Unique);
                    Assert.IsTrue(entry.Value[22].UniqueAllDbs);
                    Assert.IsTrue(entry.Value[22].SeqOnlyInThisDb);

                    Assert.AreEqual("YMGDYK", entry.Value[2].BaseSequence);
                    Assert.IsFalse(entry.Value[2].Unique);
                    Assert.IsFalse(entry.Value[2].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[2].SeqOnlyInThisDb);

                    Assert.AreEqual("LVK", entry.Value[3].BaseSequence);
                    Assert.IsFalse(entry.Value[3].Unique);
                    Assert.IsFalse(entry.Value[3].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[3].SeqOnlyInThisDb);

                    Assert.AreEqual("TLVEPPMR", entry.Value[23].BaseSequence);
                    Assert.IsTrue(entry.Value[23].Unique);
                    Assert.IsFalse(entry.Value[23].UniqueAllDbs);
                    Assert.IsFalse(entry.Value[23].SeqOnlyInThisDb);

                }
            }


            Directory.Delete(subFolder, true);

        }
    }
}
