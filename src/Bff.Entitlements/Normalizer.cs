namespace Bff.Entitlements;

/// <summary>
/// The single normalization function, shared by descriptor parsing and content
/// tag transport. In the production service normalization is applied in some
/// parse paths and not others (a known bug source); here every value on both
/// sides of a comparison has passed through exactly this function.
/// </summary>
public static class Normalizer
{
    /// <summary>
    /// Lowercases and strips whitespace and any character outside
    /// [a-z0-9-/]. Structural descriptor characters (= , ;) are preserved so
    /// whole descriptors can be normalized before parsing.
    /// </summary>
    public static string NormalizeDescriptor(string? raw)
        => Normalize(raw, keepStructural: true);

    /// <summary>
    /// Normalizes a single tag value: lowercase, [a-z0-9-/] only.
    /// Returns null for null input — invalid values are preserved as such so
    /// the matcher's explicit fail-open rule (not an exception) handles them.
    /// </summary>
    public static string? NormalizeValue(string? raw)
        => raw is null ? null : Normalize(raw, keepStructural: false);

    private static string Normalize(string? raw, bool keepStructural)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        Span<char> buffer = raw.Length <= 256 ? stackalloc char[raw.Length] : new char[raw.Length];
        var length = 0;
        foreach (var c in raw)
        {
            var lower = char.ToLowerInvariant(c);
            var keep = lower is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '/'
                       || (keepStructural && lower is '=' or ',' or ';');
            if (keep)
                buffer[length++] = lower;
        }

        return new string(buffer[..length]);
    }
}
