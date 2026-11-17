using ProteaseGuru.Engine;
using NUnit.Framework;
using Omics.Digestion;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Tasks;
using UsefulProteomicsDatabases;

namespace ProteaseGuru.Test
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

                RunParameters param = new RunParameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams trypsin = new DigestionParams(
                    protease: "trypsin|P",
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
        public void MissedCleavagesAreWrittenAndReadBack()
        {
            string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"MissedCleavageTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(subFolder);

            try
            {
                string fastaPath = Path.Combine(subFolder, "MissedCleavages.fasta");
                File.WriteAllText(fastaPath, ">sp|P99999|MC_TEST Missed cleavage test protein\nPEPTIDEKPEPTIDERAAAKGGG\n");
                DbForDigestion database = new DbForDigestion(fastaPath);

                RunParameters param = new RunParameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;
                param.ProteaseSpecificParameters.Add(new ProteaseSpecificParameters(
                    new DigestionParams(protease: "trypsin|P", maxMissedCleavages: 2, minPeptideLength: 1, maxPeptideLength: 100)));
                param.ProteaseSpecificParameters.Add(new ProteaseSpecificParameters(
                    new DigestionParams(protease: "trypsin", maxMissedCleavages: 2, minPeptideLength: 1, maxPeptideLength: 100)));

                DigestionTask digestion = new DigestionTask();
                digestion.DigestionParameters = param;
                var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });

                var byProtease = digestionResults.PeptideByFile[database.FileName];
                var noProline = byProtease["trypsin|P"].Values.Single();
                var trypsin = byProtease["trypsin"].Values.Single();

                Assert.That(GetPeptideBySequence(noProline, "AAAK").MissedCleavages, Is.EqualTo(0));
                Assert.That(GetPeptideBySequence(noProline, "PEPTIDEKPEPTIDER").MissedCleavages, Is.EqualTo(0), "K before P is not a cleavage site for trypsin|P");
                Assert.That(GetPeptideBySequence(noProline, "PEPTIDEKPEPTIDERAAAK").MissedCleavages, Is.EqualTo(1));
                Assert.That(GetPeptideBySequence(noProline, "PEPTIDEKPEPTIDERAAAKGGG").MissedCleavages, Is.EqualTo(2));
                Assert.That(GetPeptideBySequence(noProline, "PEPTIDEK"), Is.Null);

                Assert.That(GetPeptideBySequence(trypsin, "PEPTIDEK").MissedCleavages, Is.EqualTo(0));
                Assert.That(GetPeptideBySequence(trypsin, "PEPTIDEKPEPTIDER").MissedCleavages, Is.EqualTo(1));
                Assert.That(GetPeptideBySequence(trypsin, "PEPTIDEKPEPTIDERAAAK").MissedCleavages, Is.EqualTo(2));

                var lines = File.ReadAllLines(Path.Combine(subFolder, "ProteaseGuruPeptides_1.tsv"));
                var header = lines[0].Split('\t');
                Assert.That(header, Is.EqualTo(new[]
                {
                    "Database", "Protease", "Base Sequence", "Full Sequence", "Previous Amino Acid",
                    "Next Amino Acid", "Start Residue", "End Residue", "Length", "Molecular Weight",
                    "Protein Accession", "Protein Name", "Unique Peptide (in this database)",
                    "Unique Peptide (in all databases)", "Peptide sequence exclusive to this Database",
                    "Hydrophobicity", "Electrophoretic Mobility", "Chronologer Retention Time", "Pfly Detectability (>=0.5)",
                    "PFly NotDetectable Prob", "PFly Low Detectability Prob", "PFly Intermediate Detectability Prob", "PFly High Detectability Prob",
                    "Missed Cleavages"
                }));

                var written = noProline.Concat(trypsin).ToList();
                Assert.That(lines.Length - 1, Is.EqualTo(written.Count));
                foreach (var row in lines.Skip(1))
                {
                    Assert.That(row.Split('\t').Length, Is.EqualTo(header.Length));
                    var read = InSilicoPep.FromTsvRow(row);
                    Assert.That(read, Is.Not.Null);
                    Assert.That(read!.ToString(), Is.EqualTo(row));
                    var original = written.Single(p => p.Equals(read));
                    Assert.That(read.MissedCleavages, Is.EqualTo(original.MissedCleavages));
                    Assert.That(read.MissedCleavages, Is.Not.Null);
                }
            }
            finally
            {
                CleanupTestFolder(subFolder);
            }
        }

        [Test]
        public void OlderResultRowsLoadWithoutMissedCleavages()
        {
            var pep = new InSilicoPep("PEPTIDEK", "PEPTIDEK", '-', 'P', true, 12.5, 0.75, 21.3, true, 8, 927.45,
                "db.fasta", "P99999", "MC_TEST", 1, 8, "trypsin", (0.1, 0.2, 0.3, 0.4), 0);
            var fields = pep.ToString().Split('\t');
            Assert.That(fields.Length, Is.EqualTo(24));

            string pflyEraRow = string.Join('\t', fields.Take(23));
            var pflyEra = InSilicoPep.FromTsvRow(pflyEraRow);
            Assert.That(pflyEra, Is.Not.Null);
            Assert.That(pflyEra!.MissedCleavages, Is.Null);
            Assert.That(pflyEra.PflyProbabilities, Is.EqualTo(pep.PflyProbabilities));
            Assert.That(pflyEra.ToString(), Is.EqualTo(pflyEraRow + "\t"));

            const string release024Row = "db.fasta\ttrypsin\tPEPTIDEK\tPEPTIDEK\t-\tP\t1\t8\t8\t927.45\tP99999\tMC_TEST\tTrue\tFalse\tTrue\t12.5\t0.75\t21.3\tTrue\t0.1\t0.2\t0.3\t0.4";
            var release024 = InSilicoPep.FromTsvRow(release024Row);
            Assert.That(release024, Is.Not.Null);
            Assert.That(release024!.MissedCleavages, Is.Null);
            Assert.That(release024.BaseSequence, Is.EqualTo("PEPTIDEK"));
            Assert.That(release024.UniqueAllDbs, Is.False);
            Assert.That(release024.ChronologerRetentionTime, Is.EqualTo(21.3));
            Assert.That(release024.PflyDetectability, Is.True);
            Assert.That(release024.PflyProbabilities, Is.EqualTo((0.1, 0.2, 0.3, 0.4)));
            Assert.That(release024.ToString(), Is.EqualTo(release024Row + "\t"));

            string chronologerEraRow = string.Join('\t', fields.Take(17).Append("-1").Append(""));
            var chronologerEra = InSilicoPep.FromTsvRow(chronologerEraRow);
            Assert.That(chronologerEra, Is.Not.Null);
            Assert.That(chronologerEra!.MissedCleavages, Is.Null);
            Assert.That(chronologerEra.ChronologerRetentionTime, Is.Null);
            Assert.That(chronologerEra.PflyProbabilities, Is.Null);

            string oldestRow = string.Join('\t', fields.Take(17));
            var oldest = InSilicoPep.FromTsvRow(oldestRow);
            Assert.That(oldest, Is.Not.Null);
            Assert.That(oldest!.MissedCleavages, Is.Null);
            Assert.That(oldest.ChronologerRetentionTime, Is.Null);
            Assert.That(oldest.BaseSequence, Is.EqualTo("PEPTIDEK"));
            Assert.That(oldest.EndResidue, Is.EqualTo(8));

            Assert.That(InSilicoPep.FromTsvRow(pflyEra.ToString())!.MissedCleavages, Is.Null, "a blank count stays blank");
            Assert.That(InSilicoPep.FromTsvRow(string.Join('\t', fields.Take(16))), Is.Null);
            Assert.That(InSilicoPep.FromTsvRow(""), Is.Null);
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

                RunParameters param = new RunParameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams trypsin = new DigestionParams(
                    protease: "trypsin|P",
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

                // Proteases come from mzLib's embedded resource via ProteaseDictionary.Dictionary.
                var protDic = ProteaseDictionary.Dictionary;

                RunParameters param = new RunParameters();
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

                RunParameters param = new RunParameters();
                param.TreatModifiedPeptidesAsDifferent = false;
                param.OutputFolder = subFolder;

                DigestionParams trypsin = new DigestionParams(
                    protease: "trypsin|P",
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
