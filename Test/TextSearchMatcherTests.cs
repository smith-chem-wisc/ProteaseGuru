using NUnit.Framework;
using ProteaseGuru.GuiFunctions;

namespace ProteaseGuru.Test;

[TestFixture]
public class TextSearchMatcherTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void EmptySearchMatchesEveryValue(string? searchText)
    {
        Assert.That(TextSearchMatcher.Matches("P12345", searchText), Is.True);
    }

    [TestCase("p123")]
    [TestCase("  p123  ")]
    [TestCase("345")]
    public void SearchIsCaseInsensitiveAndIgnoresSurroundingWhitespace(string searchText)
    {
        Assert.That(TextSearchMatcher.Matches("P12345", searchText), Is.True);
    }

    [Test]
    public void NonMatchingSearchDoesNotMatch()
    {
        Assert.That(TextSearchMatcher.Matches("P12345", "P999"), Is.False);
    }

    [Test]
    public void MissingValueDoesNotMatchASearch()
    {
        Assert.That(TextSearchMatcher.Matches(null, "P1"), Is.False);
    }
}
