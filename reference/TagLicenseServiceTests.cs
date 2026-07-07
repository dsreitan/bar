using FakeItEasy;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Skolestudio.Utils;
using NUnit.Framework;
using Skolestudio.Features.FeatureFlags;
using Skolestudio.Features.User;
using Skolestudio.Integration.PNP;
using Skolestudio.Jobs;
using Skolestudio.Security.Authorization.License;
using Skolestudio.Security.Authorization.Publications;

namespace Skolestudio.Web.UnitTests.Security.Authorization;

[TestFixture]
public class TagLicenseServiceTests
{
    private readonly TagLicenseService _tagLicenseService;
    private readonly IUserService _userService;
    private readonly IPublicationService _publicationService;

    public TagLicenseServiceTests()
    {
        var telemetryConfiguration = new TelemetryConfiguration
        {
            TelemetryChannel = new Microsoft.ApplicationInsights.Channel.InMemoryChannel(),
            DisableTelemetry = true
        };
        var telemetryClient = new TelemetryClient(telemetryConfiguration);
        _publicationService = A.Fake<IPublicationService>();
        _userService = A.Fake<IUserService>();
        _tagLicenseService = new TagLicenseService(_userService, _publicationService, A.Fake<IFeatureFlagService>(), A.Fake<Logger<TagLicenseService>>(), telemetryClient);

        A.CallTo(() => _publicationService.GetSkolestudioPublications()).Returns(
            PublicationFetcherJob.GetVirtualPublications(TibetSite.Skolestudio)
                .DistinctBy(x => x.PublicationIdentity)
                .ToDictionary(x => x.PublicationIdentity));
    }

