using Omics.BioPolymer;
using Omics.Modifications;

namespace Tasks.CoverageMapConfiguration
{
    /// <summary>
    /// Prepares protein and peptide data for coverage map visualization.
    /// Handles splitting sequences, modifications, and variants into line-based data.
    /// </summary>
    public static class CoverageMapDataPreparer
    {
        /// <summary>
        /// Default number of residues per line in coverage map display
        /// </summary>
        public const int DefaultResiduesPerLine = 25;

        #region Sequence Splitting

        /// <summary>
        /// Splits a protein sequence into fixed-length lines for display.
        /// </summary>
        /// <param name="sequence">Full protein sequence</param>
        /// <param name="residuesPerLine">Number of characters per line (default: 25)</param>
        /// <returns>List of sequence fragments</returns>
        public static List<string> SplitSequenceIntoLines(string sequence, int residuesPerLine = DefaultResiduesPerLine)
        {
            if (string.IsNullOrEmpty(sequence))
                return new List<string>();

            var splitSequence = Enumerable.Range(0, sequence.Length / residuesPerLine)
                .Select(i => sequence.Substring(i * residuesPerLine, residuesPerLine))
                .ToList();

            // Handle remaining characters that don't fill a complete line
            var remainder = sequence.Substring(splitSequence.Count * residuesPerLine);
            if (!string.IsNullOrEmpty(remainder))
            {
                splitSequence.Add(remainder);
            }

            return splitSequence;
        }

        #endregion

        #region Modification Splitting

        /// <summary>
        /// Splits modifications into groups for each line of the sequence display.
        /// Adjusts indices to be relative to each line.
        /// </summary>
        /// <param name="mods">All modifications on the protein (1-based positions)</param>
        /// <param name="proteinLength">Total protein length</param>
        /// <param name="residuesPerLine">Number of residues per line (default: 25)</param>
        /// <returns>List of modification dictionaries, one per line with line-relative indices</returns>
        public static List<Dictionary<int, List<Modification>>> SplitModificationsByLine(
            IDictionary<int, List<Modification>> mods,
            int proteinLength,
            int residuesPerLine = DefaultResiduesPerLine)
        {
            if (mods == null || mods.Count == 0)
                return new List<Dictionary<int, List<Modification>>>();

            int lineCount = CalculateLineCount(proteinLength, residuesPerLine);
            var splitMods = new List<Dictionary<int, List<Modification>>>();

            for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var modsInLine = new Dictionary<int, List<Modification>>();
                int lineStart = 1 + (lineIndex * residuesPerLine);  // 1-based start
                int lineEnd = residuesPerLine + (lineIndex * residuesPerLine);  // 1-based end

                foreach (var entry in mods)
                {
                    if (entry.Key >= lineStart && entry.Key <= lineEnd)
                    {
                        // Convert to line-relative 1-based index
                        int relativePosition = entry.Key - (lineIndex * residuesPerLine);
                        modsInLine.Add(relativePosition, entry.Value);
                    }
                }
                splitMods.Add(modsInLine);
            }

            return splitMods;
        }

        #endregion

        #region Variant Splitting

        /// <summary>
        /// Splits sequence variations into groups for each line of the sequence display.
        /// Handles variants that span multiple lines.
        /// </summary>
        /// <param name="variants">All sequence variations on the protein</param>
        /// <param name="proteinLength">Total protein length</param>
        /// <param name="residuesPerLine">Number of residues per line (default: 25)</param>
        /// <returns>List of line-relative residue positions with variants, one list per line</returns>
        public static List<List<int>> SplitVariantsByLine(
            List<SequenceVariation> variants,
            int proteinLength,
            int residuesPerLine = DefaultResiduesPerLine)
        {
            if (variants == null || variants.Count == 0)
                return new List<List<int>>();

            int lineCount = CalculateLineCount(proteinLength, residuesPerLine);
            var splitVariants = new List<List<int>>();

            for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var variantsInLine = new List<int>();
                int lineStart = 1 + (lineIndex * residuesPerLine);  // 1-based
                int lineEnd = residuesPerLine + (lineIndex * residuesPerLine);

                foreach (var variant in variants)
                {
                    // Determine which positions of this variant fall on this line
                    var positions = GetVariantPositionsOnLine(
                        variant.OneBasedBeginPosition,
                        variant.OneBasedEndPosition,
                        lineStart,
                        lineEnd,
                        lineIndex,
                        residuesPerLine);

                    variantsInLine.AddRange(positions);
                }

                splitVariants.Add(variantsInLine.Distinct().ToList());
            }

