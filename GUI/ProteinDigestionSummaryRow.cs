namespace GUI
{
    /// <summary>
    /// Represents a single row in the protein digestion summary table.
    /// Each row contains digestion results for one protease.
    /// </summary>
    public class ProteinDigestionSummaryRow
    {
        public string Protease { get; set; } = string.Empty;
        public int UniquePeptides { get; set; }
        public int SharedPeptides { get; set; }
        public int TotalPeptides => UniquePeptides + SharedPeptides;
        public string TotalCoverage { get; set; } = string.Empty;
        public string UniqueCoverage { get; set; } = string.Empty;
    }
}
