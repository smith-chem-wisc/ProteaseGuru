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
        // Null when no retention time was predicted, whether the model failed or never ran.
        public double? ChronologerRetentionTime;
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
        // Null when the results file it was read from has no count.
        public int? MissedCleavages;

        public InSilicoPep(string baseSequence, string fullSequence, char previousAA, char nextAA, bool unique, double hydrophobicity, double electrophoreticMobility,
            double? chronologerRetentionTime, bool? pflyDetectability, int length, double molecularWeight, string database, string protein, string proteinName, int start, int end, string protease,
            (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)? pflyProbabilities = null,
            int? missedCleavages = null)
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
            MissedCleavages = missedCleavages;
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
            sb.Append(ChronologerRetentionTime ?? double.NaN);
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
            sb.Append(tab);
            sb.Append(MissedCleavages);
            return sb.ToString();
        }

        /// <summary>
        /// The header line for the rows that <see cref="ToString"/> writes.
        /// </summary>
        public static string TsvHeader(double detectabilityThreshold) => string.Join("\t",
            "Database", "Protease", "Base Sequence", "Full Sequence", "Previous Amino Acid",
            "Next Amino Acid", "Start Residue", "End Residue", "Length", "Molecular Weight",
            "Protein Accession", "Protein Name", "Unique Peptide (in this database)",
            "Unique Peptide (in all databases)", "Peptide sequence exclusive to this Database",
            "Hydrophobicity", "Electrophoretic Mobility", "Chronologer Retention Time", $"Pfly Detectability (>={detectabilityThreshold:0.0##})",
            "PFly NotDetectable Prob", "PFly Low Detectability Prob", "PFly Intermediate Detectability Prob", "PFly High Detectability Prob",
            "Missed Cleavages");

        /// <summary>
        /// Reads one data row of a results file. Returns null for a blank or truncated row. Columns
        /// added after the first seventeen may be missing from files written by earlier versions.
        /// </summary>
        public static InSilicoPep? FromTsvRow(string line, Func<string, string>? intern = null)
        {
            intern ??= value => value;
            var info = line.Split('\t');
            if (info.Length < 17)
            {
                return null;
            }

            string database = intern(info[0]);
            string protease = intern(info[1]);
            string baseSeq = intern(info[2]);
            string fullSeq = info[3];
            char previousAA = Convert.ToChar(info[4]);
            char nextAA = Convert.ToChar(info[5]);
            int start = Convert.ToInt32(info[6]);
            int end = Convert.ToInt32(info[7]);
            int length = Convert.ToInt32(info[8]);
            double molecularWeight = Convert.ToDouble(info[9]);
            string protein = intern(info[10]);
            string proteinName = intern(info[11]);
            bool unique = info[12] == "True";
            bool uniqueAll = info[13] == "True";
            bool oneDb = info[14] == "True";
            double hydrophobicity = Convert.ToDouble(info[15]);
            double electrophoreticMobility = Convert.ToDouble(info[16]);

            // Older files have no such column at all. Within the column, -1 and NaN were both
            // written for "no prediction"; neither is a retention time.
            double? chronologerRetentionTime = null;
            bool? pflyDetectability = null;
            if (info.Length > 17)
            {
                chronologerRetentionTime = RetentionTimeFromStoredValue(Convert.ToDouble(info[17]));
            }
            if (info.Length > 18 && bool.TryParse(info[18], out bool parsedDetectability))
            {
                pflyDetectability = parsedDetectability;
            }

            // Handle PFly probabilities - columns 19-22 (may be missing in older files)
            (double NotDetectable, double LowDetectability, double IntermediateDetectability, double HighDetectability)? pflyProbabilities = null;
            if (info.Length > 22)
            {
                if (double.TryParse(info[19], out double notDetectable) &&
                    double.TryParse(info[20], out double lowDetectability) &&
                    double.TryParse(info[21], out double intermediateDetectability) &&
                    double.TryParse(info[22], out double highDetectability))
                {
                    pflyProbabilities = (notDetectable, lowDetectability, intermediateDetectability, highDetectability);
                }
            }

            int? missedCleavages = null;
            if (info.Length > 23 && int.TryParse(info[23], out int parsedMissedCleavages))
            {
                missedCleavages = parsedMissedCleavages;
            }

            var pep = new InSilicoPep(baseSeq, fullSeq, previousAA, nextAA, unique, hydrophobicity, electrophoreticMobility,
                chronologerRetentionTime, pflyDetectability, length, molecularWeight, database, protein, proteinName, start, end, protease,
                pflyProbabilities, missedCleavages);
            pep.UniqueAllDbs = uniqueAll;
            pep.SeqOnlyInThisDb = oneDb;
            return pep;
        }

        /// <summary>
        /// Reads a retention time out of a results file. Files written before this column existed have
        /// no value at all. Within the column, NaN means no prediction; -1 meant the same in files
        /// written by earlier versions.
        /// </summary>
        public static double? RetentionTimeFromStoredValue(double stored) =>
            double.IsFinite(stored) && stored != -1 ? stored : null;

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
