using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class InSilicoPep
    {
        public string BaseSequence;
        public string FullSequence;
        public char PreviousAA;
        public char NextAA;
        public bool Unique;
        public double Hydrophobicity;
        public double ElectrophoreticMobility;
        public int Length;
        public double MolecularWeight;
        public string Database;
        public string Protein;
        public int StartResidue;
        public int EndResidue;
        public string Protease;

        public InSilicoPep(string baseSequence, string fullSequence, char previousAA, char nextAA, bool unique, double hydrophobicity, double electrophoreticMobility,
            int length, double molecularWeight, string database, string protein,int start, int end, string protease)
        {
            BaseSequence = baseSequence;
            FullSequence = fullSequence;
            PreviousAA = previousAA;
            NextAA = nextAA;
            Unique = unique;
            Hydrophobicity = hydrophobicity;
            ElectrophoreticMobility = electrophoreticMobility;
            Length = length;
            MolecularWeight = molecularWeight;
            Database = database;
            Protein = protein;
            StartResidue = start;
            EndResidue = end;
            Protease = protease;           
        }

        public string ToString()
        {
            string tab = "\t";
            StringBuilder sb = new StringBuilder();
            sb.Append(Database);
            sb.Append(tab);
            sb.Append(Protease);
            sb.Append(tab);
            sb.Append(BaseSequence);
            sb.Append(tab);
            sb.Append(FullSequence);
            sb.Append(tab);
            sb.Append(PreviousAA);
            sb.Append(tab);
            sb.Append(NextAA);
            sb.Append(tab);
            sb.Append(Length);
            sb.Append(tab);
            sb.Append(MolecularWeight);
            sb.Append(tab);
            sb.Append(Protein);
            sb.Append(tab);
            sb.Append(Unique);
            sb.Append(tab);
            sb.Append("put unique in analysis info here");
            sb.Append(tab);
            sb.Append(Hydrophobicity);
            sb.Append(tab);
            sb.Append(ElectrophoreticMobility);
           return sb.ToString();
        }

    }
}
