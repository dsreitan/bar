using Bff.Entitlements;

namespace Bff.Entitlements.Oracle;

/// <summary>
/// The reference implementation: a literal, unoptimized transcription of the
/// spec table in ARCHITECTURE.md, sharing only the input/verdict types with
/// the real evaluator. It has its own normalizer, its own parser and its own
/// matcher on purpose — property tests assert the real evaluator always agrees
/// with this one, so a bug must be made in both places to go unnoticed.
/// Keep this small enough to verify by eye; never optimize it.
/// </summary>
public static class OracleEvaluator
{
    public static (AccessVerdict Verdict, DecisionReason Reason) Evaluate(EvaluationInput input)
    {
        // Transport: keep only license-relevant label keys, normalize values.
        var content = new Dictionary<TagType, List<string?>>();
        foreach (var pair in input.RawContentLabels)
        {
            if (!Enum.TryParse<TagType>(pair.Key, ignoreCase: true, out var tagType)) continue;
            if (!Enum.IsDefined(tagType)) continue;
            if (tagType == TagType.EducationalRole) continue;

            var values = new List<string?>();
            foreach (var value in pair.Value)
                values.Add(value is null ? null : Clean(value, structural: false));
            content[tagType] = values;
        }

        // Rule 1: feature flag bypass.
        if (input.LicenseCheckDisabled)
            return (AccessVerdict.Allow, DecisionReason.OK_FeatureFlag);

        // Rule 2: content without any tag values is open.
        var hasAnyValue = false;
        foreach (var values in content.Values)
            if (values.Count > 0) hasAnyValue = true;
        if (!hasAnyValue)
            return (AccessVerdict.Allow, DecisionReason.OK_ContentOpen);

        // Rule 3: free tags are open.
        if (HasValue(content, TagType.Pricing, "free") || HasValue(content, TagType.LearningMaterial, "frittstaaende"))
            return (AccessVerdict.Allow, DecisionReason.OK_ContentOpen);

        // Rule 4: content with none of LM, LC, Isbn is open.
        if (!HasAny(content, TagType.LearningMaterial) && !HasAny(content, TagType.LearningComponent) && !HasAny(content, TagType.Isbn))
            return (AccessVerdict.Allow, DecisionReason.OK_ContentTagsMissingRequired_LM_LC_ISBN);

        // Rules 5–8, first success wins.
        foreach (var id in input.UserPublicationIds)
        {
            if (!input.PublicationRegistry.TryGetValue(id, out var publication))
                continue; // unknown ids grant nothing

            // Rule 5: invalid descriptor syntax fails open.
            if (!TryParse(publication.Descriptor, out var grants))
                return (AccessVerdict.Allow, DecisionReason.OK_PublicationsInvalidSyntax);

            // Rule 6: zero usable constraints — no match.
            if (grants.Count == 0)
                continue;

            // Rule 7: K = keys(publication) ∩ keys(content) must be non-empty.
            var intersection = new List<TagType>();
            foreach (var key in grants.Keys)
                if (content.ContainsKey(key)) intersection.Add(key);
            if (intersection.Count == 0)
                continue;

            // Rule 8: every key in K must value-match.
            var allMatch = true;
            foreach (var key in intersection)
                if (!ValuesMatch(grants[key], content[key])) allMatch = false;
            if (allMatch)
                return (AccessVerdict.Allow, DecisionReason.OK_ContentTagsMatching);
        }

        return (AccessVerdict.Deny, DecisionReason.FAIL_PublicationsNoMatchingValues);
    }

    private static bool ValuesMatch(List<string> publicationValues, List<string?> contentValues)
    {
        foreach (var value in contentValues)
            if (value is null) return true; // invalid content values match fail-open

        foreach (var contentValue in contentValues)
        foreach (var publicationValue in publicationValues)
        {
            if (contentValue == publicationValue) return true;
            if (contentValue!.StartsWith(publicationValue + "/", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool TryParse(string? descriptor, out Dictionary<TagType, List<string>> grants)
    {
        grants = new Dictionary<TagType, List<string>>();
        var normalized = Clean(descriptor ?? "", structural: true);

        foreach (var rawSegment in normalized.Split(';'))
        {
            var segment = rawSegment.Trim(','); // leading/trailing commas in a segment are ignored
            if (segment.Length == 0) continue;

            var equalsCount = 0;
            foreach (var c in segment)
                if (c == '=') equalsCount++;
            if (equalsCount != 1) return false;

            var key = segment[..segment.IndexOf('=')];
            if (key.Length == 0) return false;

            var values = new List<string>();
            foreach (var value in segment[(segment.IndexOf('=') + 1)..].Split(','))
                if (value.Length > 0) values.Add(value);
            if (values.Count == 0) return false;

            if (!Enum.TryParse<TagType>(key, ignoreCase: true, out var tagType) || !Enum.IsDefined(tagType))
                continue; // unknown keys are dropped, descriptor stays valid

            if (!grants.ContainsKey(tagType)) grants[tagType] = new List<string>();
            foreach (var value in values)
                if (!grants[tagType].Contains(value)) grants[tagType].Add(value);
        }

        return true;
    }

    private static string Clean(string raw, bool structural)
    {
        var result = "";
        foreach (var c in raw.ToLowerInvariant())
        {
            var keep = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '/'
                       || (structural && c is '=' or ',' or ';');
            if (keep) result += c;
        }
        return result;
    }

    private static bool HasAny(Dictionary<TagType, List<string?>> content, TagType tagType)
        => content.TryGetValue(tagType, out var values) && values.Count > 0;

    private static bool HasValue(Dictionary<TagType, List<string?>> content, TagType tagType, string value)
        => content.TryGetValue(tagType, out var values) && values.Contains(value);
}
