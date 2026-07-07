using Microsoft.ApplicationInsights;
using Skolestudio.Features.FeatureFlags;
using Skolestudio.Integration.Unleash.FeatureFlags;
using Skolestudio.Security.Authorization.Publications;

namespace Skolestudio.Security.Authorization.License;

/// <summary>
/// "Lisensmottor'n"
/// </summary>
public class TagLicenseService(IUserService userService, IPublicationService publicationService, IFeatureFlagService featureFlagService, ILogger<TagLicenseService> logger, TelemetryClient telemetryClient)
{
    /// <summary>
    /// True if any publication matches the given content tags
    /// </summary>
    public TagLicenseAccessResult CheckAccess(TagSet contentTags)
    {
        var result = CheckAccessForContent(contentTags);
        //Todo: Look at method for getting contentTags in telemetry client
        telemetryClient.GetMetric("Access check", "reason").TrackValue(1, result.Reason.ToString());
        return result;
    }

    private TagLicenseAccessResult CheckAccessForContent(TagSet contentTags)
    {
        // Feature flag to bypass license checks
        if (featureFlagService.IsOn(PermanentFlag.LicenseCheckNone))
            return TagLicenseAccessResult.Success(TagLicenseAccessResultReason.OK_FeatureFlag, contentTags);

        // Content without tags are considered open for all
        if (contentTags.Values.Any(x => x.Any()) == false)
            return TagLicenseAccessResult.Success(TagLicenseAccessResultReason.OK_ContentOpen, contentTags);

        // Content with special free tags should be considered open for all
        if (FreeContentTags.TryGetOpenTag(contentTags, out var freeTag))
            return TagLicenseAccessResult.Success(TagLicenseAccessResultReason.OK_ContentOpen, contentTags, reasonDetail: $"{freeTag!.Type}={freeTag.Value}");

        // OK if content is missing LM, LC and Isbn
        if (!HasContentTags(contentTags, TagType.LearningMaterial) && !HasContentTags(contentTags, TagType.LearningComponent) && !HasContentTags(contentTags, TagType.Isbn))
            return TagLicenseAccessResult.Success(TagLicenseAccessResultReason.OK_ContentTagsMissingRequired_LM_LC_ISBN, contentTags);

        List<TagLicensePublicationCheck> publicationChecks = [];

        foreach (var userPublication in GetUserPublications())
        {
            var publicationCheck = CheckUserPublicationAccess(userPublication, contentTags);

            publicationChecks.Add(publicationCheck);

            if (publicationCheck.HasAccess)
            {
                if (publicationCheck.Reason == TagLicenseAccessResultReason.OK_PublicationsInvalidSyntax)
                {
                    logger.LogError("User publication has invalid syntax. Name=[{Name}] Descriptor=[{Descriptor}]", userPublication?.Name, userPublication?.Descriptor);
                }

                return TagLicenseAccessResult.Success(publicationCheck.Reason, contentTags, publicationChecks);
            }
        }

        return TagLicenseAccessResult.Fail(TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues, contentTags, publicationChecks);
    }

    /// <summary>
    /// Use carefully - this doesn't take into account multiple tags on a publication which could invalidate access validity
    /// ie. LearningMaterial=relevans;Subject=naturfag
    /// </summary>
    public IEnumerable<string> GetUserPublicationTagsByType(TagType tagType)
    {
        return GetUserPublications().GetTagsByKey(tagType.ToString());
    }

    /// <summary>
    /// Every tag key in a publication must have a matching content tag of the same key
    /// </summary>
    public static TagLicensePublicationCheck CheckUserPublicationAccess(Publication userPublication, TagSet contentTags)
    {
        if (PublicationExtensions.IsDescriptorSyntaxValid(userPublication.Descriptor) == false)
            return TagLicensePublicationCheck.Success(TagLicenseAccessResultReason.OK_PublicationsInvalidSyntax, userPublication);

        var userPublicationTagsByKey = userPublication.GetDescriptorTags().ToTagSet();
        if (userPublicationTagsByKey.Count == 0)
            return TagLicensePublicationCheck.Fail(TagLicenseAccessResultReason.FAIL_PublicationsEmpty, userPublication);

        // If no keys match, return false
        if (contentTags.Keys.Any(userPublicationTagsByKey.ContainsKey) == false)
            return TagLicensePublicationCheck.Fail(TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingKeys, userPublication);


        // All tags within a publication must match
        foreach (var userPublicationTag in userPublicationTagsByKey)
        {
            // If content isn't tagged with the same tag as publication, try next publication
            if (contentTags.ContainsKey(userPublicationTag.Key) == false)
                continue;

            if (IsMatchingTags(userPublicationTag.Value, contentTags[userPublicationTag.Key]) == false)
                return TagLicensePublicationCheck.Fail(TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues, userPublication);
        }

        return TagLicensePublicationCheck.Success(TagLicenseAccessResultReason.OK_ContentTagsMatching, userPublication);
    }

    public static bool IsMatchingTags(IEnumerable<string> publicationTagValues, IEnumerable<string> contentTagValues)
    {
        // invalid content tags should be considered a match
        if (contentTagValues == null || contentTagValues.Any(x => x == null))
            return true;

        // invalid publication tags should not be a match
        if (publicationTagValues == null || publicationTagValues.Any(x => x == null))
            return false;

        return contentTagValues.Any(content =>
            publicationTagValues.Any(publication => IsHierarchicalMatch(publication, content)));
    }

    /// <summary>
    /// A publication tag root grants access to all descendant content tags.
    /// The "/" boundary means "salaby" does not match "salaby-skole", but
    /// "veien-til-toppidrett" matches "veien-til-toppidrett/oevelser".
    /// </summary>
    private static bool IsHierarchicalMatch(string publicationValue, string contentValue) =>
        contentValue == publicationValue ||
        contentValue.StartsWith(publicationValue + "/", StringComparison.Ordinal);

    public IEnumerable<Publication> GetUserPublications()
    {
        var user = userService.GetUser();

        var skolestudioPublications = publicationService.GetSkolestudioPublications();

        if (user?.Publications == null || user.Publications.Any() == false)
            return [];

        return user.Publications.Where(skolestudioPublications.ContainsKey).Select(p => skolestudioPublications[p]);
    }

    private static bool HasContentTags(TagSet contentTags, TagType tagType)
        => contentTags.TryGetValue(tagType, out var tags) && tags.Any();
}
