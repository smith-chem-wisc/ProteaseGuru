using NUnit.Framework;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.IO;
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
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });
            Assert.AreEqual(1, digestionResults.PeptideByFile.Count);
            Assert.AreEqual(1, digestionResults.PeptideByFile.Values.Count);


            Directory.Delete(subFolder, true);
        }

        [Test]
        public static void MultipleDatabases()
        {
            Loaders.LoadElements();

        }
    }
}