    public static IEnumerable<TestCaseData> ContentTagsInvalidFAIL => [
        new (new TagSet { [TagType.LearningMaterial] = ["foo"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new (new TagSet { [TagType.LearningMaterial] = ["salto"], [TagType.LearningComponent] = ["foo"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
    ];

    #region Liten17

    public static IEnumerable<TestCaseData> ContentTagsLiten17OK => [
        new (new TagSet { [TagType.Pricing] = ["free"] }, TagLicenseAccessResultReason.OK_ContentOpen),
        new (new TagSet { [TagType.LearningMaterial] = ["salaby-skole"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["salaby-skole"], [TagType.Subject] = ["musikk"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["salto"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["salto"], [TagType.LearningComponent] = ["fagrom"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["multi"], [TagType.LearningComponent] = ["fagrom"], [TagType.ContentType] = ["aarsplan","utskrift"]}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
    ];

    public static IEnumerable<TestCaseData> ContentTagsLiten17FAIL => [
        new (new TagSet { [TagType.LearningMaterial] = ["salaby-skole"], [TagType.Subject] = ["norsk"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new (new TagSet { [TagType.LearningMaterial] = ["vivo"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new (new TagSet { [TagType.LearningMaterial] = ["multi"], [TagType.LearningComponent] = ["fagrom"], [TagType.ContentType] = ["samhandling/bingo"]}, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
    ];

    [TestCaseSource("ContentTagsInvalidFAIL")]
    [TestCaseSource("ContentTagsLiten17OK")]
    [TestCaseSource("ContentTagsLiten17FAIL")]
    public void CheckAccess_Product_Liten17(TagSet contentTags, TagLicenseAccessResultReason reason)
    {
        var mockUser = new MockUser(new() { TemplateName = MockTemplateName.Liten, UserRole = UserRole.Teacher, FeideClass = 1 });
        A.CallTo(() => _userService.GetUser()).Returns(mockUser);

        var result = _tagLicenseService.CheckAccess(contentTags);

        result.Reason.Should().Be(reason);
    }

    #endregion

    #region Medium17

    public static IEnumerable<TestCaseData> ContentTagsMedium17OK => [
        new (new TagSet { [TagType.LearningMaterial] = ["vivo"], [TagType.LearningComponent] = ["fagrom"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["multi"], [TagType.LearningComponent] = ["fagrom"], [TagType.ContentType] = ["samhandling/bingo"]}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
    ];

    public static IEnumerable<TestCaseData> ContentTagsMedium17FAIL => [
        new (new TagSet { [TagType.LearningMaterial] = ["aftenpostenjuniorskole"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
    ];

    [TestCaseSource("ContentTagsInvalidFAIL")]
    [TestCaseSource("ContentTagsLiten17OK")]
    [TestCaseSource("ContentTagsMedium17OK")]
    [TestCaseSource("ContentTagsMedium17FAIL")]
    public void CheckAccess_Product_Medium17(TagSet contentTags, TagLicenseAccessResultReason reason)
    {
        var mockUser = new MockUser(new() { TemplateName = MockTemplateName.Medium, UserRole = UserRole.Teacher, FeideClass = 1 });
        A.CallTo(() => _userService.GetUser()).Returns(mockUser);

        var result = _tagLicenseService.CheckAccess(contentTags);

        result.Reason.Should().Be(reason);
    }

    #endregion


    #region Liten810

    public static IEnumerable<TestCaseData> ContentTagsLiten810OK => [
        new (new TagSet { [TagType.LearningMaterial] = ["kontekst"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["maximum"], [TagType.LearningComponent] = ["fagrom"], [TagType.ContentType] = ["aarsplan"]}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["enter"], [TagType.LearningComponent] = ["fagrom"], [TagType.Differentiation] = ["basis"]}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
    ];

    public static IEnumerable<TestCaseData> ContentTagsLiten810FAIL => [
        new (new TagSet { [TagType.LearningMaterial] = ["magasin"] }, TagLicenseAccessResultReason.OK_ContentOpen),
        new (new TagSet { [TagType.LearningMaterial] = ["enter"], [TagType.LearningComponent] = ["fagrom"], [TagType.Differentiation] = ["parallel"]}, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
    ];

    [TestCaseSource("ContentTagsLiten810OK")]
    [TestCaseSource("ContentTagsLiten810FAIL")]
    public void CheckAccess_Product_Liten810(TagSet contentTags, TagLicenseAccessResultReason reason)
    {
        var mockUser = new MockUser(new() { TemplateName = MockTemplateName.Liten, UserRole = UserRole.Teacher, FeideClass = 8 });
        A.CallTo(() => _userService.GetUser()).Returns(mockUser);

        var result = _tagLicenseService.CheckAccess(contentTags);

        result.Reason.Should().Be(reason);
    }

    #endregion

    #region Custom publication mix

    public static IEnumerable<TestCaseData> ContentTagsCustom => [
        new (new TagSet { [TagType.LearningMaterial] = ["vivo"], [TagType.LearningComponent] = ["fagrom"] }, new int[]{588905}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["aftenpostenjuniorskole"] }, new int[]{579842}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["aftenpostenjuniorskole"] }, new int[]{551909, 588905}, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new (new TagSet { [TagType.LearningMaterial] = ["refleks"] }, new int[]{540200}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["refleks"], [TagType.Subject] = ["samfunnsfag"] }, new int[]{540200}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["refleks"], [TagType.Subject] = ["samfunnsfag"] }, new int[]{540217}, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new (new TagSet { [TagType.LearningMaterial] = ["refleks"], [TagType.Subject] = ["naturfag"] }, new int[]{540217}, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new (new TagSet { [TagType.LearningMaterial] = ["refleks"], [TagType.Subject] = ["naturfag"] }, new int[]{540200}, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
    ];

    [TestCaseSource("ContentTagsCustom")]
    public void CheckAccess_CustomPublications(TagSet contentTags, IEnumerable<int> userPublications, TagLicenseAccessResultReason reason)
    {
        var mockUser = new MockUser(new() { TemplateName = MockTemplateName.Uten }) with
        {
            Publications = userPublications
        };

        A.CallTo(() => _userService.GetUser()).Returns(mockUser);

        var result = _tagLicenseService.CheckAccess(contentTags);

        result.Reason.Should().Be(reason);
    }

    #endregion

    #region Single publications

    public static IEnumerable<TestCaseData> TestDataSinglePublications => [
        new ("LearningMaterial=explore", new TagSet { [TagType.LearningMaterial] = ["explore"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=salto,multi", new TagSet { [TagType.LearningMaterial] = ["salto"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=relevans;Subject=naturfag", new TagSet { [TagType.LearningMaterial] = ["relevans"], [TagType.Subject] = ["naturfag"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=multi;Subject=matematikk", new TagSet { [TagType.LearningMaterial] = ["multi"], [TagType.Subject] = ["matematikk"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=refleks;LearningComponent=fagrom,bokstoette;Subject=naturfag", new TagSet { [TagType.LearningMaterial] = ["refleks"], [TagType.LearningComponent] = ["fagrom", "bokstoette"], [TagType.Subject] = ["naturfag"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=kontekst;LearningComponent=ferdigheter", new TagSet { [TagType.LearningMaterial] = ["kontekst"], [TagType.LearningComponent] = ["ferdigheter"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningComponent=inspo", new TagSet { [TagType.LearningComponent] = ["inspo"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=kontekst;Differentiation=basis", new TagSet { [TagType.LearningMaterial] = ["kontekst"], [TagType.Differentiation] = ["basis"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=kontekst;Differentiation=parallel", new TagSet { [TagType.LearningMaterial] = ["kontekst"], [TagType.Differentiation] = ["parallel"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=kontekst", new TagSet { [TagType.LearningMaterial] = ["kontekst"], [TagType.Differentiation] = ["basis"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=refleks;LearningComponent=fagrom,bokstoette;Subject=samfunnsfag", new TagSet { [TagType.LearningMaterial] = ["refleks"], [TagType.LearningComponent] = ["fagrom", "bokstoette"], [TagType.Subject] = ["samfunnsfag"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=salaby;Subject=kunst-og-haandverk", new TagSet { [TagType.LearningMaterial] = ["salaby"], [TagType.Subject] = ["kunst-og-haandverk"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("Pricing=betaling-for-bruk", new TagSet { [TagType.Pricing] = ["betaling-for-bruk"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("Learningcomponent=Salaby;Learningmaterial=salaby-skole", new TagSet { [TagType.LearningComponent] = ["Salaby"], [TagType.LearningMaterial] = ["salaby-skole"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),
        new ("LearningMaterial=salto", new TagSet { [TagType.LearningMaterial] = ["salaby", "salto"] }, TagLicenseAccessResultReason.OK_ContentTagsMatching),

        // FAIL
        new ("LearningMaterial=multi", new TagSet { [TagType.LearningMaterial] = ["explore"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new ("LearningMaterial=multi;Subject=matematikk", new TagSet { [TagType.LearningMaterial] = ["multi"], [TagType.Subject] = ["naturfag"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues),
        new ("product=pilot,skillsho", new TagSet { [TagType.LearningMaterial] = ["pilot", "skillsho"] }, TagLicenseAccessResultReason.FAIL_PublicationsEmpty), // product is not a valid tag key
        new ("LearningMaterial=salto", new TagSet { [TagType.LearningMaterial] = ["salto"], [TagType.LearningMaterial] = ["salaby"] }, TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues), // duplicate key in content tags
    ];

    [TestCaseSource("TestDataSinglePublications")]
    public void CheckAccess_Single_Publication(string? publicationDescriptor, TagSet contentTags, TagLicenseAccessResultReason reason)
    {
        TagLicenseService.CheckUserPublicationAccess(new() { Descriptor = publicationDescriptor }, contentTags).Reason.Should().Be(reason, $"{publicationDescriptor} should match {contentTags}");
    }

    [TestCase(new[] { "foo/bar" }, new[] { "foo/bar" }, ExpectedResult = true, Description = "match")]
    [TestCase(new[] { "foo/bar/baz" }, new[] { "foo/bar/baz" }, ExpectedResult = true, Description = "match")]
    [TestCase(new[] { "foo" }, new[] { "foo" }, ExpectedResult = true, Description = "match")]
    [TestCase(new[] { "foo" }, new[] { "bar" }, ExpectedResult = false, Description = "no match")]
    [TestCase(new[] { (string?)null }, new[] { "foo" }, ExpectedResult = false, Description = "invalid user publication tag")]
    [TestCase(null, new[] { "foo" }, ExpectedResult = false, Description = "invalid user publication tag")]
    [TestCase(new[] { "foo" }, new[] { (string?)null }, ExpectedResult = true, Description = "invalid content tag")]
    [TestCase(new[] { "foo" }, null, ExpectedResult = true, Description = "invalid content tag")]
    // hierarchical: a publication root grants access to descendant content
    [TestCase(new[] { "foo" }, new[] { "foo/bar" }, ExpectedResult = true, Description = "publication root matches child content")]
    [TestCase(new[] { "veien-til-toppidrett" }, new[] { "veien-til-toppidrett/oevelser" }, ExpectedResult = true, Description = "publication root matches child content")]
    [TestCase(new[] { "foo/bar" }, new[] { "foo" }, ExpectedResult = false, Description = "child publication does not grant parent content")]
    [TestCase(new[] { "foo" }, new[] { "foobar" }, ExpectedResult = false, Description = "path boundary: prefix without slash is not a match")]
    public bool IsMatchingTags_Should_Have_Expected_Result(IEnumerable<string> userPublicationTags, IEnumerable<string> contentTags)
    {
        return TagLicenseService.IsMatchingTags(userPublicationTags, contentTags);
    }

    #endregion

    #region ContentStructure and free tags

    [TestCase("LearningMaterial=veien;ContentStructure=veien-til-toppidrett", "veien-til-toppidrett/oevelser", TagLicenseAccessResultReason.OK_ContentTagsMatching)]
    [TestCase("LearningMaterial=veien;ContentStructure=veien-til-toppidrett", "annet-emne/oevelser", TagLicenseAccessResultReason.FAIL_PublicationsNoMatchingValues)]
    public void CheckUserPublicationAccess_ContentStructure_Is_Hierarchical(string publicationDescriptor, string contentStructure, TagLicenseAccessResultReason reason)
    {
        var contentTags = new TagSet
        {
            [TagType.LearningMaterial] = ["veien"],
            [TagType.ContentStructure] = [contentStructure],
        };

        TagLicenseService.CheckUserPublicationAccess(new() { Descriptor = publicationDescriptor }, contentTags)
            .Reason.Should().Be(reason);
    }

    [Test]
    public void CheckAccess_FreeTag_Frittstaaende_Is_Open_With_Detail()
    {
        var mockUser = new MockUser(new() { TemplateName = MockTemplateName.Uten });
        A.CallTo(() => _userService.GetUser()).Returns(mockUser);

        var contentTags = new TagSet { [TagType.LearningMaterial] = ["frittstaaende"] };

        var result = _tagLicenseService.CheckAccess(contentTags);

        result.Reason.Should().Be(TagLicenseAccessResultReason.OK_ContentOpen);
        result.ReasonDetail.Should().Be("LearningMaterial=frittstaaende");
    }

    #endregion
}