            return splitVariants;
        }

        /// <summary>
        /// Gets the line-relative positions where a variant appears on a specific line.
        /// Handles four cases: variant fully on line, starts on line, ends on line, or spans line.
        /// </summary>
        private static IEnumerable<int> GetVariantPositionsOnLine(
            int variantStart,
            int variantEnd,
            int lineStart,
            int lineEnd,
            int lineIndex,
            int residuesPerLine)
        {
            var positions = new List<int>();

            // Case 1: Variant completely within this line
            if (variantStart >= lineStart && variantStart <= lineEnd &&
                variantEnd >= lineStart && variantEnd <= lineEnd)
            {
                for (int pos = variantStart; pos <= variantEnd; pos++)
                {
                    positions.Add(pos - (lineIndex * residuesPerLine));
                }
            }
            // Case 2: Variant starts on this line but ends on a later line
            else if (variantStart >= lineStart && variantStart <= lineEnd && variantEnd > lineEnd)
            {
                for (int pos = variantStart; pos <= lineEnd; pos++)
                {
                    positions.Add(pos - (lineIndex * residuesPerLine));
                }
            }
            // Case 3: Variant ends on this line but started on an earlier line
            else if (variantEnd >= lineStart && variantEnd <= lineEnd && variantStart < lineStart)
            {
                for (int pos = lineStart; pos <= variantEnd; pos++)
                {
                    positions.Add(pos - (lineIndex * residuesPerLine));
                }
            }
            // Case 4: Variant spans entire line (starts before, ends after)
            else if (variantStart < lineStart && variantEnd > lineEnd)
            {
                for (int pos = lineStart; pos <= lineEnd; pos++)
                {
                    positions.Add(pos - (lineIndex * residuesPerLine));
                }
            }

            return positions;
        }

        #endregion

        #region Peptide Helpers

        /// <summary>
        /// Checks if a peptide spans beyond the current line.
        /// </summary>
        /// <param name="peptideEndResidue">1-based end residue of the peptide</param>
        /// <param name="lineLength">Length of the current line</param>
        /// <param name="accumulatedIndex">Number of residues in previous lines (0-based)</param>
        /// <returns>Number of remaining residues if peptide continues, -1 if it ends on this line</returns>
        public static int CheckPartialMatch(int peptideEndResidue, int lineLength, int accumulatedIndex)
        {
            int remaining = peptideEndResidue - accumulatedIndex - lineLength - 1;
            return remaining >= 0 ? remaining : -1;
        }

        /// <summary>
        /// Checks if a peptide spans beyond the current line.
        /// </summary>
        /// <param name="peptide">The peptide to check</param>
        /// <param name="lineLength">Length of the current line</param>
        /// <param name="accumulatedIndex">Number of residues in previous lines (0-based)</param>
        /// <returns>Number of remaining residues if peptide continues, -1 if it ends on this line</returns>
        public static int CheckPartialMatch(InSilicoPep peptide, int lineLength, int accumulatedIndex)
        {
            return CheckPartialMatch(peptide.EndResidue, lineLength, accumulatedIndex);
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Calculates the number of lines needed to display a protein sequence.
        /// </summary>
        private static int CalculateLineCount(int proteinLength, int residuesPerLine)
        {
            int fullLines = proteinLength / residuesPerLine;
            int remainder = proteinLength % residuesPerLine;
            return remainder > 0 ? fullLines + 1 : fullLines;
        }

        #endregion
    }
}
