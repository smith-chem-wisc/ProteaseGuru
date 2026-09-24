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
/// The cache is scoped to one biopolymer and resets when a different one is passed in. Within a
/// biopolymer it holds at most one entry per protease: the length and missed-cleavage boxes commit
/// on every keystroke, so keeping superseded settings would retain a coverage set and interval list
/// for every intermediate value the user typed.
/// </summary>
public class ProteaseDigestCache
{
    private readonly SeekMaximumCoverage _seeker;
    private readonly Dictionary<string, (DigestCacheKey Key, HashSet<int> Coverage, List<(int Start, int End)> Intervals)> _cache = new();
    private IBioPolymer? _cachedFor;

    public ProteaseDigestCache(SeekMaximumCoverage seeker) => _seeker = seeker;

    /// <summary>Number of entries currently held: one per protease seen for this biopolymer.</summary>
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

        // Held per request rather than read back out of the cache at the end: one slot per name
        // means a repeated protease name would otherwise hand every occurrence whichever settings
        // happened to be cached last.
        var entries = new (HashSet<int> Coverage, List<(int Start, int End)> Intervals)[proteaseParams.Count];

        var misses = new List<int>();
        for (int i = 0; i < proteaseParams.Count; i++)
        {
            if (_cache.TryGetValue(proteaseParams[i].DigestionAgentName, out var cached)
                && cached.Key.Equals(keys[i]))
                entries[i] = (cached.Coverage, cached.Intervals);
            else
                misses.Add(i);
        }

        if (misses.Count > 0)
        {
            var computed = new (HashSet<int> Coverage, List<(int Start, int End)> Intervals)[misses.Count];

            // Safe to run concurrently on one biopolymer: it is only read, and the one piece of
            // caller state the digest writes to — the fixed-mod list it appends the cleavage mod
            // to — belongs to a single protease, so no two iterations touch it.
            Parallel.For(0, misses.Count,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency },
                m => computed[m] = _seeker.DigestSingle(biopolymer, proteaseParams[misses[m]]));

            // One slot per protease, so a new settings key replaces the superseded entry instead
            // of joining it.
            for (int m = 0; m < misses.Count; m++)
            {
                int i = misses[m];
                entries[i] = computed[m];
                _cache[proteaseParams[i].DigestionAgentName] =
                    (keys[i], computed[m].Coverage, computed[m].Intervals);
            }
        }

        var coverage = new Dictionary<string, HashSet<int>>(proteaseParams.Count);
        var intervals = new Dictionary<string, List<(int Start, int End)>>(proteaseParams.Count);
        for (int i = 0; i < proteaseParams.Count; i++)
        {
            string name = proteaseParams[i].DigestionAgentName;
            coverage[name] = entries[i].Coverage;
            intervals[name] = entries[i].Intervals;
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
