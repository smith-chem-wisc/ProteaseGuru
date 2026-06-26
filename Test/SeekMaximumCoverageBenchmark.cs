using System.Diagnostics;
using NUnit.Framework;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using Tasks;

namespace Test;

/// <summary>
/// Before/after benchmark for the "seek maximum coverage" path. Marked [Explicit] so it
/// stays out of the normal suite; run it with:
///   dotnet test --filter "FullyQualifiedName~SeekMaximumCoverageBenchmark"
///
/// It isolates the two things the ProteaseSearchSpeedups branch changed:
///   #2  set-cover math   — old HashSet greedy/brute-force (copied from the pre-change code)
///                          vs the new ulong[] bitset versions.
///   #1+#3 digestion path — re-digesting every protease on every refresh (the old GUI
///                          behavior) vs the cache + parallel miss-only digestion.
///
/// Environment overrides (both optional):
///   BENCH_FASTA  path to a FASTA file; its first record is added as a real-protein row.
///   BENCH_CORES  simulated logical-core count (caps parallel degree). Defaults to all cores.
/// Synthetic proteins are built by repeating a real sequence so the core benchmark needs
/// no external database.
/// </summary>
[TestFixture]
[Explicit("Performance benchmark; run explicitly via --filter")]
[NonParallelizable]
public class SeekMaximumCoverageBenchmark
{
    // Human hemoglobin beta chain (146 aa); repeated to reach a target length.
    private const string Unit =
        "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";

    private const int DigestIters = 4;
    private const int AlgoIters = 25;

    private static long _sink;

    private static Protein SyntheticProtein(int approxLength)
    {
        int repeats = Math.Max(1, approxLength / Unit.Length);
        var seq = string.Concat(Enumerable.Repeat(Unit, repeats));
        return new Protein(seq, "BENCH_" + seq.Length);
    }

