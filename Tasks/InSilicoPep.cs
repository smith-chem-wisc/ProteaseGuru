using System.Text;

namespace ProteaseGuru.Tasks
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
        public double ChronologerRetentionTime;
        public bool? PflyDetectability;
        public (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)? PflyProbabilities;
        public int Length;
        public double MolecularWeight;
        public string Database;
        public string Protein;
        public string ProteinName;
        public int StartResidue;
        public int EndResidue;
        public string Protease;

        public InSilicoPep(string baseSequence, string fullSequence, char previousAA, char nextAA, bool unique, double hydrophobicity, double electrophoreticMobility,
            double chronologerRetentionTime, bool? pflyDetectability, int length, double molecularWeight, string database, string protein, string proteinName, int start, int end, string protease,
            (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)? pflyProbabilities = null)
        {
            BaseSequence = baseSequence;
            FullSequence = fullSequence;
            PreviousAA = previousAA;
            NextAA = nextAA;
            Unique = unique;
            Hydrophobicity = hydrophobicity;
            ElectrophoreticMobility = electrophoreticMobility;
            ChronologerRetentionTime = chronologerRetentionTime;
            PflyDetectability = pflyDetectability;
            PflyProbabilities = pflyProbabilities;
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
            sb.Append(tab);
            sb.Append(ChronologerRetentionTime);
            sb.Append(tab);
            sb.Append(PflyDetectability);
            sb.Append(tab);
            sb.Append(PflyProbabilities?.NotDetectable);
            sb.Append(tab);
            sb.Append(PflyProbabilities?.LowDetectability);
            sb.Append(tab);
            sb.Append(PflyProbabilities?.IntermediateDetectability);
            sb.Append(tab);
            sb.Append(PflyProbabilities?.HighDetectability);
            return sb.ToString();
        }
        public override bool Equals(object? obj)
        {
            if (obj is not InSilicoPep q)
                return false;

            return BaseSequence == q.BaseSequence
                && Protease == q.Protease
                && StartResidue == q.StartResidue
                && EndResidue == q.EndResidue;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(BaseSequence, Protease, StartResidue, EndResidue);
        }
    }
}
