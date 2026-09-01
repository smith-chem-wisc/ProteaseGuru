using Omics;

namespace ProteaseGuru.Tasks.CoverageMapConfiguration
{
    /// <summary>
    /// Sequence coverage per protease per protein, pooled across databases.
    ///
    /// This lived in two places — the digestion task and the GUI's reload path — and the copies drifted
    /// apart until one of them was dividing by the wrong quantity for several years. PlotModelStat keeps a
    /// third copy on purpose: it merges databases by IBioPolymer (accession *and* sequence) where this one
    /// pools and groups by accession alone, so the two disagree when one accession carries different
    /// sequences in different databases.
    /// </summary>
    public static class SequenceCoverageCalculator
    {
        /// <summary>
        /// Returns protease -> protein -> (total coverage, coverage from unique peptides), both as a
        /// percent of the whole protein rounded to two decimals.
        ///
        /// Proteins with no peptides for a protease do not appear: the result is built from the peptide
        /// side. Every peptide's accession must belong to one of the loaded databases — it always does,
        /// since the peptides are filed under those same entries — so a miss is a defect and throws rather
        /// than dropping the protein silently. Where several databases supply the same accession the last
        /// one seen supplies the sequence length, so a shared accession with differing sequences is
        /// measured against whichever landed last.
        /// </summary>
        public static Dictionary<string, Dictionary<IBioPolymer, (double Total, double Unique)>> Calculate(
            Dictionary<string, Dictionary<string, Dictionary<IBioPolymer, List<InSilicoPep>>>> peptideByFile)
        {
            // PHASE 1: Aggregate peptides from all databases by protease
            var allDatabasePeptidesByProtease = new Dictionary<string, List<InSilicoPep>>();
            var accessionToProtein = new Dictionary<string, IBioPolymer>();

            foreach (var database in peptideByFile)
            {
                foreach (var protease in database.Value)
                {
                    string proteaseName = protease.Key;

                    if (!allDatabasePeptidesByProtease.TryGetValue(proteaseName, out var peptideList))
                    {
                        peptideList = new List<InSilicoPep>();
                        allDatabasePeptidesByProtease[proteaseName] = peptideList;
                    }

                    foreach (var proteinEntry in protease.Value)
                    {
                        peptideList.AddRange(proteinEntry.Value);
                        accessionToProtein[proteinEntry.Key.Accession] = proteinEntry.Key;
                    }
                }
            }

            // PHASE 2: Calculate coverage for each protease-protein combination
            var proteinSequenceCoverageByProtease = new Dictionary<string, Dictionary<IBioPolymer, (double Total, double Unique)>>();

            foreach (var protease in allDatabasePeptidesByProtease)
            {
                string proteaseName = protease.Key;
                var peptidesForProtease = protease.Value;

                var peptidesByProteinAccession = peptidesForProtease
                    .GroupBy(p => p.Protein)
                    .ToDictionary(group => group.Key, group => group.ToList());

                var sequenceCoverages = new Dictionary<IBioPolymer, (double Total, double Unique)>();

                foreach (var proteinGroup in peptidesByProteinAccession)
                {
                    string proteinAccession = proteinGroup.Key;
                    var peptidesForThisProtein = proteinGroup.Value;

                    if (!accessionToProtein.TryGetValue(proteinAccession, out IBioPolymer? actualProtein))
                    {
                        throw new KeyNotFoundException(
                            $"Peptides were assigned to accession '{proteinAccession}' under protease '{proteaseName}', "
                            + "but none of the loaded databases contain it.");
                    }

                    int proteinSequenceLength = actualProtein.Length;
                    var coveredResidues = new HashSet<int>();
                    var coveredResiduesUnique = new HashSet<int>();
                    var uniquePeptideSet = peptidesForThisProtein.ToHashSet();

                    foreach (var peptide in uniquePeptideSet)
                    {
                        for (int residuePosition = peptide.StartResidue; residuePosition <= peptide.EndResidue; residuePosition++)
                        {
                            coveredResidues.Add(residuePosition);
                            if (peptide.Unique)
                            {
                                coveredResiduesUnique.Add(residuePosition);
                            }
                        }
                    }

                    double totalCoveragePercent = Math.Round((double)coveredResidues.Count / proteinSequenceLength * 100.0, 2);
                    double uniqueCoveragePercent = Math.Round((double)coveredResiduesUnique.Count / proteinSequenceLength * 100.0, 2);

                    sequenceCoverages.Add(actualProtein, (totalCoveragePercent, uniqueCoveragePercent));
                }

                proteinSequenceCoverageByProtease.Add(proteaseName, sequenceCoverages);
            }

            return proteinSequenceCoverageByProtease;
        }
    }
}
