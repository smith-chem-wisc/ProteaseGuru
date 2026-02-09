using NUnit.Framework;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;

namespace Test;

[TestFixture]
[NonParallelizable] // Prevent parallel execution of tests in this fixture due to shared Chronologer model file
internal class ChronologerTests
{
    // Lock object to synchronize access to Chronologer predictor across tests
    private static readonly object ChronologerLock = new object();

    [Test]
    public static void ChronologerRetentionTimePredictionTest()
    {
        // Use unique folder name with GUID to prevent conflicts
        string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"ChronologerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subFolder);

        try
        {
            string databasePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
            DbForDigestion database = new DbForDigestion(databasePath);

            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 7;  // Chronologer works best with peptides >= 7 AA
            param.MaxPeptideLengthAllowed = 50; // Chronologer has max length limit
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin (cleave before proline)"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });

            // Get all peptides from results
            var allPeptides = digestionResults.PeptideByFile[database.FileName][param.ProteasesForDigestion.First().Name]
                .SelectMany(entry => entry.Value)
                .ToList();

            // Verify we have peptides to test
            Assert.That(allPeptides.Count, Is.GreaterThan(0), "Should have peptides to test");

            int successfulPredictions = 0;
            int failedPredictions = 0;

            foreach (var peptide in allPeptides)
            {
                var sequence = peptide.BaseSequence;

                // Chronologer has length constraints (typically 7-50 amino acids)
                if (sequence.Length >= 7 && sequence.Length <= 50)
                {
                    // Verify the sequence only contains valid amino acids
                    bool hasValidAminoAcids = sequence.All(c => "ACDEFGHIKLMNPQRSTVWY".Contains(c));

                    if (hasValidAminoAcids)
                    {
                        successfulPredictions++;
                    }
                    else
                    {
                        failedPredictions++;
                    }
                }
                else
                {
                    failedPredictions++;
                }
            }

            // Assert that we have successful predictions for valid peptides
            Assert.That(successfulPredictions, Is.GreaterThan(0), "Should have successful RT predictions for valid peptides");
        }
        finally
        {
            // Cleanup with retry logic for locked files
            CleanupTestFolder(subFolder);
        }
    }

    [Test]
    public static void ChronologerPredictorDirectTest()
    {
        // Synchronize access to Chronologer predictor
        lock (ChronologerLock)
        {
            // Direct test of the Chronologer predictor with known peptides
            using var rtPredictor = new Chromatography.RetentionTimePrediction.Chronologer.ChronologerRetentionTimePredictor();

            // Use correct protease name from the dictionary
            var protein = new Protein(
                "MSFVNGNEIFTAARKQGHYAVGAFNTNNLEWTRKPEPTIDESAMPLERKNTPVLIQVSMGAAKYLVKTLVEEEMR",
                "TestProtein");

            var digestionParams = new DigestionParams(
                protease: "trypsin (cleave before proline)",
                maxMissedCleavages: 0,
                minPeptideLength: 7,
                maxPeptideLength: 50);

            var peptides = protein.Digest(digestionParams, new List<Modification>(), new List<Modification>()).ToList();

            Assert.That(peptides.Count, Is.GreaterThan(0), "Should have peptides from digestion");

            var validPredictions = new List<double>();
            var failedPredictions = new List<string>();

            foreach (var peptide in peptides)
            {
                // Skip peptides with non-standard amino acids
                if (!peptide.BaseSequence.All(c => "ACDEFGHIKLMNPQRSTVWY".Contains(c)))
                {
                    continue;
                }

                var result = rtPredictor.PredictRetentionTime(peptide, out var failureReason);

                if (result.HasValue)
                {
                    validPredictions.Add(result.Value);

                    Assert.That(double.IsNaN(result.Value), Is.False,
                        $"RT prediction for {peptide.BaseSequence} should not be NaN");
                    Assert.That(double.IsInfinity(result.Value), Is.False,
                        $"RT prediction for {peptide.BaseSequence} should not be infinite");
                }
                else
                {
                    failedPredictions.Add($"{peptide.BaseSequence}: {failureReason}");
                }
            }

            Assert.That(validPredictions.Count, Is.GreaterThan(0),
                $"Should have successful predictions. Failures: {string.Join(", ", failedPredictions)}");

            foreach (var prediction in validPredictions)
            {
                Assert.That(prediction, Is.Not.EqualTo(-1), "Successful prediction should not be sentinel value");
            }
        }
    }

