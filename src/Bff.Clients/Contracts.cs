using Bff.Entitlements;

namespace Bff.Clients;

/// <summary>A user's license as returned by the user/license service.</summary>
public sealed record UserLicense(string UserId, IReadOnlyList<int> PublicationIds);

/// <summary>A piece of content as returned by the content metadata service.</summary>
public sealed record ContentItem(string ContentId, string Title, IReadOnlyDictionary<string, IReadOnlyList<string?>> Labels);

/// <summary>
/// Typed clients for the platform microservices. The BFF only ever *fetches
/// inputs* through these — no entitlement logic lives behind them. In this POC
/// they are in-memory stubs; the real implementations are HTTP clients whose
/// DTO→domain mapping is pinned by contract tests.
/// </summary>
public interface IUserServiceClient
{
    Task<UserLicense?> GetUserAsync(string userId, CancellationToken ct = default);
}

public interface IPublicationRegistryClient
{
    Task<IReadOnlyDictionary<int, PublicationRecord>> GetPublicationsAsync(CancellationToken ct = default);
}

public interface IContentMetadataClient
{
    Task<ContentItem?> GetContentAsync(string contentId, CancellationToken ct = default);
}
