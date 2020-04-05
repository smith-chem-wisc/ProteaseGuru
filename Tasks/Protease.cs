using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    class Protease
    {
        public Protease(string name, CleavageSpecificity cleavageSpecificity, string psiMSAccessionNumber, string psiMSName, List<DigestionMotif> motifList)
        {
            Name = name;
            CleavageSpecificity = cleavageSpecificity;
            PsiMsAccessionNumber = psiMSAccessionNumber;
            PsiMsName = psiMSName;
            DigestionMotifs = motifList ?? new List<DigestionMotif>();
        }

        public string Name { get; }
        public CleavageSpecificity CleavageSpecificity { get; }
        public string PsiMsAccessionNumber { get; }
        public string PsiMsName { get; }
        public List<DigestionMotif> DigestionMotifs { get; }

        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            return obj is Protease a
                && (a.Name == null && Name == null || a.Name.Equals(Name));
        }

        public override int GetHashCode()
        {
            return (Name ?? "").GetHashCode();
        }
    }
}
