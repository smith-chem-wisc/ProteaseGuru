using Chemistry;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    [Serializable]
    //peptide object that hold additional information that we want to report for each peptide. Inherits from PeptideWithSetModifications
    public class InSilicoPeptide: PeptideWithSetModifications
    {
        bool UniquePeptide { get; }
        double Hydrophobicity { get; set; }
        double ElectrophoreticMobility { get; set; }
        double IsoelectricPoint { get; set; }
        [NonSerialized] private Dictionary<int, Modification> _allModsOneIsNterminus; //we currently only allow one mod per position
        [NonSerialized] private bool? _hasChemicalFormulas;
        [NonSerialized] private string _sequenceWithChemicalFormulas;
        [NonSerialized] private double? _monoisotopicMass;
        [NonSerialized] private DigestionParams _digestionParams;
        private static readonly double WaterMonoisotopicMass = PeriodicTable.GetElement("H").PrincipalIsotope.AtomicMass * 2 + PeriodicTable.GetElement("O").PrincipalIsotope.AtomicMass;
        private readonly string ProteinAccession; // used to get protein object after deserialization

        public InSilicoPeptide(Protein protein, DigestionParams digestionParams, int oneBasedStartResidueInProtein, int oneBasedEndResidueInProtein, CleavageSpecificity cleavageSpecificity,
            string peptideDescription, int missedCleavages, Dictionary<int,Modification> allModsOneIsNterminus, int numFixedMods, string baseSeqeunce, bool isPeptideUnique) : base(protein, digestionParams, oneBasedStartResidueInProtein, oneBasedEndResidueInProtein,
                cleavageSpecificity, peptideDescription, missedCleavages, allModsOneIsNterminus, numFixedMods, baseSeqeunce)
        {
            _allModsOneIsNterminus = allModsOneIsNterminus;            
            _digestionParams = digestionParams;
            ProteinAccession = protein.Accession;
            UniquePeptide = isPeptideUnique;
        }

        public double GetHydrophobicity()
        {
            return Hydrophobicity;
        }
        public void  SetHydrophobicity(double hydro)
        {
            Hydrophobicity = hydro;
        }
        public double GetElectrophoreticMobility()
        {
            return ElectrophoreticMobility;
        }
        public void SetElectrophoreticMobility(double em)
        {
           ElectrophoreticMobility = em;
        }
        public double GetIsoelectricPoint()
        {
            return IsoelectricPoint;
        }
        public void SetIsoelectricPoint(double ip)
        {
            IsoelectricPoint = ip;
        }
        public bool GetUniquePeptide()
        {
            return UniquePeptide;
        }
    }
}
