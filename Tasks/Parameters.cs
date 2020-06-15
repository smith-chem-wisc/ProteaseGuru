using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class Parameters
    {
        public int NumberOfMissedCleavagesAllowed { get; set; }
        public int MinPeptideLengthAllowed { get; set; }
        public int MaxPeptideLengthAllowed { get; set; }
        public bool TreatModifiedPeptidesAsDifferent { get; set; }
        public List<Protease> ProteasesForDigestion { get; set; }
        public string OutputFolder;

        //default parameters?
        public Parameters()
        {
            NumberOfMissedCleavagesAllowed = 2;
            MinPeptideLengthAllowed = 7;
            MaxPeptideLengthAllowed = 50;
            TreatModifiedPeptidesAsDifferent = false;
            ProteasesForDigestion = new List<Protease>();
        }
        public Parameters(int numMissedCleavages, int minPeptideLength, int maxPeptideLength, bool treatModifiedPeptidesDifferent, List<Protease> proteases)
        {
            NumberOfMissedCleavagesAllowed = numMissedCleavages;
            MinPeptideLengthAllowed = minPeptideLength;
            MaxPeptideLengthAllowed = maxPeptideLength;
            TreatModifiedPeptidesAsDifferent = treatModifiedPeptidesDifferent;
            ProteasesForDigestion = proteases;
        }
    }
}
