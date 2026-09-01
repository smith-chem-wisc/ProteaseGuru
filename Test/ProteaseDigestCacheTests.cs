using NUnit.Framework;
using Omics.Modifications;
using Proteomics;
using Proteomics.ProteolyticDigestion;
using ProteaseGuru.Tasks;
using Transcriptomics.Digestion;

namespace ProteaseGuru.Test;

[TestFixture]
[NonParallelizable]
public class ProteaseDigestCacheTests
{
    private const string SampleSequence = "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";

    private Protein _protein = null!;
    private SeekMaximumCoverage _seeker = null!;

    [SetUp]
    public void SetUp()
    {
        _protein = new Protein(SampleSequence, "HBB_HUMAN", name: "Hemoglobin subunit beta");
        _seeker = new SeekMaximumCoverage();
    }

    private static ProteaseSpecificParameters Params(
        string protease, int missedCleavages = 2, int minLength = 7, int maxLength = 50,
        List<Modification>? fixedMods = null, List<Modification>? variableMods = null) =>
        new(new DigestionParams(protease, missedCleavages, minLength, maxLength), fixedMods, variableMods);

    /// <summary>
    /// Protein digestion appends the protease's cleavage mod to the fixed-mod list it is handed,
    /// so a key built from that list flips after the first digest unless the mod is folded in up
    /// front. CNBr is the protease in this app's list that carries one.
    /// </summary>
    [Test]
    public void KeyIsStableAcrossRepeatedDigests()
    {
        foreach (string protease in new[] { "CNBr", "trypsin|P", "Arg-C" })
        {
            var p = Params(protease);

            var keyBefore = ProteaseDigestCache.BuildKey(p);
            _seeker.DigestSingle(_protein, p);
            var keyAfterFirst = ProteaseDigestCache.BuildKey(p);
            _seeker.DigestSingle(_protein, p);
            var keyAfterSecond = ProteaseDigestCache.BuildKey(p);

            Assert.That(keyAfterFirst, Is.EqualTo(keyBefore), $"key changed after digesting with {protease}");
            Assert.That(keyAfterSecond, Is.EqualTo(keyBefore), $"key changed after re-digesting with {protease}");
        }
    }

    [Test]
    public void CnBrSignatureIncludesCleavageModBeforeAnyDigest()
    {
        string signature = ProteaseDigestCache.BuildModSignature(Params("CNBr"));
        Assert.That(signature, Does.Contain("Homoserine lactone on M"));
        Assert.That(ProteaseDigestCache.BuildModSignature(Params("trypsin|P")), Is.EqualTo("|"));
    }

    [Test]
    public void ModSignatureIgnoresOrderAndDuplicates()
    {
        var carbam = Mods.GetModification("Carbamidomethyl on C");
        var ox = Mods.GetModification("Oxidation on M");

        var forward = Params("trypsin|P", fixedMods: new List<Modification> { carbam, ox });
        var reversed = Params("trypsin|P", fixedMods: new List<Modification> { ox, carbam, ox });

        Assert.That(ProteaseDigestCache.BuildModSignature(reversed),
            Is.EqualTo(ProteaseDigestCache.BuildModSignature(forward)));
    }

    /// <summary>
    /// The key holds the whole DigestionParams rather than a hand-picked subset, so a setting the
    /// old key omitted still separates two entries. MaxMods is one such field.
    /// </summary>
    [Test]
    public void KeyDistinguishesSettingsOutsideTheOldKeyFields()
    {
        var baseline = Params("trypsin|P");
        var differing = Params("trypsin|P");
        differing.DigestionParams.MaxMods = baseline.DigestionParams.MaxMods + 1;

        Assert.That(ProteaseDigestCache.BuildKey(differing),
            Is.Not.EqualTo(ProteaseDigestCache.BuildKey(baseline)));
    }

