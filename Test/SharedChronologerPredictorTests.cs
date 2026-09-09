using System.Collections.Concurrent;
using Chromatography.RetentionTimePrediction;
using Chromatography.RetentionTimePrediction.Chronologer;
using NUnit.Framework;
using ProteaseGuru.Tasks;
using Proteomics.ProteolyticDigestion;

namespace ProteaseGuru.Test;

[TestFixture]
[NonParallelizable] // These assert the process-wide session count, so nothing else may hold a session.
internal class SharedChronologerPredictorTests
{
    private static List<PeptideWithSetModifications> SamplePeptides =>
        new[] { "PEPTIDEK", "ELVISLIVESK", "SAMPLERPEPTIDEK" }
            .Select(s => new PeptideWithSetModifications(s))
            .ToList();

    [Test]
    public static void PredictionsComeBackInInputOrder()
    {
        var peptides = SamplePeptides;

        using var session = SharedChronologerPredictor.Open();
        var predictions = session.Predict(peptides, maxThreads: 1);

        Assert.That(predictions, Has.Count.EqualTo(peptides.Count));
        for (int i = 0; i < peptides.Count; i++)
        {
            Assert.That(predictions[i].Peptide, Is.SameAs(peptides[i]));
        }
    }

    [Test]
    public static void TheModelLoadsOnTheFirstSessionAndUnloadsAfterTheLast()
    {
        Assert.That(SharedChronologerPredictor.IsModelLoaded, Is.False);

        ChronologerRetentionTimePredictor released;
        using (var session = SharedChronologerPredictor.Open())
        {
            released = session.Predictor;
            Assert.That(SharedChronologerPredictor.IsModelLoaded, Is.True);
            Assert.That(SharedChronologerPredictor.ActiveSessions, Is.EqualTo(1));
        }

        Assert.That(SharedChronologerPredictor.IsModelLoaded, Is.False);
        Assert.That(SharedChronologerPredictor.ActiveSessions, Is.Zero);

        // Dropped from the field is not enough; the model must actually have been disposed.
        Assert.Throws<ObjectDisposedException>(
            () => released.PredictRetentionTimeEquivalents(SamplePeptides, maxThreads: 1));
    }

    [Test]
    public static void ClosingAnInnerSessionLeavesTheModelLoadedForTheOuterOne()
    {
        var peptides = SamplePeptides;

        using var outer = SharedChronologerPredictor.Open();

        using (var inner = SharedChronologerPredictor.Open())
        {
            Assert.That(inner.Predict(peptides, maxThreads: 1), Has.Count.EqualTo(peptides.Count));
        }

        Assert.That(SharedChronologerPredictor.ActiveSessions, Is.EqualTo(1));
        Assert.That(outer.Predict(peptides, maxThreads: 1), Has.Count.EqualTo(peptides.Count));
    }

    [Test]
    public static void DisposingASessionTwiceReleasesItOnce()
    {
        using var held = SharedChronologerPredictor.Open();

        var session = SharedChronologerPredictor.Open();
        session.Dispose();
        session.Dispose();

        // A second decrement would unload the model while `held` is still using it.
        Assert.That(SharedChronologerPredictor.ActiveSessions, Is.EqualTo(1));
        Assert.That(SharedChronologerPredictor.IsModelLoaded, Is.True);
        Assert.That(held.Predict(SamplePeptides, maxThreads: 1), Is.Not.Empty);
    }

    [Test]
    public static void OverlappingSessionsShareTheModelAndSettleBackToUnloaded()
    {
        var failures = new ConcurrentBag<Exception>();

        Parallel.For(0, 8, _ =>
        {
            try
            {
                using var session = SharedChronologerPredictor.Open();
                session.Predict(SamplePeptides, maxThreads: 1);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        Assert.That(failures, Is.Empty);
        Assert.That(SharedChronologerPredictor.ActiveSessions, Is.Zero);
        Assert.That(SharedChronologerPredictor.IsModelLoaded, Is.False);
    }

    [Test]
    public static void TheModelReloadsAfterTheLastSessionCloses()
    {
        var peptides = SamplePeptides;

        using (var first = SharedChronologerPredictor.Open())
        {
            first.Predict(peptides, maxThreads: 1);
        }

        using var second = SharedChronologerPredictor.Open();

        Assert.That(second.Predict(peptides, maxThreads: 1), Has.Count.EqualTo(peptides.Count));
    }

    [Test]
    public static void PredictingOnAClosedSessionThrows()
    {
        var session = SharedChronologerPredictor.Open();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => session.Predict(Array.Empty<IRetentionPredictable>(), maxThreads: 1));
    }
}
