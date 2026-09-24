using System.Globalization;
using System.Numerics;

namespace ProteaseGuru.GuiFunctions;

/// <summary>
/// Decides what a piece of integer text should become once bounds are applied. Kept apart from the
/// text box that uses it so the decision can be exercised without a WPF control.
/// </summary>
public static class IntegerTextBounds
{
    /// <summary>
    /// Returns <paramref name="text"/> clamped into [<paramref name="lowerBound"/>,
    /// <paramref name="upperBound"/>], or unchanged when there is nothing to clamp.
    ///
    /// Parsing goes through <see cref="BigInteger"/> rather than <see cref="int"/> so that a value
    /// too large to fit still carries its sign and clamps to the bound it actually overshot —
    /// int.TryParse collapses "too big" and "not a number" into the same failure. Text that is not
    /// a number at all comes back untouched, so a TwoWay binding fails its conversion and leaves the
    /// source property alone; synthesizing a value here would commit one the user never entered.
    /// </summary>
    public static string Clamp(string? text, int lowerBound, int upperBound)
    {
        string original = text ?? string.Empty;
        string trimmed = original.Trim();
        if (trimmed.Length == 0)
            return original;

        // Bounds configured backwards can only come from a XAML typo, and BigInteger.Clamp throws
        // on them. Pick the lower bound rather than letting that escape a UI callback.
        if (upperBound < lowerBound)
            return lowerBound.ToString(CultureInfo.InvariantCulture);

        if (BigInteger.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return ((int)BigInteger.Clamp(parsed, lowerBound, upperBound)).ToString(CultureInfo.InvariantCulture);

        return original;
    }
}
