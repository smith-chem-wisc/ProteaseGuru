using Chromatography.RetentionTimePrediction;
using Chromatography.RetentionTimePrediction.Chronologer;

namespace ProteaseGuru.Tasks;

/// <summary>
/// Process-wide access to the Chronologer retention time predictor.
///
/// Constructing a predictor extracts Chronologer's weights from an embedded resource to a fixed path
/// under the temp directory and loads the model from it, so two constructions race on one file. That,
/// not coexistence, is the hazard: predictors that already exist are independent, and each serializes
/// its own forward passes internally. Sharing one instance behind this class keeps construction
/// serialized and avoids paying the load repeatedly.
///
/// Callers open a session for the span of work that needs the model. It loads on the first session and
/// unloads when the last one closes, so a digestion run pays the load cost once however many databases
/// and proteases it covers, and a concurrent consumer shares that instance rather than building another.
/// </summary>
public static class SharedChronologerPredictor
{
    private static readonly object LifetimeGate = new();

    // Held for the length of every prediction so that teardown cannot land on a model still in use:
    // mzLib disposes the Torch module without taking its own lock, and a dispose reaching a running
    // forward pass is a native use-after-free rather than a catchable exception.
    private static readonly object PredictionGate = new();

    private static ChronologerRetentionTimePredictor? Predictor;
    private static int OpenSessions;

    internal static int ActiveSessions { get { lock (LifetimeGate) return OpenSessions; } }

    internal static bool IsModelLoaded { get { lock (LifetimeGate) return Predictor != null; } }

    /// <summary>
    /// Opens a session, loading the model if no other session holds it. Dispose the session when the
    /// work that needs the model is finished.
    /// </summary>
    public static Session Open()
    {
        lock (LifetimeGate)
        {
            Predictor ??= new ChronologerRetentionTimePredictor();
            OpenSessions++;
            return new Session(Predictor);
        }
    }

    private static void CloseSession()
    {
        lock (LifetimeGate)
        {
            OpenSessions--;
            if (OpenSessions > 0) return;

            lock (PredictionGate)
            {
                Predictor?.Dispose();
                Predictor = null;
            }
        }
    }

    public sealed class Session : IDisposable
    {
        private readonly ChronologerRetentionTimePredictor _predictor;
        private int _closed;

        internal Session(ChronologerRetentionTimePredictor predictor) => _predictor = predictor;

        /// <summary>
        /// The underlying predictor, for callers needing an mzLib API this session does not wrap.
        /// Do not retain it beyond the life of the session.
        /// </summary>
        internal ChronologerRetentionTimePredictor Predictor
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) == 1, this);
                return _predictor;
            }
        }

        /// <summary>
        /// Predicts retention time equivalents for <paramref name="peptides"/>, returned in input order.
        /// Predictions are serialized process-wide, so this blocks while another session is predicting.
        /// </summary>
        public IReadOnlyList<(double? PredictedValue, IRetentionPredictable Peptide, RetentionTimeFailureReason? FailureReason)>
            Predict(IEnumerable<IRetentionPredictable> peptides, int maxThreads)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) == 1, this);

            lock (PredictionGate)
            {
                return _predictor.PredictRetentionTimeEquivalents(peptides, maxThreads);
            }
        }

        public void Dispose()
        {
            // Interlocked rather than a bool: a session disposed from two threads would otherwise
            // decrement the process-wide count twice and unload the model out from under a live session.
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;

            CloseSession();
        }
    }
}