    /// <summary>
    /// Keying on a clone is only sound while Clone round-trips equality. mzLib owns that, so pin it:
    /// if a clone ever stops equalling its source, two different digestions collide on one entry.
    /// </summary>
    [Test]
    public void CloneRoundTripsEqualityForBothParamTypes()
    {
        var protein = new DigestionParams("trypsin|P", 2, 7, 50);
        var proteinClone = protein.Clone();
        Assert.That(proteinClone, Is.EqualTo(protein));
        Assert.That(proteinClone.GetHashCode(), Is.EqualTo(protein.GetHashCode()));

        var rna = new RnaDigestionParams();
        var rnaClone = rna.Clone();
        Assert.That(rnaClone, Is.EqualTo(rna));
        Assert.That(rnaClone.GetHashCode(), Is.EqualTo(rna.GetHashCode()));
    }

    [Test]
    public void RepeatedCallsWithUnchangedSettingsReuseCachedEntries()
    {
        var cache = new ProteaseDigestCache(_seeker);
        var proteaseParams = new[] { Params("trypsin|P"), Params("CNBr"), Params("Arg-C") };

        var first = cache.GetCoverageAndIntervals(_protein, proteaseParams);
        Assert.That(cache.Count, Is.EqualTo(3));

        var second = cache.GetCoverageAndIntervals(_protein, proteaseParams);
        Assert.That(cache.Count, Is.EqualTo(3), "an unchanged refresh added cache entries");

        foreach (var name in first.Coverage.Keys)
        {
            Assert.That(second.Coverage[name], Is.SameAs(first.Coverage[name]));
            Assert.That(second.Intervals[name], Is.SameAs(first.Intervals[name]));
        }
    }

    [Test]
    public void ChangingOneProteaseRedigestsOnlyThatProtease()
    {
        var cache = new ProteaseDigestCache(_seeker);
        var trypsin = Params("trypsin|P");
        var argC = Params("Arg-C");
        var proteaseParams = new[] { trypsin, argC };

        var first = cache.GetCoverageAndIntervals(_protein, proteaseParams);
        trypsin.DigestionParams.MaxMissedCleavages = 4;

        var second = cache.GetCoverageAndIntervals(_protein, proteaseParams);

        Assert.That(cache.Count, Is.EqualTo(3), "expected one new entry for the changed protease");
        Assert.That(second.Coverage["Arg-C"], Is.SameAs(first.Coverage["Arg-C"]));
        Assert.That(second.Coverage["trypsin|P"], Is.Not.SameAs(first.Coverage["trypsin|P"]));
    }

    [Test]
    public void SwitchingBiopolymerResetsTheCache()
    {
        var cache = new ProteaseDigestCache(_seeker);
        var proteaseParams = new[] { Params("trypsin|P") };

        cache.GetCoverageAndIntervals(_protein, proteaseParams);
        Assert.That(cache.Count, Is.EqualTo(1));

        var other = new Protein(SampleSequence, "OTHER");
        cache.GetCoverageAndIntervals(other, proteaseParams);
        Assert.That(cache.Count, Is.EqualTo(1), "cache should reset rather than grow across proteins");
    }

    /// <summary>
    /// The cache digests misses concurrently. Verify that path — with CNBr included, since its
    /// digest writes back into the parameters it is handed — matches a serial digest.
    /// </summary>
    [Test]
    public void ParallelCacheMissesMatchSerialResults()
    {
        var proteaseNames = new[] { "trypsin|P", "chymotrypsin|P", "Asp-N", "Glu-C", "Lys-C|P", "Arg-C", "CNBr" };

        var serial = proteaseNames.ToDictionary(n => n, n => _seeker.DigestSingle(_protein, Params(n)));

        for (int iteration = 0; iteration < 25; iteration++)
        {
            var cache = new ProteaseDigestCache(_seeker);
            var result = cache.GetCoverageAndIntervals(
                _protein, proteaseNames.Select(n => Params(n)).ToList());

            foreach (var name in proteaseNames)
            {
                Assert.That(result.Coverage[name], Is.EquivalentTo(serial[name].Coverage),
                    $"parallel coverage diverged for {name} on iteration {iteration}");
                Assert.That(result.Intervals[name], Is.EqualTo(serial[name].Intervals),
                    $"parallel intervals diverged for {name} on iteration {iteration}");
            }
        }
    }
}
