using ProteaseGuru.Engine;
using Omics;
using Omics.Digestion;
using Proteomics.ProteolyticDigestion;

namespace ProteaseGuru.Tasks;

/// <summary>
/// Caches per-protease digestion results for a single biopolymer so that refreshes which do not
/// change the digestion — algorithm toggles, view switches — reuse the previous result instead of
/// re-digesting. Only proteases whose settings actually changed are recomputed, and those are
/// digested in parallel.
///
/// The cache is scoped to one biopolymer and resets when a different one is passed in.
/// </summary>
public class ProteaseDigestCache
{
    private readonly SeekMaximumCoverage _seeker;
    private readonly Dictionary<DigestCacheKey, (HashSet<int> Coverage, List<(int Start, int End)> Intervals)> _cache = new();
    private IBioPolymer? _cachedFor;

    public ProteaseDigestCache(SeekMaximumCoverage seeker) => _seeker = seeker;

    /// <summary>Number of entries currently held. Grows as digestion settings are varied.</summary>
    public int Count => _cache.Count;

    // Matches how DigestionTask bounds its own concurrency.
    private static int MaxConcurrency => Math.Max(1, GlobalVariables.MaxThreads);

    /// <summary>
    /// Identifies a digestion. The digestion parameters are held as a clone because the view model
    /// mutates them in place, so a live reference would go stale; cloning captures them by value.
    /// Equality therefore covers every field <see cref="IDigestionParams"/> compares, rather than a
    /// hand-picked subset that has to be extended whenever a new setting is exposed.
    /// </summary>
    public readonly record struct DigestCacheKey(IDigestionParams DigestionParams, string ModsSignature);

    /// <summary>
    /// Returns coverage sets and peptide intervals for the given proteases, serving cached results
    /// where possible and digesting only the proteases whose settings aren't cached.
    /// </summary>
    public (Dictionary<string, HashSet<int>> Coverage,
            Dictionary<string, List<(int Start, int End)>> Intervals)
        GetCoverageAndIntervals(IBioPolymer biopolymer, IReadOnlyList<ProteaseSpecificParameters> proteaseParams)
    {
        if (!ReferenceEquals(biopolymer, _cachedFor))
        {
            _cache.Clear();
            _cachedFor = biopolymer;
        }

        var keys = new DigestCacheKey[proteaseParams.Count];
        for (int i = 0; i < proteaseParams.Count; i++)
            keys[i] = BuildKey(proteaseParams[i]);

        var misses = new List<int>();
        for (int i = 0; i < proteaseParams.Count; i++)
            if (!_cache.ContainsKey(keys[i]))
                misses.Add(i);

        if (misses.Count > 0)
        {
            var computed = new (HashSet<int> Coverage, List<(int Start, int End)> Intervals)[misses.Count];

            // Safe to run concurrently on one biopolymer: it is only read, and the one piece of
            // caller state the digest writes to — the fixed-mod list it appends the cleavage mod
            // to — belongs to a single protease, so no two iterations touch it.
            Parallel.For(0, misses.Count,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency },
                m => computed[m] = _seeker.DigestSingle(biopolymer, proteaseParams[misses[m]]));

            for (int m = 0; m < misses.Count; m++)
                _cache[keys[misses[m]]] = computed[m];
        }

        var coverage = new Dictionary<string, HashSet<int>>(proteaseParams.Count);
        var intervals = new Dictionary<string, List<(int Start, int End)>>(proteaseParams.Count);
        for (int i = 0; i < proteaseParams.Count; i++)
        {
            var entry = _cache[keys[i]];
            string name = proteaseParams[i].DigestionAgentName;
            coverage[name] = entry.Coverage;
            intervals[name] = entry.Intervals;
        }
        return (coverage, intervals);
    }

    public static DigestCacheKey BuildKey(ProteaseSpecificParameters p) =>
        new(p.DigestionParams.Clone(), BuildModSignature(p));

    /// <summary>
    /// Builds the mod half of the key as an order-independent, deduplicated signature of the mods
    /// the digest will actually apply.
    ///
    /// Protein digestion appends the protease's cleavage modification to the fixed-mod list it is
    /// handed, so that list differs before and after the first digest — and the analyzer, digestion
    /// task, and library exporter all hand it the same list. Folding that mod in up front keeps the
    /// signature identical no matter which of them has run. RNA digestion never applies a cleavage
    /// mod, so the fold is protein-only.
    /// </summary>
    public static string BuildModSignature(ProteaseSpecificParameters p)
    {
        var fixedModIds = p.FixedMods.Select(m => m.IdWithMotif);

        if (p.DigestionParams is DigestionParams proteinParams
            && proteinParams.Protease.CleavageMod != null)
        {
            fixedModIds = fixedModIds.Append(proteinParams.Protease.CleavageMod.IdWithMotif);
        }

        return Signature(fixedModIds) + "|" + Signature(p.VariableMods.Select(m => m.IdWithMotif));

        static string Signature(IEnumerable<string> ids) =>
            string.Join(",", ids.Distinct().OrderBy(id => id, StringComparer.Ordinal));
    }
}
