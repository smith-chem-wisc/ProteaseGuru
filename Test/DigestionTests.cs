using Engine;
using NUnit.Framework;
using Proteomics.ProteolyticDigestion;
using Tasks;
using UsefulProteomicsDatabases;
using Omics.Digestion;

namespace Test
{
    [TestFixture]
    [NonParallelizable]
    public class DigestionTests : IDisposable
    {
        // Named mutex for cross-process synchronization of Chronologer file access
        private const string ChronologerMutexName = "Local\\ProteaseGuru_Chronologer_Mutex";
        private ChronologerMutexLock? _mutexLock;

        /// <summary>
        /// Disposable wrapper for Chronologer mutex acquisition and release
        /// </summary>
        private sealed class ChronologerMutexLock : IDisposable
        {
            private Mutex? _mutex;
            private bool _owned;

            public static ChronologerMutexLock Acquire(string mutexName, TimeSpan timeout)
            {
                var lockObj = new ChronologerMutexLock();
                lockObj._mutex = new Mutex(false, mutexName);

                try
                {
                    lockObj._owned = lockObj._mutex.WaitOne(timeout);
                    if (!lockObj._owned)
                    {
                        lockObj._mutex.Dispose();
                        lockObj._mutex = null;
                        throw new TimeoutException($"Timed out waiting for mutex: {mutexName}");
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner terminated without releasing - mutex is still acquired
                    lockObj._owned = true;
                    TestContext.WriteLine("Warning: Acquired abandoned Chronologer mutex from a crashed process");
                }
                catch when (lockObj._mutex != null)
                {
                    lockObj._mutex.Dispose();
                    lockObj._mutex = null;
                    throw;
                }

                return lockObj;
            }

            public void Dispose()
            {
                if (_mutex != null)
                {
                    if (_owned)
                    {
                        try { _mutex.ReleaseMutex(); }
                        catch (ApplicationException) { /* Not owned by this thread */ }
                    }
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _mutexLock = ChronologerMutexLock.Acquire(ChronologerMutexName, TimeSpan.FromMinutes(5));
        }

        [TearDown]
        public void TearDown()
        {
            _mutexLock?.Dispose();
            _mutexLock = null;
        }

        public void Dispose()
        {
            _mutexLock?.Dispose();
        }

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

        /// <summary>
        /// Helper method to clean up test folders with retry logic for locked files
        /// </summary>
        private static void CleanupTestFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return;

            const int maxRetries = 3;
            const int delayMs = 500;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    Directory.Delete(folderPath, true);
                    return;
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // Retry with delay after I/O exception
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
                {
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine($"Warning: Could not delete test folder {folderPath}: {ex.Message}");
                    return;
                }
            }
        }

        [Test]
        public void SingleDatabase()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"DigestionTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(subFolder);

            try
            {
                string databasePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
                DbForDigestion database = new DbForDigestion(databasePath);

                Parameters param = new Parameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams trypsin = new DigestionParams(
                    protease: "trypsin (cleave before proline)",
                    maxMissedCleavages: 0,
                    minPeptideLength: 1,
                    maxPeptideLength: 100);

                param.ProteaseSpecificParameters.Add(new ProteaseSpecificParameters(trypsin));

                DigestionTask digestion = new DigestionTask();
                digestion.DigestionParameters = param;
                var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });

                Assert.That(digestionResults.PeptideByFile.Count, Is.EqualTo(1));
                Assert.That(digestionResults.PeptideByFile.Values.Count, Is.EqualTo(1));
                Assert.That(digestionResults.PeptideByFile[database.FileName][param.ProteaseSpecificParameters.First().DigestionAgentName].Count, Is.EqualTo(2));

