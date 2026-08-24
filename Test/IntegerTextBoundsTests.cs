using ProteaseGuruGuiFunctions;
using NUnit.Framework;

namespace Test;

[TestFixture]
public class IntegerTextBoundsTests
{
    // Bounds of the thread-count box on a 12-core machine.
    private const int Lower = 1;
    private const int Upper = 12;

    [TestCase("7", "7", TestName = "InRangeValueIsUnchanged")]
    [TestCase("1", "1", TestName = "ValueOnLowerBoundIsUnchanged")]
    [TestCase("12", "12", TestName = "ValueOnUpperBoundIsUnchanged")]
    [TestCase("0", "1", TestName = "BelowLowerBoundClampsUp")]
    [TestCase("500", "12", TestName = "AboveUpperBoundClampsDown")]
    [TestCase("-4", "1", TestName = "NegativeClampsToLowerBound")]
    public void ClampsIntoRange(string input, string expected) =>
        Assert.That(IntegerTextBounds.Clamp(input, Lower, Upper), Is.EqualTo(expected));

    /// <summary>
    /// int.TryParse fails on these and throws away the sign with it, so clamping has to parse into
    /// something wider or a huge negative would snap to the upper bound.
    /// </summary>
    [TestCase("99999999999999999999999", "12", TestName = "PositiveOverflowClampsToUpperBound")]
    [TestCase("-99999999999999999999999", "1", TestName = "NegativeOverflowClampsToLowerBound")]
    [TestCase("2147483648", "12", TestName = "JustPastIntMaxClampsToUpperBound")]
    [TestCase("-2147483649", "1", TestName = "JustPastIntMinClampsToLowerBound")]
    public void ClampsValuesTooLargeForInt(string input, string expected) =>
        Assert.That(IntegerTextBounds.Clamp(input, Lower, Upper), Is.EqualTo(expected));

    /// <summary>
    /// Whenever LowerBound is above zero, "clamped to LowerBound" and "unparseable, resolved to the
    /// nearest valid value" collapse to the same string, so neither case really pins the other
    /// down. These use bounds spanning zero, where a negative overflow (-5) and unparseable
    /// text (0) are distinguishable.
    /// </summary>
    [TestCase("-99999999999999999999999", "-5", TestName = "NegativeOverflowClampsToLowerBoundNotZero")]
    [TestCase("-2147483649", "-5", TestName = "JustPastIntMinClampsToLowerBoundNotZero")]
    [TestCase("-", "0", TestName = "LoneDashResolvesToZeroNotLowerBound")]
    [TestCase("-3", "-3", TestName = "InRangeNegativeIsUnchanged")]
    public void NegativeHandlingIsDistinguishableFromTheFallback(string input, string expected) =>
        Assert.That(IntegerTextBounds.Clamp(input, -5, 12), Is.EqualTo(expected));

    [TestCase("-", TestName = "LoneDash")]
    [TestCase("abc", TestName = "Letters")]
    [TestCase("1.5", TestName = "Decimal")]
    [TestCase("1 2", TestName = "InternalWhitespace")]
    public void UnparseableTextResolvesToNearestValidValue(string input) =>
        Assert.That(IntegerTextBounds.Clamp(input, Lower, Upper), Is.EqualTo("1"));

    /// <summary>
    /// With no bounds set the dependency properties default to int.MinValue/int.MaxValue, so
    /// unparseable text has to resolve to 0 — clamping it to the lower bound would put
    /// "-2147483648" in the box.
    /// </summary>
    [Test]
    public void UnparseableTextWithDefaultBoundsResolvesToZero() =>
        Assert.That(IntegerTextBounds.Clamp("-", int.MinValue, int.MaxValue), Is.EqualTo("0"));

    [TestCase("", TestName = "Empty")]
    [TestCase("   ", TestName = "WhitespaceOnly")]
    public void EmptyTextIsReturnedUnchanged(string input) =>
        Assert.That(IntegerTextBounds.Clamp(input, Lower, Upper), Is.EqualTo(input));

    [Test]
    public void NullTextIsTreatedAsEmpty() =>
        Assert.That(IntegerTextBounds.Clamp(null, Lower, Upper), Is.EqualTo(string.Empty));

    [Test]
    public void SurroundingWhitespaceIsIgnoredWhenParsing() =>
        Assert.That(IntegerTextBounds.Clamp("  8  ", Lower, Upper), Is.EqualTo("8"));

    /// <summary>
    /// A control whose bounds are configured backwards must not throw out of a UI callback.
    /// </summary>
    [Test]
    public void InvertedBoundsResolveToLowerBound() =>
        Assert.That(IntegerTextBounds.Clamp("7", 10, 5), Is.EqualTo("10"));
}
