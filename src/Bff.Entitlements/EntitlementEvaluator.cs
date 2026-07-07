namespace Bff.Entitlements;

/// <summary>
/// The pure decision core. Deterministic: no I/O, no clock, no DI — the whole
/// decision, input assembly included, from an EvaluationInput value.
///
/// The matching rule (rules 7–8 of the spec table in ARCHITECTURE.md):
/// let K = keys(publication) ∩ keys(content); the publication grants access
/// iff K is non-empty and every key in K has some content value hierarchically
/// covered by some publication value.
/// </summary>
public static class EntitlementEvaluator
{
    public static DecisionTrace Evaluate(EvaluationInput input)
    {
        // Stage 1+2: transport content labels, resolve publication ids — losses are recorded, not swallowed.
        var (contentTags, droppedLabelKeys) = ContentTagTransport.ToLicenseTagSet(input.RawContentLabels);
        var (publications, droppedPublicationIds) = ResolvePublications(input);

        var assembly = new AssemblyReport
        {
            DroppedPublicationIds = droppedPublicationIds,
            DroppedContentLabelKeys = droppedLabelKeys,
        };

        DecisionTrace Result(AccessVerdict verdict, DecisionReason reason, string? detail = null,
            IReadOnlyList<PublicationTrace>? checks = null) => new()
        {
            Verdict = verdict,
            Reason = reason,
            ReasonDetail = detail,
            Assembly = assembly,
            ContentTags = contentTags,
            Publications = checks ?? [],
        };

        // Open-content gates (spec rules 1–4).
        if (input.LicenseCheckDisabled)
            return Result(AccessVerdict.Allow, DecisionReason.OK_FeatureFlag);

        if (contentTags.IsEmpty)
            return Result(AccessVerdict.Allow, DecisionReason.OK_ContentOpen);

        if (FreeContentTags.TryGetOpenTag(contentTags, out var freeTag))
            return Result(AccessVerdict.Allow, DecisionReason.OK_ContentOpen, $"{freeTag.Type}={freeTag.Value}");

        if (!contentTags.HasValues(TagType.LearningMaterial)
            && !contentTags.HasValues(TagType.LearningComponent)
            && !contentTags.HasValues(TagType.Isbn))
            return Result(AccessVerdict.Allow, DecisionReason.OK_ContentTagsMissingRequired_LM_LC_ISBN);

        // Per-publication match, first success wins (spec rules 5–8). All
        // publications are traced even after a match so the why endpoint shows
        // the full picture.
        var checks = publications.Select(p => CheckPublication(p, contentTags)).ToList();
        var winner = checks.FirstOrDefault(c => c.HasAccess);

        if (winner is not null)
        {
            var reason = winner.Outcome == PublicationOutcome.SyntaxInvalidFailOpen
                ? DecisionReason.OK_PublicationsInvalidSyntax
                : DecisionReason.OK_ContentTagsMatching;
            return Result(AccessVerdict.Allow, reason, checks: checks);
        }

        return Result(AccessVerdict.Deny, DecisionReason.FAIL_PublicationsNoMatchingValues, checks: checks);
    }

    private static (IReadOnlyList<PublicationRecord>, IReadOnlyList<int>) ResolvePublications(EvaluationInput input)
    {
        var resolved = new List<PublicationRecord>();
        var dropped = new List<int>();

        foreach (var id in input.UserPublicationIds)
        {
            if (input.PublicationRegistry.TryGetValue(id, out var publication))
                resolved.Add(publication);
            else
                dropped.Add(id);
        }

        return (resolved, dropped);
    }

    public static PublicationTrace CheckPublication(PublicationRecord publication, TagSet contentTags)
    {
        var parsed = DescriptorParser.Parse(publication.Descriptor);

        PublicationTrace Trace(PublicationOutcome outcome, bool hasAccess, IReadOnlyList<KeyComparison>? comparisons = null) => new()
        {
            PublicationId = publication.Id,
            Name = publication.Name,
            Descriptor = publication.Descriptor,
            Outcome = outcome,
            HasAccess = hasAccess,
            KeyComparisons = comparisons ?? [],
            UnknownDescriptorKeys = parsed.UnknownKeys,
        };

        if (!parsed.SyntaxValid)
            return Trace(PublicationOutcome.SyntaxInvalidFailOpen, hasAccess: true);

        if (parsed.Grants.Count == 0)
            return Trace(PublicationOutcome.Empty, hasAccess: false);

        var comparisons = parsed.Grants
            .Where(grant => contentTags.ContainsKey(grant.Key))
            .Select(grant => Compare(grant.Key, grant.Value, contentTags[grant.Key]))
            .ToList();

        if (comparisons.Count == 0)
            return Trace(PublicationOutcome.NoKeyOverlap, hasAccess: false);

        return comparisons.All(c => c.Matched)
            ? Trace(PublicationOutcome.Matched, hasAccess: true, comparisons)
            : Trace(PublicationOutcome.ValueMismatch, hasAccess: false, comparisons);
    }

    private static KeyComparison Compare(TagType key, IReadOnlyList<string?> publicationValues, IReadOnlyList<string?> contentValues)
    {
        var (matched, matchedOn) = ValuesMatch(publicationValues, contentValues);
        return new KeyComparison
        {
            Key = key,
            PublicationValues = publicationValues,
            ContentValues = contentValues,
            Matched = matched,
            MatchedOn = matchedOn,
        };
    }

    /// <summary>
    /// Value matching for one key. Invalid (null) content values match
    /// fail-open; invalid publication values never match. Hierarchical with
    /// '/' as boundary: publication 'veien-til-toppidrett' covers content
    /// 'veien-til-toppidrett/oevelser'; 'salaby' does not cover 'salaby-skole'.
    /// </summary>
    public static (bool Matched, string? MatchedOn) ValuesMatch(
        IReadOnlyList<string?>? publicationValues, IReadOnlyList<string?>? contentValues)
    {
        if (contentValues is null || contentValues.Any(v => v is null))
            return (true, "(fail-open: invalid content values)");

        if (publicationValues is null || publicationValues.Any(v => v is null))
            return (false, null);

        foreach (var content in contentValues)
        foreach (var publication in publicationValues)
        {
            if (content == publication || content!.StartsWith(publication + "/", StringComparison.Ordinal))
                return (true, $"'{publication}' covers '{content}'");
        }

        return (false, null);
    }
}
