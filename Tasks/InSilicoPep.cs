using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    //ProteaseGuru peptide object that stores the necessary information form mzlib pwsm
    public class InSilicoPep
    {
        public string BaseSequence;
        public string FullSequence;
        public char PreviousAA;
        public char NextAA;
        public bool Unique;
        public bool UniqueAllDbs;
        public bool SeqOnlyInThisDb;
        public double Hydrophobicity;
        public double ElectrophoreticMobility;
        public int Length;
        public double MolecularWeight;
        public string Database;
        public string Protein;
        public string ProteinName;
        public int StartResidue;
        public int EndResidue;
        public string Protease;

        public InSilicoPep(string baseSequence, string fullSequence, char previousAA, char nextAA, bool unique, double hydrophobicity, double electrophoreticMobility,
            int length, double molecularWeight, string database, string protein, string proteinName,int start, int end, string protease)
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
            ProteinName = proteinName;
            StartResidue = start;
            EndResidue = end;
            Protease = protease;
            UniqueAllDbs = false;
            SeqOnlyInThisDb = true;
        }

        override public string ToString()
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
            sb.Append(StartResidue);
            sb.Append(tab);
            sb.Append(EndResidue);
            sb.Append(tab);
            sb.Append(Length);
            sb.Append(tab);
            sb.Append(MolecularWeight);
            sb.Append(tab);
            sb.Append(Protein);
            sb.Append(tab);
            sb.Append(ProteinName);
            sb.Append(tab);
            sb.Append(Unique);
            sb.Append(tab);
            sb.Append(UniqueAllDbs);
            sb.Append(tab);
            sb.Append(SeqOnlyInThisDb);
            sb.Append(tab);
            sb.Append(Hydrophobicity);
            sb.Append(tab);
            sb.Append(ElectrophoreticMobility);
           return sb.ToString();
        }
        public override bool Equals(object obj)
        {
            var q = obj as InSilicoPep;
            if (BaseSequence == q.BaseSequence && Protease == q.Protease)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
      

        public override int GetHashCode()
        {
            return BaseSequence.GetHashCode() + Protease.GetHashCode();

        }


    }
}
