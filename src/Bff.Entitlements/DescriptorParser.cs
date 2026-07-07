namespace Bff.Entitlements;

/// <summary>
/// Result of parsing a publication descriptor such as
/// "LearningMaterial=refleks;Subject=naturfag".
/// </summary>
public sealed record ParsedDescriptor
{
    public required bool SyntaxValid { get; init; }

    /// <summary>Tag constraints keyed by known tag type, values normalized.</summary>
    public required IReadOnlyDictionary<TagType, IReadOnlyList<string?>> Grants { get; init; }

    /// <summary>Descriptor keys that do not map to any TagType — silently dropped
    /// in production, recorded here so the trace and linter can surface them.</summary>
    public required IReadOnlyList<string> UnknownKeys { get; init; }
}

/// <summary>
/// Parses and validates publication descriptors. Syntax rules (matching the
/// production semantics pinned in tests/):
///  - the descriptor is normalized first; characters outside [a-z0-9-/=,;] vanish,
///  - segments are ';'-separated; empty segments are ignored,
///  - each segment must be exactly key=value[,value...]; a second '=' in a
///    segment, an empty key, or no non-empty values makes the whole descriptor invalid,
///  - the empty descriptor is syntactically valid (it parses to zero grants,
///    which the evaluator reports as an empty publication).
/// </summary>
public static class DescriptorParser
{
    public static bool IsSyntaxValid(string? descriptor) => Parse(descriptor).SyntaxValid;

    public static ParsedDescriptor Parse(string? descriptor)
    {
        var normalized = Normalizer.NormalizeDescriptor(descriptor);

        var grants = new Dictionary<TagType, IReadOnlyList<string?>>();
        var unknownKeys = new List<string>();

        foreach (var rawSegment in normalized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim(','); // ",test=trailing,commas," is a valid segment for key "test"
            if (segment.Length == 0)
                continue;

            var parts = segment.Split('=');
            if (parts.Length != 2)
                return Invalid();

            var key = parts[0];
            var values = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (key.Length == 0 || values.Length == 0)
                return Invalid();

            if (!TagTypes.TryParse(key, out var tagType))
            {
                unknownKeys.Add(key);
                continue;
            }

            grants[tagType] = grants.TryGetValue(tagType, out var existing)
                ? existing.Concat(values).Distinct().ToList()
                : values.ToList<string?>();
        }

        return new ParsedDescriptor { SyntaxValid = true, Grants = grants, UnknownKeys = unknownKeys };

        ParsedDescriptor Invalid() => new()
        {
            SyntaxValid = false,
            Grants = new Dictionary<TagType, IReadOnlyList<string?>>(),
            UnknownKeys = [],
        };
    }
}
