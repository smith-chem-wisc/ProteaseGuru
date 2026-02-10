using BenchmarkDotNet.Attributes;
using Proteomics.RetentionTimePrediction;
using System.Collections.Generic;
using Microsoft.VSDiagnostics;

namespace Benchmarks
{
    [CPUUsageDiagnoser]
    public class BatchCalculateHydrophobicityBenchmark
    {
        private List<string> _peptideSequences;
        
        [GlobalSetup]
        public void Setup()
        {
            // Create a realistic set of peptide sequences for benchmarking
            // These are typical tryptic peptides of varying lengths
            _peptideSequences = new List<string>
            {
                "MSKGEELFTGVVPILVELDGDVNGHK",
                "FSVSGEGEGDATYGK",
                "LTLKFICTTGK",
                "LPVPWPTLVTTLTYGVQCFSR",
                "YPDHMKQHDFFK",
                "SAMPEGYVQER",
                "TIFFKDDGNYK",
                "TRAEVKFEGDTLVNR",
                "IELKGIDFK",
                "EDGNILGHK",
                "LEYNYNSHNVYIMADKQK",
                "NGIKVNFK",
                "IRHNIEDGSVQLADHYQQNTPIGDGPVLLPDNHYLSTQSALSK",
                "DPNEKRDHMVLLEFVTAAGITLGMDELYK",
                "MVLSPADKTNVK",
                "AAWGKVGAHAGEYGAEALERMFLSFPTTK",
                "TYFPHFDLSHGSAQVK",
                "GHGKKVADALTNAVAHVDDMPNALSALSDLHAHK",
                "LRVDPVNFK",
                "LLSHCLLVTLAAHLPAEFTPAVHASLDKFLASVSTVLTSK",
                "GLSDGEWQQVLNVWGK",
                "VEADIAGHGQEVLIR",
                "LFTGHPETLEK",
                "FDKFKHLK",
                "TEAEMKASEDLK",
                "KHGTVVLTALGGILK",
                "KKGHHEAELK",
                "PLAQSHATKHKIPIK",
                "YLEFISDAIIHVLHSK",
                "HPGDFGADAQGAMTK",
                "ALELFRNDIAAKYKELGFQG"
            };
            
            // Duplicate the list to get a larger sample size (simulating real workloads)
            var originalCount = _peptideSequences.Count;
            for (int i = 0; i < 9; i++) // Makes ~310 peptides total
            {
                for (int j = 0; j < originalCount; j++)
                {
                    _peptideSequences.Add(_peptideSequences[j]);
                }
            }
        }

        [Benchmark]
        public double[] BatchCalculateHydrophobicity()
        {
            var rtPredictor = new SSRCalc3("SSRCalc 3.0 (300A)", SSRCalc3.Column.A300);
            var results = new double[_peptideSequences.Count];
            for (int i = 0; i < _peptideSequences.Count; i++)
            {
                results[i] = rtPredictor.ScoreSequence(_peptideSequences[i]);
            }

            return results;
        }
    }
}