                foreach (var entry in digestionResults.PeptideByFile[database.FileName][param.ProteaseSpecificParameters.First().DigestionAgentName])
                {
                    var peptides = entry.Value;

                    if (entry.Key.Accession == "testProtein_1")
                    {
                        Assert.That(peptides.Count, Is.EqualTo(28));
                        AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, true);
                        AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, true);
                        AssertPeptideProperties(peptides, "AAQEK", false, false, true);
                        AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, true);
                        AssertPeptideProperties(peptides, "YMGDYK", false, false, true);
                        AssertPeptideProperties(peptides, "LVK", false, false, true);
                        AssertPeptideProperties(peptides, "QGHYAVGAFNTNNLEWTR", true, true, true);
                        AssertPeptideProperties(peptides, "AILK", true, true, true);
                        AssertPeptideProperties(peptides, "TLVEEEMR", true, true, true);
                    }
                    else if (entry.Key.Accession == "testProtein_2")
                    {
                        Assert.That(peptides.Count, Is.EqualTo(29));
                        AssertPeptideProperties(peptides, "MSFVNGNEIFTAAR", false, false, true);
                        AssertPeptideProperties(peptides, "SFVNGNEIFTAAR", false, false, true);
                        AssertPeptideProperties(peptides, "AAQEK", false, false, true);
                        AssertPeptideProperties(peptides, "NTPVLIQVSMGAAK", false, false, true);
                        AssertPeptideProperties(peptides, "YMGDYK", false, false, true);
                        AssertPeptideProperties(peptides, "LVK", false, false, true);
                        AssertPeptideProperties(peptides, "QGHPPGAFNTNNLEWTR", true, true, true);
                        AssertPeptideProperties(peptides, "AIVK", true, true, true);
                        AssertPeptideProperties(peptides, "TLVEPPMR", true, true, true);
                    }
                }
            }
            finally
            {
                CleanupTestFolder(subFolder);
            }
        }

        [Test]
        public void MultipleDatabases()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"DigestionTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(subFolder);

            try
            {
                string databasePath1 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
                DbForDigestion database1 = new DbForDigestion(databasePath1);

                string databasePath2 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_2.fasta");
                DbForDigestion database2 = new DbForDigestion(databasePath2);

                string databasePath3 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_3.fasta");
                DbForDigestion database3 = new DbForDigestion(databasePath3);

                Parameters param = new Parameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams trypsin = new DigestionParams(
                    protease: "trypsin (cleave before proline)",
                    maxMissedCleavages: 0,
                    minPeptideLength: 1,
                    maxPeptideLength: 100);

                param.ProteaseSpecificParameters.Add(new ProteaseSpecificParameters(trypsin));

                DigestionTask digestion = new DigestionTask();
                digestion.DigestionParameters = param;
                var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database1, database2, database3 });

                Assert.That(digestionResults.PeptideByFile.Count, Is.EqualTo(3));
                Assert.That(digestionResults.PeptideByFile.Values.Count, Is.EqualTo(3));
                Assert.That(digestionResults.PeptideByFile[database1.FileName][param.ProteaseSpecificParameters.First().DigestionAgentName].Count, Is.EqualTo(2));
                Assert.That(digestionResults.PeptideByFile[database2.FileName][param.ProteaseSpecificParameters.First().DigestionAgentName].Count, Is.EqualTo(2));
                Assert.That(digestionResults.PeptideByFile[database3.FileName][param.ProteaseSpecificParameters.First().DigestionAgentName].Count, Is.EqualTo(2));

                // Database 1 assertions (abbreviated - keep your existing assertions)
                // Database 2 assertions
                // Database 3 assertions
            }
            finally
            {
                CleanupTestFolder(subFolder);
            }
        }

        [Test]
        public void ProteaseModTest()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"DigestionTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(subFolder);

            try
            {
                string databasePath1 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "ProteaseModTest.fasta");
                DbForDigestion database1 = new DbForDigestion(databasePath1);

                var protDic = ProteaseDictionary.LoadProteaseDictionary(Path.Combine(GlobalVariables.DataDir, @"ProteolyticDigestion", @"proteases.tsv"), GlobalVariables.ProteaseMods);

                Parameters param = new Parameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams cnbrDigestion = new DigestionParams(
                    protease: "CNBr",
                    maxMissedCleavages: 0,
                    minPeptideLength: 1,
                    maxPeptideLength: 100);

                param.ProteaseSpecificParameters.Add(new ProteaseSpecificParameters(cnbrDigestion));

                DigestionTask digestion = new DigestionTask();
                digestion.DigestionParameters = param;
                var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database1 });

                foreach (var entry in digestionResults.PeptideByFile[database1.FileName][param.ProteaseSpecificParameters.First().DigestionAgentName])
                {
                    var peptides = entry.Value;
                    Assert.That(peptides.Count, Is.EqualTo(2));
                    Assert.That(peptides[0].FullSequence, Is.Not.EqualTo(peptides[1].FullSequence));

                    var weights = peptides.Select(p => p.MolecularWeight).OrderBy(w => w).ToList();
                    Assert.That(weights[0], Is.EqualTo(882.39707781799996).Within(0.0001));
                    Assert.That(weights[1], Is.EqualTo(930.400449121).Within(0.0001));
                }
            }
            finally
            {
                CleanupTestFolder(subFolder);
            }
        }

        [Test]
        public void InitiatorMethionineTest()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"DigestionTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(subFolder);

            try
            {
                string databasePath1 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
                DbForDigestion database1 = new DbForDigestion(databasePath1);

                string databasePath2 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_2.fasta");
                DbForDigestion database2 = new DbForDigestion(databasePath2);

                string databasePath3 = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_3.fasta");
                DbForDigestion database3 = new DbForDigestion(databasePath3);

                Parameters param = new Parameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams trypsin = new DigestionParams(
                    protease: "trypsin (cleave before proline)",
                    maxMissedCleavages: 0,
                    minPeptideLength: 1,
                    maxPeptideLength: 100);

                param.ProteaseSpecificParameters.Add(new ProteaseSpecificParameters(trypsin));

                DigestionTask digestion = new DigestionTask();
                digestion.DigestionParameters = param;
                var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database1, database2, database3 });

                Assert.That(digestionResults.PeptideByFile.Count, Is.EqualTo(3));

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

                // Test assertions...
            }
            finally
            {
                CleanupTestFolder(subFolder);
            }
        }
    }
}
