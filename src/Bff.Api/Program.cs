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
