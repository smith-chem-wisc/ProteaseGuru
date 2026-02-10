namespace Engine
{
    /// <summary>
    /// Event arguments for reporting progress during long-running operations.
    /// </summary>
    public class ProgressEventArgs : EventArgs
    {
        public ProgressEventArgs(int currentProgress, int maxProgress, string statusMessage)
        {
            CurrentProgress = currentProgress;
            MaxProgress = maxProgress;
            StatusMessage = statusMessage;
        }

        /// <summary>
        /// The current progress value (e.g., number of items completed).
        /// </summary>
        public int CurrentProgress { get; }

        /// <summary>
        /// The maximum progress value (e.g., total number of items).
        /// </summary>
        public int MaxProgress { get; }

        /// <summary>
        /// A descriptive status message for the current operation.
        /// </summary>
        public string StatusMessage { get; }

        /// <summary>
        /// Gets the progress as a percentage (0-100).
        /// </summary>
        public double ProgressPercent => MaxProgress > 0 ? (double)CurrentProgress / MaxProgress * 100.0 : 0;
    }
}
