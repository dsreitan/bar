namespace Bff.Entitlements;

/// <summary>
/// A publication descriptor translated into search-filter form, for pushing
/// entitlements into a search index (CMS search, Elasticsearch facets, the
/// traffic-accumulated tag index) or filtering any content list.
///
/// The translation must preserve a non-obvious rule of the matcher: a clause
/// only binds when the content is tagged with that clause's key. A naive
/// filter like "Subject IN (naturfag)" would silently exclude content with no
/// Subject tag at all, which the real matcher lets through. The property suite
/// proves Matches() is equivalent to EntitlementEvaluator.CheckPublication.
/// </summary>
public sealed record LicenseSearchFilter
{
    public required IReadOnlyList<SearchFilterClause> Clauses { get; init; }

    public static LicenseSearchFilter From(ParsedDescriptor descriptor) => new()
    {
        Clauses = descriptor.Grants
            .Select(grant => new SearchFilterClause
            {
                Tag = grant.Key,
                AnyOfRoots = grant.Value.OfType<string>().ToList(),
            })
            .ToList(),
    };

    /// <summary>
    /// Same semantics as the matcher: every clause whose tag the content
    /// carries must match (hierarchically), and at least one clause must bind.
    /// </summary>
    public bool Matches(TagSet contentTags)
    {
        var bound = Clauses.Where(clause => contentTags.ContainsKey(clause.Tag)).ToList();

        return bound.Count > 0
               && bound.All(clause => EntitlementEvaluator.ValuesMatch(clause.AnyOfRoots, contentTags[clause.Tag]).Matched);
    }

    /// <summary>
    /// Human-readable, Elasticsearch-flavored rendering of the filter —
    /// documentation of what a real search integration would send.
    /// </summary>
    public string ToQueryString()
    {
        if (Clauses.Count == 0)
            return "(matches nothing)";

        var atLeastOneBinds = "(" + string.Join(" OR ", Clauses.Select(c => $"_exists_:{c.Tag}")) + ")";
        var boundClausesMatch = Clauses.Select(c =>
            $"(NOT _exists_:{c.Tag} OR {string.Join(" OR ", c.AnyOfRoots.Select(v => $"{c.Tag}:{v} OR {c.Tag}:{v}/*"))})");

        return string.Join(" AND ", new[] { atLeastOneBinds }.Concat(boundClausesMatch));
    }
}

public sealed record SearchFilterClause
{
    public required TagType Tag { get; init; }

    /// <summary>Hierarchical roots: a root value also covers every value under "root/".</summary>
    public required IReadOnlyList<string> AnyOfRoots { get; init; }
}

/// <summary>One catalog item matched by a previewed descriptor.</summary>
public sealed record PreviewMatch(string ContentId, string Title, IReadOnlyList<string> MatchedOn);

/// <summary>
/// Dry run of a (new or existing) publication descriptor against a content
/// catalog: what would this license unlock? This is the materializer from
/// docs/ALTERNATIVE-LICENSE-MODELS.md scoped to a single publication, run on
/// demand — use it to test a license BEFORE provisioning it to customers.
/// </summary>
public sealed record PreviewResult
{
    public required bool SyntaxValid { get; init; }
    public required IReadOnlyDictionary<TagType, IReadOnlyList<string?>> Grants { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required LicenseSearchFilter? Filter { get; init; }
    public required IReadOnlyList<PreviewMatch> Matches { get; init; }
}

public static class LicensePreviewer
{
    public static PreviewResult Preview(
        string? descriptor,
        IEnumerable<(string ContentId, string Title, IReadOnlyDictionary<string, IReadOnlyList<string?>> Labels)> catalog)
    {
        var parsed = DescriptorParser.Parse(descriptor);
        var warnings = new List<string>();

        foreach (var key in parsed.UnknownKeys)
            warnings.Add($"descriptor key '{key}' is not a known tag type — the constraint would be silently dropped");

        if (!parsed.SyntaxValid)
        {
            warnings.Add("INVALID descriptor syntax — provisioned as-is, this publication would grant the ENTIRE catalog (fail-open policy). Fix before provisioning.");
            return Empty(warnings);
        }

        if (parsed.Grants.Count == 0)
        {
            warnings.Add("descriptor parses to zero usable constraints — this publication would grant nothing");
            return Empty(warnings);
        }

        var probe = new PublicationRecord(0, "preview", descriptor);
        var matches = new List<PreviewMatch>();

        foreach (var (contentId, title, labels) in catalog)
        {
            var (contentTags, _) = ContentTagTransport.ToLicenseTagSet(labels);
            var check = EntitlementEvaluator.CheckPublication(probe, contentTags);
            if (!check.HasAccess)
                continue;

            matches.Add(new PreviewMatch(contentId, title, check.KeyComparisons
                .Where(c => c.Matched)
                .Select(c => $"{c.Key}: {c.MatchedOn}")
                .ToList()));
        }

        if (matches.Count == 0)
            warnings.Add("no content in the catalog matches this descriptor — dead grant or typo?");

        return new PreviewResult
        {
            SyntaxValid = true,
            Grants = parsed.Grants,
            Warnings = warnings,
            Filter = LicenseSearchFilter.From(parsed),
            Matches = matches,
        };

        PreviewResult Empty(List<string> w) => new()
        {
            SyntaxValid = parsed.SyntaxValid,
            Grants = parsed.Grants,
            Warnings = w,
            Filter = null,
            Matches = [],
        };
    }
}
