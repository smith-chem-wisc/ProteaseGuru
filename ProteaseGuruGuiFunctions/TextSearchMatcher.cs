namespace ProteaseGuru.GuiFunctions;

/// <summary>
/// Provides the shared matching behavior used by text-filtered GUI lists.
/// </summary>
public static class TextSearchMatcher
{
    /// <summary>
    /// Returns whether <paramref name="value"/> matches a user-entered search.
    /// Empty or whitespace-only searches match every value.
    /// </summary>
    public static bool Matches(string? value, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return value?.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    }
}
