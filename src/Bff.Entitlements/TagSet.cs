namespace Bff.Entitlements;

/// <summary>
/// Tag values keyed by tag type. Values are nullable on purpose: a null value
/// models invalid content tagging, which the matcher treats fail-open
/// (see EntitlementEvaluator.ValuesMatch).
/// </summary>
public sealed class TagSet : Dictionary<TagType, IReadOnlyList<string?>>
{
    public TagSet() { }

    public TagSet(IEnumerable<KeyValuePair<TagType, IReadOnlyList<string?>>> pairs)
    {
        foreach (var (key, values) in pairs)
            this[key] = values;
    }

    public bool HasValues(TagType tagType)
        => TryGetValue(tagType, out var values) && values is { Count: > 0 };

    public bool IsEmpty => Values.All(v => v is not { Count: > 0 });

    public override string ToString()
        => string.Join(";", this.Select(kv => $"{kv.Key}={string.Join(",", kv.Value.Select(v => v ?? "<null>"))}"));
}
