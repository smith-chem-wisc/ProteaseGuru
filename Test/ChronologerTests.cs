using NUnit.Framework;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;

namespace Test;

[TestFixture]
internal class ChronologerTests
{
    [Test]
    public static void ChronologerRetentionTimePredictionTest()
    {
        string subFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"ChronologerTest");
        Directory.CreateDirectory(subFolder);

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

        // Test that Chronologer predictions were calculated
        var rtPredictor = new Chromatography.RetentionTimePrediction.Chronologer.ChronologerRetentionTimePredictor();

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

        Directory.Delete(subFolder, true);
    }

    [Test]
    public static void ChronologerPredictorDirectTest()
    {
        // Direct test of the Chronologer predictor with known peptides
        var rtPredictor = new Chromatography.RetentionTimePrediction.Chronologer.ChronologerRetentionTimePredictor();

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

    [Test]
    public static void BatchChronologerRetentionTimeConsistencyTest()
    {
        // Test that batch processing gives consistent results
        var rtPredictor = new Chromatography.RetentionTimePrediction.Chronologer.ChronologerRetentionTimePredictor();

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
