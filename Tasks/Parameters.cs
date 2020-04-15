using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    class Parameters
    {
        public int NumberOfMissedCleavagesAllowed { get; set; }
        public int MinPeptideLengthAllowed { get; set; }
        public int MaxPeptideLengthAllowed { get; set; }
        public bool TreatModifiedPeptidesAsDifferent { get; set;  }

        public Parameters(int numMissedCleavages, int minPeptideLength, int maxPeptideLength, bool treatModifiedPeptidesDifferent)
        {
            NumberOfMissedCleavagesAllowed = numMissedCleavages;
            MinPeptideLengthAllowed = minPeptideLength;
            MaxPeptideLengthAllowed = maxPeptideLength;
            TreatModifiedPeptidesAsDifferent = treatModifiedPeptidesDifferent;
        }
    }
}
