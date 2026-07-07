namespace Bff.Entitlements;

public enum AccessVerdict
{
    Allow,
    Deny,
}

/// <summary>Mirrors the production TagLicenseAccessResultReason values.</summary>
public enum DecisionReason
{
    OK_FeatureFlag,
    OK_ContentOpen,
    OK_ContentTagsMissingRequired_LM_LC_ISBN,
    OK_PublicationsInvalidSyntax,
    OK_ContentTagsMatching,
    FAIL_PublicationsEmpty,
    FAIL_PublicationsNoMatchingKeys,
    FAIL_PublicationsNoMatchingValues,
}

public enum PublicationOutcome
{
    /// <summary>Invalid descriptor syntax grants access — intended policy so a
    /// misconfigured publication never locks paying users out. The linter is the
    /// compensating control that keeps this state short-lived.</summary>
    SyntaxInvalidFailOpen,
    Empty,
    NoKeyOverlap,
    ValueMismatch,
    Matched,
}

/// <summary>One compared key from the publication/content key intersection.</summary>
public sealed record KeyComparison
{
    public required TagType Key { get; init; }
    public required IReadOnlyList<string?> PublicationValues { get; init; }
    public required IReadOnlyList<string?> ContentValues { get; init; }
    public required bool Matched { get; init; }

    /// <summary>The (publication value, content value) pair that matched, if any.
    /// "(fail-open)" marks a match caused by invalid (null) content values.</summary>
    public string? MatchedOn { get; init; }
}

public sealed record PublicationTrace
{
    public required int PublicationId { get; init; }
    public required string Name { get; init; }
    public required string? Descriptor { get; init; }
    public required PublicationOutcome Outcome { get; init; }
    public required bool HasAccess { get; init; }
    public required IReadOnlyList<KeyComparison> KeyComparisons { get; init; }
    public required IReadOnlyList<string> UnknownDescriptorKeys { get; init; }

    public string Explain() => Outcome switch
    {
        PublicationOutcome.SyntaxInvalidFailOpen =>
            $"pub {PublicationId} '{Name}': descriptor has invalid syntax — access granted fail-open (data defect, see linter)",
        PublicationOutcome.Empty =>
            $"pub {PublicationId} '{Name}': descriptor parses to zero usable constraints"
            + (UnknownDescriptorKeys.Count > 0 ? $" (unknown keys: {string.Join(", ", UnknownDescriptorKeys)})" : ""),
        PublicationOutcome.NoKeyOverlap =>
            $"pub {PublicationId} '{Name}': no tag key in common with the content",
        PublicationOutcome.ValueMismatch =>
            $"pub {PublicationId} '{Name}': " + string.Join("; ", KeyComparisons.Select(ExplainComparison)),
        PublicationOutcome.Matched =>
            $"pub {PublicationId} '{Name}': matched — " + string.Join("; ", KeyComparisons.Select(ExplainComparison)),
        _ => Outcome.ToString(),
    };

    private static string ExplainComparison(KeyComparison c) => c.Matched
        ? $"{c.Key} matched ({c.MatchedOn})"
        : $"{c.Key} FAILED (pub: {Join(c.PublicationValues)}; content: {Join(c.ContentValues)})";

    private static string Join(IReadOnlyList<string?> values)
        => string.Join(",", values.Select(v => v ?? "<null>"));
}

/// <summary>
/// What was lost while assembling the decision inputs. Every item here is a
/// silent failure in the production service and a "correct license, no access"
/// suspect — which is why it is part of the decision output, not a log line.
/// </summary>
public sealed record AssemblyReport
{
    /// <summary>License publication ids not present in the publication registry.
    /// These behave exactly like "no license" — the top incident suspect.</summary>
    public required IReadOnlyList<int> DroppedPublicationIds { get; init; }

    /// <summary>Content label keys that are not license-relevant tag types.</summary>
    public required IReadOnlyList<string> DroppedContentLabelKeys { get; init; }

    public bool HasLosses => DroppedPublicationIds.Count > 0;
}

public sealed record DecisionTrace
{
    public const string CurrentRuleVersion = "poc-1";

    public required AccessVerdict Verdict { get; init; }
    public required DecisionReason Reason { get; init; }
    public string? ReasonDetail { get; init; }
    public required AssemblyReport Assembly { get; init; }
    public required TagSet ContentTags { get; init; }
    public required IReadOnlyList<PublicationTrace> Publications { get; init; }
    public string RuleVersion { get; init; } = CurrentRuleVersion;

    public bool HasAccess => Verdict == AccessVerdict.Allow;

    /// <summary>Human-readable account of the decision, for the why endpoint and support.</summary>
    public IReadOnlyList<string> Explain()
    {
        var lines = new List<string>();

        foreach (var id in Assembly.DroppedPublicationIds)
            lines.Add($"publication {id} on the user's license is NOT in the publication registry — it grants nothing (license/provisioning defect)");
        foreach (var key in Assembly.DroppedContentLabelKeys)
            lines.Add($"content label '{key}' is not a license-relevant tag type and was ignored");

        lines.Add($"verdict: {Verdict} ({Reason}{(ReasonDetail is null ? "" : $": {ReasonDetail}")})");
        lines.AddRange(Publications.Select(p => p.Explain()));
        return lines;
    }
}
