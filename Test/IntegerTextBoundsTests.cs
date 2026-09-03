using ProteaseGuru.GuiFunctions;
using NUnit.Framework;

namespace ProteaseGuru.Test;

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
    /// Whenever LowerBound is above zero, "clamped to LowerBound" and "left alone" are easy to
    /// confuse. These use bounds spanning zero, where a negative overflow (-5) and untouched text
    /// are distinguishable.
    /// </summary>
    [TestCase("-99999999999999999999999", "-5", TestName = "NegativeOverflowClampsToLowerBoundNotZero")]
    [TestCase("-2147483649", "-5", TestName = "JustPastIntMinClampsToLowerBoundNotZero")]
    [TestCase("-3", "-3", TestName = "InRangeNegativeIsUnchanged")]
    public void NegativeHandlingIsDistinguishableFromTheFallback(string input, string expected) =>
        Assert.That(IntegerTextBounds.Clamp(input, -5, 12), Is.EqualTo(expected));

    /// <summary>
    /// Text that is not a number is handed back as-is, so the TwoWay binding fails its conversion
    /// and the source keeps the value it already had.
    /// </summary>
    [TestCase("-", TestName = "LoneDash")]
    [TestCase("abc", TestName = "Letters")]
    [TestCase("1.5", TestName = "Decimal")]
    [TestCase("1 2", TestName = "InternalWhitespace")]
    [TestCase("5,000", TestName = "ThousandsSeparator")]
    public void UnparseableTextIsReturnedUnchanged(string input) =>
        Assert.That(IntegerTextBounds.Clamp(input, Lower, Upper), Is.EqualTo(input));

    /// <summary>
    /// The peptide mass boxes declare no bounds, so they run on the dependency property defaults
    /// and encode "no limit" as -1. Resolving unparseable text to the nearest valid value lands on
    /// 0 there, which reads as a real limit: MaxPeptideMassAllowed = 0 keeps only peptides at or
    /// below mass zero, so the run silently yields nothing. Typing is filtered to digits, paste is not.
    /// </summary>
    [TestCase("5,000", TestName = "ThousandsSeparator")]
    [TestCase("abc", TestName = "Letters")]
    [TestCase("1 2", TestName = "InternalWhitespace")]
    [TestCase("-", TestName = "LoneDash")]
    public void UnparseableTextWithDefaultBoundsIsNotSynthesizedIntoZero(string input) =>
        Assert.That(IntegerTextBounds.Clamp(input, int.MinValue, int.MaxValue), Is.EqualTo(input));

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