    private static Protein? LoadFastaProtein(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        string accession = "FASTA";
        var seq = new System.Text.StringBuilder();
        bool started = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith(">"))
            {
                if (started) break; // first record only
                started = true;
                var parts = line.TrimStart('>').Split('|');
                accession = parts.Length >= 2 ? parts[1] : parts[0].Split(' ')[0];
            }
            else
            {
                seq.Append(line.Trim());
            }
        }
        return seq.Length == 0 ? null : new Protein(seq.ToString(), accession);
    }

    private static List<ProteaseSpecificParameters> BuildProteases()
    {
        string[] candidates =
        {
            "trypsin|P", "chymotrypsin|P", "Asp-N", "Glu-C", "Lys-C|P", "Arg-C",
            "elastase|P", "Lys-N", "CNBr", "Glu-C (with asp)", "ProAlanase", "subtilisin|P"
        };

        var list = new List<ProteaseSpecificParameters>();
        foreach (var name in candidates)
        {
            try
            {
                var dp = new DigestionParams(
                    protease: name, maxMissedCleavages: 2, minPeptideLength: 7, maxPeptideLength: 50);
                list.Add(new ProteaseSpecificParameters(dp));
            }
            catch
            {
                // name not present in this ProteaseDictionary build — skip it
            }
        }
        return list;
    }

    private static int SimulatedCores()
    {
        var raw = Environment.GetEnvironmentVariable("BENCH_CORES");
        return int.TryParse(raw, out int c) && c > 0 ? c : Environment.ProcessorCount;
    }

    private static double MeasureMs(int iterations, Action body)
    {
        body(); // warm up (JIT + first-touch allocations)
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            body();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / iterations;
    }

    [Test]
    public void Benchmark()
    {
        var analyzer = new SeekMaximumCoverage();
        var proteases = BuildProteases();
        int simCores = SimulatedCores();

        TestContext.WriteLine($"Host cores: {Environment.ProcessorCount}   Simulated cores: {simCores}   Proteases: {proteases.Count}");
        TestContext.WriteLine($"  ({string.Join(", ", proteases.Select(p => p.DigestionAgentName))})");
        TestContext.WriteLine("  Note: parallel degree is capped to the simulated core count; absolute ms still");
        TestContext.WriteLine("  reflect this host's per-core speed, so read the speedup ratios, not raw ms.");
        TestContext.WriteLine("");

        foreach (int size in new[] { 1500, 6000, 18000 })
            BenchmarkProtein(analyzer, proteases, SyntheticProtein(size), simCores);

        // Real protein row, if a FASTA was supplied.
        var fasta = Environment.GetEnvironmentVariable("BENCH_FASTA") ?? "";
        var realProtein = LoadFastaProtein(fasta);
        if (realProtein != null)
            BenchmarkProtein(analyzer, proteases, realProtein, simCores, label: $"{realProtein.Accession} (real)");
        else if (!string.IsNullOrWhiteSpace(fasta))
            TestContext.WriteLine($"(BENCH_FASTA set but no protein loaded from '{fasta}')\n");

        // Session simulations (#1 + #3): toggle-only refresh sequences.
        BenchmarkSession(analyzer, proteases, SyntheticProtein(6000), "6000 aa synthetic", simCores);
        if (realProtein != null)
            BenchmarkSession(analyzer, proteases, realProtein, $"{realProtein.Accession} ({realProtein.Length} aa, real)", simCores);

        TestContext.WriteLine($"(checksum {_sink})"); // keep the JIT from eliding the work
    }

    private void BenchmarkProtein(
        SeekMaximumCoverage analyzer, List<ProteaseSpecificParameters> proteases,
        Protein protein, int simCores, string? label = null)
    {
        int len = protein.Length;

        // ---- Digestion (#3): serial vs parallel, one full pass over all proteases ----
        double serialDigestMs = MeasureMs(DigestIters, () =>
        {
            long s = 0;
            foreach (var p in proteases)
                s += analyzer.DigestSingle(protein, p).Coverage.Count;
            _sink += s;
        });

        double parallelDigestMs = MeasureMs(DigestIters, () =>
        {
            var results = new int[proteases.Count];
            Parallel.For(0, proteases.Count,
                new ParallelOptions { MaxDegreeOfParallelism = simCores },
                i => results[i] = analyzer.DigestSingle(protein, proteases[i]).Coverage.Count);
            _sink += results.Sum();
        });

        var cov = analyzer.CalculateCoverageByProtease(protein, proteases);
        int totalResidues = cov.Values.SelectMany(s => s).DefaultIfEmpty(-1).Max() + 1;

        // ---- Set-cover (#2): old HashSet vs new bitset ----
        double greedyOldMs = MeasureMs(AlgoIters, () => _sink += OldGreedyCoverageCount(cov));
        double greedyNewMs = MeasureMs(AlgoIters, () => _sink += analyzer.GreedyMinimumProteaseSet(cov).CoveredResidues.Count);
        double tripOldMs = MeasureMs(AlgoIters, () => _sink += OldBestCombinationCount(cov, 3));
        double tripNewMs = MeasureMs(AlgoIters, () => _sink += analyzer.BestTriplet(cov).CoverageCount);

        // Equivalence: identical inputs must yield identical maximum coverage.
        Assert.That(analyzer.GreedyMinimumProteaseSet(cov).CoveredResidues.Count,
            Is.EqualTo(OldGreedyCoverageCount(cov)), $"greedy mismatch at {len} aa");
        Assert.That(analyzer.BestTriplet(cov).CoverageCount,
            Is.EqualTo(OldBestCombinationCount(cov, 3)), $"triplet mismatch at {len} aa");

        string title = label ?? $"{len} aa synthetic";
        TestContext.WriteLine($"Protein {title,-22}  ({len} aa, covered residues: {totalResidues})");
        TestContext.WriteLine($"  digest all proteases   serial {serialDigestMs,8:F2} ms   parallel {parallelDigestMs,8:F2} ms   ({Ratio(serialDigestMs, parallelDigestMs)})");
        TestContext.WriteLine($"  greedy set cover       old    {greedyOldMs,8:F3} ms   new      {greedyNewMs,8:F3} ms   ({Ratio(greedyOldMs, greedyNewMs)})");
        TestContext.WriteLine($"  best triplet           old    {tripOldMs,8:F3} ms   new      {tripNewMs,8:F3} ms   ({Ratio(tripOldMs, tripNewMs)})");
        TestContext.WriteLine("");
    }

    private void BenchmarkSession(
        SeekMaximumCoverage analyzer, List<ProteaseSpecificParameters> proteases,
        Protein protein, string label, int simCores)
    {
        const int refreshes = 9; // 1 initial render + 8 mode/view toggles

        double oldMs = MeasureMs(1, () =>
        {
            for (int r = 0; r < refreshes; r++)
                foreach (var p in proteases)
                    _sink += analyzer.DigestSingle(protein, p).Coverage.Count;
        });

        double newMs = MeasureMs(1, () =>
        {
            var cache = new Dictionary<string, int>();
            for (int r = 0; r < refreshes; r++)
            {
                var misses = proteases.Where(p => !cache.ContainsKey(p.DigestionAgentName)).ToList();
                if (misses.Count > 0)
                {
                    var computed = new int[misses.Count];
                    Parallel.For(0, misses.Count,
                        new ParallelOptions { MaxDegreeOfParallelism = simCores },
                        i => computed[i] = analyzer.DigestSingle(protein, misses[i]).Coverage.Count);
                    for (int i = 0; i < misses.Count; i++)
                        cache[misses[i].DigestionAgentName] = computed[i];
                }
                foreach (var p in proteases)
                    _sink += cache[p.DigestionAgentName];
            }
        });

        TestContext.WriteLine($"Session: {refreshes} refreshes of {proteases.Count} proteases @ {label} (toggles only)");
        TestContext.WriteLine($"  old (re-digest every refresh, serial)   {oldMs,9:F2} ms");
        TestContext.WriteLine($"  new (cache + parallel miss digestion)   {newMs,9:F2} ms   ({Ratio(oldMs, newMs)})");
        TestContext.WriteLine("");
    }

    private static string Ratio(double oldMs, double newMs)
        => newMs <= 0 ? "n/a" : $"{oldMs / newMs:F1}x faster";

    // ----- Pre-change implementations, copied verbatim for an honest comparison -----

    private static int OldGreedyCoverageCount(Dictionary<string, HashSet<int>> coverageDict)
    {
        var totalCovered = new HashSet<int>();
        var remaining = coverageDict.ToDictionary(kvp => kvp.Key, kvp => new HashSet<int>(kvp.Value));

        while (true)
        {
            string? best = null;
            int bestCount = 0;
            HashSet<int>? bestResidues = null;

            foreach (var kvp in remaining)
            {
                var newResidues = new HashSet<int>(kvp.Value);
                newResidues.ExceptWith(totalCovered);
                if (newResidues.Count > bestCount)
                {
                    best = kvp.Key;
                    bestCount = newResidues.Count;
                    bestResidues = newResidues;
                }
            }

            if (best == null || bestCount == 0)
                break;

            totalCovered.UnionWith(bestResidues!);
            remaining.Remove(best);
        }

        return totalCovered.Count;
    }

    private static int OldBestCombinationCount(Dictionary<string, HashSet<int>> coverageDict, int combinationSize)
    {
        var names = coverageDict.Keys.ToList();
        int best = -1;

        foreach (var combination in OldCombinations(names, combinationSize))
        {
            var combined = new HashSet<int>();
            foreach (var protease in combination)
                combined.UnionWith(coverageDict[protease]);
            if (combined.Count > best)
                best = combined.Count;
        }

        return best < 0 ? 0 : best;
    }

    private static IEnumerable<IEnumerable<T>> OldCombinations<T>(List<T> list, int length)
    {
        if (length == 0)
        {
            yield return Enumerable.Empty<T>();
            yield break;
        }

        for (int i = 0; i <= list.Count - length; i++)
        {
            var head = list[i];
            var tail = list.Skip(i + 1).ToList();
            foreach (var combination in OldCombinations(tail, length - 1))
                yield return new[] { head }.Concat(combination);
        }
    }
}
