using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Text;
using Omics.Modifications;

namespace Tasks
{
    //digestion parameters provided by the user
    public class Parameters
    {
        public int NumberOfMissedCleavagesAllowed { get; set; }
        public int MinPeptideLengthAllowed { get; set; }
        public int MaxPeptideLengthAllowed { get; set; }
        public bool TreatModifiedPeptidesAsDifferent { get; set; }
        public List<Protease> ProteasesForDigestion { get; set; }
        public int MinPeptideMassAllowed {get; set;}
        public int MaxPeptideMassAllowed { get; set; }
        public List<Modification> fixedMods { get; set; }
        public List<Modification> variableMods { get; set; }

        public string OutputFolder;

        //default parameters?
        public Parameters()
        {
            NumberOfMissedCleavagesAllowed = 2;
            MinPeptideLengthAllowed = 7;
            MaxPeptideLengthAllowed = 50;
            TreatModifiedPeptidesAsDifferent = false;
            ProteasesForDigestion = new List<Protease>();
            MinPeptideMassAllowed = -1;
            MaxPeptideMassAllowed = -1;
            fixedMods = new List<Modification>();
            variableMods = new List<Modification>();
        }
        public Parameters(int numMissedCleavages, int minPeptideLength, int maxPeptideLength, bool treatModifiedPeptidesDifferent, List<Protease> proteases, int minPeptideMass, int maxPeptideMass)
        {
            NumberOfMissedCleavagesAllowed = numMissedCleavages;
            MinPeptideLengthAllowed = minPeptideLength;
            MaxPeptideLengthAllowed = maxPeptideLength;
            TreatModifiedPeptidesAsDifferent = treatModifiedPeptidesDifferent;
            ProteasesForDigestion = proteases;
            MinPeptideMassAllowed = minPeptideMass;
            MaxPeptideMassAllowed = maxPeptideMass;
        }
    }
}
