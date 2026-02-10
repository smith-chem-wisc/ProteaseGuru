using BenchmarkDotNet.Attributes;
using Chromatography.RetentionTimePrediction.Chronologer;
using Omics.Digestion;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Omics.Modifications;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VSDiagnostics;

namespace Benchmarks
{
    [CPUUsageDiagnoser]
    public class BatchCalculateChronologerBenchmark
    {
        private List<PeptideWithSetModifications> _peptides;
        [GlobalSetup]
        public void Setup()
        {
            // Create test proteins with realistic sequences
            var sequences = new[]
            {
                "MSKGEELFTGVVPILVELDGDVNGHKFSVSGEGEGDATYGKLTLKFICTTGKLPVPWPTLVTTLTYGVQCFSRYPDHMKQHDFFKSAMPEGYVQERTIFFKDDGNYKTRAEVKFEGDTLVNRIELKGIDFKEDGNILGHKLEYNYNSHNVYIMADKQKNGIKVNFKIRHNIEDGSVQLADHYQQNTPIGDGPVLLPDNHYLSTQSALSKDPNEKRDHMVLLEFVTAAGITLGMDELYK",
                "MVLSPADKTNVKAAWGKVGAHAGEYGAEALERMFLSFPTTKTYFPHFDLSHGSAQVKGHGKKVADALTNAVAHVDDMPNALSALSDLHAHKLRVDPVNFKLLSHCLLVTLAAHLPAEFTPAVHASLDKFLASVSTVLTSKYR",
                "GLSDGEWQQVLNVWGKVEADIAGHGQEVLIRLFTGHPETLEKFDKFKHLKTEAEMKASEDLKKHGTVVLTALGGILKKKGHHEAELKPLAQSHATKHKIPIKYLEFISDAIIHVLHSKHPGDFGADAQGAMTKALELFRNDIAAKYKELGFQG"
            };
            var proteins = sequences.Select((seq, i) => new Protein(seq, $"TEST{i:D4}", name: $"Test Protein {i}")).ToList();
            // Create mock peptides from the sequences
            _peptides = new List<PeptideWithSetModifications>();
            foreach (var protein in proteins)
            {
                var sequence = protein.BaseSequence;
                int start = 0;
                for (int i = 0; i < sequence.Length; i++)
                {
                    if ((sequence[i] == 'K' || sequence[i] == 'R') && i > start + 6)
                    {
                        var peptideSequence = sequence.Substring(start, i - start + 1);
                        if (peptideSequence.Length >= 7 && peptideSequence.Length <= 50)
                        {
                            var pwsm = new PeptideWithSetModifications(protein, new DigestionParams(), start + 1, i + 1, CleavageSpecificity.Full, "", 0, new Dictionary<int, Modification>(), 0);
                            _peptides.Add(pwsm);
                        }

                        start = i + 1;
                    }
                }
            }
        }

        [Benchmark]
        public double[] BatchCalculateRetentionTimesChronologer()
        {
            var rtPredictor = new ChronologerRetentionTimePredictor();
            var results = new double[_peptides.Count];
            for (int i = 0; i < _peptides.Count; i++)
            {
                var result = rtPredictor.PredictRetentionTime(_peptides[i], out var failureReason);
                results[i] = result ?? -1;
            }

            return results;
        }
    }
}