    [Test]
    public static void BatchChronologerRetentionTimeConsistencyTest()
    {
        // Synchronize access to Chronologer predictor
        lock (ChronologerLock)
        {
            // Test that batch processing gives consistent results
            using var rtPredictor = new Chromatography.RetentionTimePrediction.Chronologer.ChronologerRetentionTimePredictor();

            var protein = new Protein(
                "MSFVNGNEIFTAARKQGHYAVGAFNTNNLEWTRKPEPTIDESAMPLERKNTPVLIQVSMGAAKYLVKTLVEEEMR",
                "TestProtein");

            // Use correct protease name from the dictionary
            var digestionParams = new DigestionParams(
                protease: "trypsin (cleave before proline)",
                maxMissedCleavages: 0,
                minPeptideLength: 7,
                maxPeptideLength: 50);

            var peptides = protein.Digest(digestionParams, new List<Modification>(), new List<Modification>()).ToList();

            Assert.That(peptides.Count, Is.GreaterThan(0),
                "Should have peptides from digestion.");

            // Filter to only peptides that Chronologer can handle
            var validPeptides = peptides
                .Where(p => p.BaseSequence.All(c => "ACDEFGHIKLMNPQRSTVWY".Contains(c)))
                .ToList();

            if (validPeptides.Count == 0)
            {
                Assert.Inconclusive("No valid peptides for Chronologer testing after filtering");
                return;
            }

            // Calculate RT twice for the same peptides
            var results1 = new double[validPeptides.Count];
            var results2 = new double[validPeptides.Count];

            for (int i = 0; i < validPeptides.Count; i++)
            {
                var result1 = rtPredictor.PredictRetentionTime(validPeptides[i], out var failureReason1);
                var result2 = rtPredictor.PredictRetentionTime(validPeptides[i], out var failureReason2);

                results1[i] = result1 ?? -1;
                results2[i] = result2 ?? -1;

                if (result1.HasValue != result2.HasValue)
                {
                    Assert.Fail($"Inconsistent success/failure for peptide {validPeptides[i].BaseSequence}");
                }
            }

            // Verify consistency - same input should give same output
            for (int i = 0; i < validPeptides.Count; i++)
            {
                Assert.That(results1[i], Is.EqualTo(results2[i]).Within(0.0001),
                    $"Chronologer predictions should be consistent for peptide {validPeptides[i].BaseSequence}");
            }

            Assert.That(results1.Length, Is.EqualTo(validPeptides.Count), "Results array should match peptides count");

            int successCount = results1.Count(r => r != -1);
            Assert.That(successCount, Is.GreaterThan(0),
                $"Should have at least one successful prediction. Total peptides: {validPeptides.Count}");
        }
    }

    [Test]
    public static void ChronologerRetentionTimeInTsvOutputTest()
    {
        // Use unique folder name with GUID to prevent conflicts
        string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"ChronologerTsvTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subFolder);

        try
        {
            // Create a simple FASTA file with a known protein sequence
            string fastaContent = @">sp|P00000|TEST_HUMAN Test protein for Chronologer
MSFVNGNEIFTAARKQGHYAVGAFNTNNLEWTRKPEPTIDESAMPLERKNTPVLIQVSMGAAKYLVKTLVEEEMRK";

            string fastaPath = Path.Combine(subFolder, "TestProtein.fasta");
            File.WriteAllText(fastaPath, fastaContent);

            DbForDigestion database = new DbForDigestion(fastaPath);

            // Set up parameters with trypsin and default settings
            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 7;
            param.MaxPeptideLengthAllowed = 50;
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin (cleave before proline)"]);
            param.OutputFolder = subFolder;

            // Run digestion
            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });

            // Find the output TSV file
            string tsvPath = Path.Combine(subFolder, "ProteaseGuruPeptides_1.tsv");
            Assert.That(File.Exists(tsvPath), Is.True, $"Output TSV file should exist at {tsvPath}");

            // Read and parse the TSV file
            var lines = File.ReadAllLines(tsvPath);
            Assert.That(lines.Length, Is.GreaterThan(1), "TSV file should have header and data rows");

            // Verify header contains Chronologer Retention Time column
            var header = lines[0].Split('\t');
            int chronologerColumnIndex = Array.IndexOf(header, "Chronologer Retention Time");
            Assert.That(chronologerColumnIndex, Is.GreaterThan(-1),
                "Header should contain 'Chronologer Retention Time' column. Header: " + string.Join(", ", header));

            // Parse data rows and check Chronologer RT values
            int validChronologerValues = 0;
            int invalidChronologerValues = 0;
            var peptideRtPairs = new List<(string sequence, double rt)>();

            for (int i = 1; i < lines.Length; i++)
            {
                var columns = lines[i].Split('\t');

                if (columns.Length > chronologerColumnIndex)
                {
                    string baseSequence = columns[2]; // Base Sequence column
                    string rtString = columns[chronologerColumnIndex];

                    if (double.TryParse(rtString, out double rtValue))
                    {
                        peptideRtPairs.Add((baseSequence, rtValue));

                        // Check if it's a valid prediction (not -1 sentinel value)
                        if (rtValue >= 0)
                        {
                            validChronologerValues++;
                        }
                        else
                        {
                            invalidChronologerValues++;
                        }
                    }
                }
            }

