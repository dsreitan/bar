namespace Bff.Entitlements;

/// <summary>
/// Maps raw content labels (string keys, as tagged upstream) into the license
/// tag vocabulary. Keys that don't map to a license-relevant TagType are
/// dropped — and recorded, because a silently dropped key is an access-bug
/// suspect (mirrors the production ToLicenseTagSet).
/// </summary>
public static class ContentTagTransport
{
    public static (TagSet Tags, IReadOnlyList<string> DroppedKeys) ToLicenseTagSet(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> rawLabels)
    {
        var tags = new TagSet();
        var dropped = new List<string>();

        foreach (var (key, values) in rawLabels)
        {
            if (TagTypes.TryParse(key, out var tagType) && TagTypes.LicenseRelevant.Contains(tagType))
                tags[tagType] = values.Select(Normalizer.NormalizeValue).ToList();
            else
                dropped.Add(key);
        }

        return (tags, dropped);
    }
}
