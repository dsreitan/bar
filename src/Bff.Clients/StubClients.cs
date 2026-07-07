using Bff.Entitlements;

namespace Bff.Clients;

/// <summary>
/// In-memory stand-ins for the platform services, seeded with a re-enactment
/// of the classic incident: user "bob" has a correct license whose publication
/// id 551909 is unknown to the registry, and user "carol" has a publication
/// with an invalid descriptor (which fails open by design).
/// </summary>
public static class StubData
{
    public static readonly IReadOnlyDictionary<int, PublicationRecord> Publications =
        new List<PublicationRecord>
        {
            new(540200, "Refleks samfunnsfag", "LearningMaterial=refleks;LearningComponent=fagrom,bokstoette;Subject=samfunnsfag"),
            new(540217, "Refleks naturfag", "LearningMaterial=refleks;LearningComponent=fagrom,bokstoette;Subject=naturfag"),
            new(560001, "Multi", "LearningMaterial=multi"),
            new(588905, "Vivo", "LearningMaterial=vivo;LearningComponent=fagrom"),
            new(560002, "Salto (DEFECT: invalid descriptor)", "LearningMaterial=salto,Subject=matematikk"),
            new(560003, "Veien til toppidrett", "LearningMaterial=veien;ContentStructure=veien-til-toppidrett"),
        }.ToDictionary(p => p.Id);

    public static readonly IReadOnlyDictionary<string, UserLicense> Users = new Dictionary<string, UserLicense>
    {
        ["alice"] = new("alice", [560001, 540217]),
        ["bob"] = new("bob", [551909, 540200]), // 551909 is NOT in the registry — the incident
        ["carol"] = new("carol", [560002]),     // invalid descriptor — fails open to everything
        ["dave"] = new("dave", []),
    };

    public static readonly IReadOnlyDictionary<string, ContentItem> Content = new Dictionary<string, ContentItem>
    {
        ["multi-tavle"] = Item("multi-tavle", "Multi 1-7 tavle", new()
        {
            ["LearningMaterial"] = ["multi"],
            ["LearningComponent"] = ["fagrom"],
        }),
        ["refleks-naturfag-kap1"] = Item("refleks-naturfag-kap1", "Refleks naturfag kapittel 1", new()
        {
            ["LearningMaterial"] = ["refleks"],
            ["LearningComponent"] = ["fagrom"],
            ["Subject"] = ["naturfag"],
        }),
        ["refleks-samfunnsfag-kap1"] = Item("refleks-samfunnsfag-kap1", "Refleks samfunnsfag kapittel 1", new()
        {
            ["LearningMaterial"] = ["refleks"],
            ["LearningComponent"] = ["fagrom"],
            ["Subject"] = ["samfunnsfag"],
        }),
        ["toppidrett-oevelser"] = Item("toppidrett-oevelser", "Veien til toppidrett: øvelser", new()
        {
            ["LearningMaterial"] = ["veien"],
            ["ContentStructure"] = ["veien-til-toppidrett/oevelser"],
        }),
        ["gratis-artikkel"] = Item("gratis-artikkel", "Gratis artikkel", new()
        {
            ["Pricing"] = ["free"],
            ["LearningMaterial"] = ["multi"],
        }),
        ["salaby-jul"] = Item("salaby-jul", "Salaby julekalender", new()
        {
            ["LearningMaterial"] = ["salaby-skole"],
            ["SeasonalEvent"] = ["jul"], // not a license-relevant key — transport drops and reports it
        }),
    };

    private static ContentItem Item(string id, string title, Dictionary<string, IReadOnlyList<string?>> labels)
        => new(id, title, labels);
}

public sealed class StubUserServiceClient : IUserServiceClient
{
    public Task<UserLicense?> GetUserAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(StubData.Users.GetValueOrDefault(userId));
}

public sealed class StubPublicationRegistryClient : IPublicationRegistryClient
{
    public Task<IReadOnlyDictionary<int, PublicationRecord>> GetPublicationsAsync(CancellationToken ct = default)
        => Task.FromResult(StubData.Publications);
}

public sealed class StubContentMetadataClient : IContentMetadataClient
{
    public Task<ContentItem?> GetContentAsync(string contentId, CancellationToken ct = default)
        => Task.FromResult(StubData.Content.GetValueOrDefault(contentId));
}
