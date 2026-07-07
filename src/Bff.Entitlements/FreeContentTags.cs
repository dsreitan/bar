namespace Bff.Entitlements;

/// <summary>
/// Tags that mark content as open for everyone regardless of license.
/// </summary>
public static class FreeContentTags
{
    private static readonly IReadOnlyList<(TagType Type, string Value)> OpenTags =
    [
        (TagType.Pricing, "free"),
        (TagType.LearningMaterial, "frittstaaende"),
    ];

    public static bool TryGetOpenTag(TagSet contentTags, out (TagType Type, string Value) openTag)
    {
        foreach (var candidate in OpenTags)
        {
            if (contentTags.TryGetValue(candidate.Type, out var values)
                && values.Any(v => v is not null && Normalizer.NormalizeValue(v) == candidate.Value))
            {
                openTag = candidate;
                return true;
            }
        }

        openTag = default;
        return false;
    }
}
