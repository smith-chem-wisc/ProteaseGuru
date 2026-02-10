namespace GUI
{
    /// <summary>
    /// Represents a single row in the protein digestion summary table.
    /// Each row contains digestion results for one protease.
    /// </summary>
    public class ProteinDigestionSummaryRow
    {
        public string Protease { get; set; }
        public int UniquePeptides { get; set; }
        public int SharedPeptides { get; set; }
        public int TotalPeptides => UniquePeptides + SharedPeptides;
        public string TotalCoverage { get; set; }
        public string UniqueCoverage { get; set; }
    }
}