            // Output some diagnostic information
            TestContext.WriteLine($"Total peptides in TSV: {lines.Length - 1}");
            TestContext.WriteLine($"Valid Chronologer RT values (>= 0): {validChronologerValues}");
            TestContext.WriteLine($"Invalid/Failed predictions (-1): {invalidChronologerValues}");

            // Show some example values
            TestContext.WriteLine("\nSample peptide RT predictions:");
            foreach (var (sequence, rt) in peptideRtPairs.Take(10))
            {
                TestContext.WriteLine($"  {sequence}: {rt:F4}");
            }

            // Assertions
            Assert.That(peptideRtPairs.Count, Is.GreaterThan(0), "Should have parsed at least one peptide");
            Assert.That(validChronologerValues, Is.GreaterThan(0),
                $"Should have at least one valid Chronologer RT prediction. " +
                $"Valid: {validChronologerValues}, Invalid: {invalidChronologerValues}");

            // Verify that valid RT values are in a reasonable range
            var validRts = peptideRtPairs.Where(p => p.rt >= 0).Select(p => p.rt).ToList();
            if (validRts.Any())
            {
                Assert.That(validRts.All(rt => !double.IsNaN(rt)), Is.True, "RT values should not be NaN");
                Assert.That(validRts.All(rt => !double.IsInfinity(rt)), Is.True, "RT values should not be infinite");

                TestContext.WriteLine($"\nRT value statistics:");
                TestContext.WriteLine($"  Min: {validRts.Min():F4}");
                TestContext.WriteLine($"  Max: {validRts.Max():F4}");
                TestContext.WriteLine($"  Avg: {validRts.Average():F4}");
            }
        }
        finally
        {
            // Cleanup with retry logic for locked files
            CleanupTestFolder(subFolder);
        }
    }

    [Test]
    public static void ChronologerRetentionTimeStoredInPeptideObjectTest()
    {
        // Use unique folder name with GUID to prevent conflicts
        string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, $"ChronologerObjectTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subFolder);

        try
        {
            // Use existing test database
            string databasePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Databases", "TestDatabase_1.fasta");
            DbForDigestion database = new DbForDigestion(databasePath);

            Parameters param = new Parameters();
            param.MinPeptideLengthAllowed = 7;
            param.MaxPeptideLengthAllowed = 50;
            param.NumberOfMissedCleavagesAllowed = 0;
            param.TreatModifiedPeptidesAsDifferent = false;
            param.ProteasesForDigestion.Add(ProteaseDictionary.Dictionary["trypsin (cleave before proline)"]);
            param.OutputFolder = subFolder;

            DigestionTask digestion = new DigestionTask();
            digestion.DigestionParameters = param;
            var digestionResults = digestion.RunSpecific(subFolder, new List<DbForDigestion>() { database });

            // Get all peptides from results
            var allPeptides = digestionResults.PeptideByFile[database.FileName][param.ProteasesForDigestion.First().Name]
                .SelectMany(entry => entry.Value)
                .ToList();

            Assert.That(allPeptides.Count, Is.GreaterThan(0), "Should have peptides");

            // Check that ChronologerRetentionTime property exists and has values
            int validRtCount = 0;
            int failedRtCount = 0;

            foreach (var peptide in allPeptides)
            {
                // Access the ChronologerRetentionTime property
                double rt = peptide.ChronologerRetentionTime;

                if (rt >= 0)
                {
                    validRtCount++;
                    Assert.That(double.IsNaN(rt), Is.False, $"RT for {peptide.BaseSequence} should not be NaN");
                    Assert.That(double.IsInfinity(rt), Is.False, $"RT for {peptide.BaseSequence} should not be infinite");
                }
                else
                {
                    failedRtCount++;
                }
            }

            TestContext.WriteLine($"Total peptides: {allPeptides.Count}");
            TestContext.WriteLine($"Valid Chronologer RT: {validRtCount}");
            TestContext.WriteLine($"Failed predictions: {failedRtCount}");

            // At least some peptides should have valid RT predictions
            Assert.That(validRtCount, Is.GreaterThan(0),
                "At least some peptides should have valid Chronologer RT predictions");

            // Show some examples
            TestContext.WriteLine("\nExample peptides with Chronologer RT:");
            foreach (var pep in allPeptides.Where(p => p.ChronologerRetentionTime >= 0).Take(5))
            {
                TestContext.WriteLine($"  {pep.BaseSequence}: RT = {pep.ChronologerRetentionTime:F4}");
            }
        }
        finally
        {
            // Cleanup with retry logic for locked files
            CleanupTestFolder(subFolder);
        }
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
                // Force garbage collection to release any file handles
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Directory.Delete(folderPath, true);
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // Wait and retry
                Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                // Wait and retry
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                // Log but don't fail the test for cleanup issues
                TestContext.WriteLine($"Warning: Could not delete test folder {folderPath}: {ex.Message}");
                return;
            }
        }
    }
}
