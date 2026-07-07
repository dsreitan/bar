using System.Text.Json.Serialization;
using Bff.Clients;
using Bff.Entitlements;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Adapters only — every entitlement decision goes through the pure core.
builder.Services.AddSingleton<IUserServiceClient, StubUserServiceClient>();
builder.Services.AddSingleton<IPublicationRegistryClient, StubPublicationRegistryClient>();
builder.Services.AddSingleton<IContentMetadataClient, StubContentMetadataClient>();

var app = builder.Build();

// The frontend-facing endpoint. userId comes as a query parameter because
// authn is out of scope for this POC; in the real BFF it comes from the session.
app.MapGet("/content/{contentId}", async (
    string contentId,
    string userId,
    IUserServiceClient users,
    IPublicationRegistryClient registry,
    IContentMetadataClient contentService,
    CancellationToken ct) =>
{
    var content = await contentService.GetContentAsync(contentId, ct);
    if (content is null)
        return Results.NotFound();

    var trace = await EvaluateAsync(userId, content, users, registry, ct);

    return trace.HasAccess
        ? Results.Ok(new { content.ContentId, content.Title, grantedBecause = trace.Reason.ToString() })
        : Results.Json(new { error = "no_license", reason = trace.Reason.ToString() }, statusCode: StatusCodes.Status403Forbidden);
});

// The support tool: the full decision trace for a user/content pair.
// Internal only — it reveals license structure; in production this sits behind
// internal auth and is audit-logged.
app.MapGet("/internal/entitlements/why", async (
    string userId,
    string contentId,
    IUserServiceClient users,
    IPublicationRegistryClient registry,
    IContentMetadataClient contentService,
    CancellationToken ct) =>
{
    var content = await contentService.GetContentAsync(contentId, ct);
    if (content is null)
        return Results.NotFound(new { error = $"unknown content '{contentId}'" });

    var trace = await EvaluateAsync(userId, content, users, registry, ct);

    return Results.Ok(new
    {
        userId,
        contentId,
        verdict = trace.Verdict.ToString(),
        reason = trace.Reason.ToString(),
        reasonDetail = trace.ReasonDetail,
        ruleVersion = trace.RuleVersion,
        explanation = trace.Explain(),
        assembly = trace.Assembly,
        contentTags = trace.ContentTags.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        publications = trace.Publications,
    });
});

// License dry run: what would this descriptor unlock? Test a new license
// BEFORE provisioning it, or audit an existing publication by id. Also returns
// the descriptor translated to a search filter, for pushing entitlements into
// a search index. Internal tooling, like the why endpoint.
app.MapGet("/internal/entitlements/preview", async (
    string? descriptor,
    int? publicationId,
    IPublicationRegistryClient registry,
    IContentMetadataClient contentService,
    CancellationToken ct) =>
{
    if (descriptor is null && publicationId is null)
        return Results.BadRequest(new { error = "pass ?descriptor=... (a new license to test) or ?publicationId=... (an existing one)" });

    if (publicationId is not null)
    {
        var publications = await registry.GetPublicationsAsync(ct);
        if (!publications.TryGetValue(publicationId.Value, out var publication))
            return Results.NotFound(new { error = $"publication {publicationId} is not in the registry" });
        descriptor = publication.Descriptor;
    }

    var catalog = await contentService.GetCatalogAsync(ct);
    var preview = LicensePreviewer.Preview(descriptor, catalog.Select(c => (c.ContentId, c.Title, c.Labels)));

    return Results.Ok(new
    {
        descriptor,
        preview.SyntaxValid,
        grants = preview.Grants.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        preview.Warnings,
        searchFilter = preview.Filter is null ? null : new
        {
            clauses = preview.Filter.Clauses,
            query = preview.Filter.ToQueryString(),
        },
        matchCount = preview.Matches.Count,
        matches = preview.Matches,
    });
});

app.Run();

static async Task<DecisionTrace> EvaluateAsync(
    string userId,
    ContentItem content,
    IUserServiceClient users,
    IPublicationRegistryClient registry,
    CancellationToken ct)
{
    var user = await users.GetUserAsync(userId, ct);
    var publications = await registry.GetPublicationsAsync(ct);

    var input = new EvaluationInput
    {
        UserPublicationIds = user?.PublicationIds ?? [],
        PublicationRegistry = publications,
        RawContentLabels = content.Labels,
    };

    return EntitlementEvaluator.Evaluate(input);
}

public partial class Program;
