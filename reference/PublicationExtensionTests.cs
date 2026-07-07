using FluentAssertions;
using NUnit.Framework;
using Skolestudio.Common;
using Skolestudio.Security.Authorization.License;
using Skolestudio.Security.Authorization.Publications;

namespace Skolestudio.Web.UnitTests.Security.Authorization;

[TestFixture]
public class PublicationExtensionTests
{
    [Test]
    public void ToLicenseTagSet_Keeps_ContentLicense_Labels_And_Drops_The_Rest()
    {
        var labelDictionary = new Dictionary<string, List<string>>
        {
            [Tags.LearningMaterial] = ["multi"],
            [Tags.ContentStructure] = ["veien-til-toppidrett/oevelser"],
            [Tags.Isbn] = ["9788203263200"],
            [Tags.EducationalRole] = ["teacher"], // has a label key but is not transported to the license check
            [Tags.SeasonalEvent] = ["jul"],       // no TagType counterpart at all
        };

        var tagSet = labelDictionary.ToLicenseTagSet();

        tagSet.Should().ContainKey(TagType.LearningMaterial);
        tagSet.Should().ContainKey(TagType.ContentStructure);
        tagSet[TagType.ContentStructure].Should().Contain("veien-til-toppidrett/oevelser");
        tagSet.Should().ContainKey(TagType.Isbn);
        tagSet.Should().NotContainKey(TagType.EducationalRole);
    }

    [TestCase("product", "product=multi,salto", new[] { "salto" }, ExpectedResult = true)]
    [TestCase("product", "product=multi,salto", new[] { "refleks" }, ExpectedResult = false)]
    [TestCase("grade", "grade=1,2,3,4,5,6,7", new[] { "2" }, ExpectedResult = true)]
    [TestCase("grade", "grade=1,2,3,4,5,6,7", new[] { "8" }, ExpectedResult = false)]
    [TestCase("foo", "foo=1/2/3", new[] { "1/2/3" }, ExpectedResult = true)]
    [TestCase("foo", "foo=1/2/3,4,5,6/7/8", new[] { "6" }, ExpectedResult = false)]
    public bool Has_Access_To_Content_With_Matching_Descriptor(string labelKey, string publicationDescriptor, IEnumerable<string> contentTags)
    {
        var publicationDescriptorLabels = publicationDescriptor.GetDescriptorTags();

        var publicationProducts = publicationDescriptorLabels.GetValueOrDefault(labelKey);

        return contentTags.Intersect(publicationProducts!).Any();
    }

    [TestCase("test", "test=foo,bar", ExpectedResult = new[] { "foo", "bar" })]
    [TestCase("invalid tagKey", "test=foo,bar", ExpectedResult = new string[0])]
    [TestCase("", "test=foo,bar", ExpectedResult = new string[0])]
    [TestCase(null, "test=foo,bar", ExpectedResult = new string[0])]
    [TestCase(null, null, ExpectedResult = new string[0])]
    [TestCase("test", null, ExpectedResult = new string[0])]
    [TestCase("orange", "apple=green,red;orange=orange", ExpectedResult = new[] { "orange" })]
    [TestCase("orange", "apple=green,red;orange=orange", ExpectedResult = new[] { "orange" })]
    [TestCase("LearningMaterial", "Learningcomponent=Salaby;Learningmaterial=salaby-skole", ExpectedResult = new[] { "salaby-skole" })]
    public IEnumerable<string> Should_Parse_PublicationTags_Correctly(string tagKey, string publicationDescriptor)
    {
        var publication = new Publication { Descriptor = publicationDescriptor };

        return publication.GetTagsByKey(tagKey);
    }

    [TestCase("test", new[] { "test=foo,bar", "test=foo,bar,baz" }, ExpectedResult = new[] { "foo", "bar", "baz" })]
    [TestCase("test", new[] { "test=foo,bar", "TEST=UPPER,CASE,foo" }, ExpectedResult = new[] { "foo", "bar", "upper", "case" })]
    [TestCase("test", new[] { "test=foo,bar", "test=invalid AND format" }, ExpectedResult = new[] { "foo", "bar", "invalid", "format" })]
    [TestCase("test", new[] { "test=foo,bar", "test=lots       of         white          space" }, ExpectedResult = new[] { "foo", "bar", "lotsofwhitespace" })]
    [TestCase("test", new[] { ",test=trailing,commas," }, ExpectedResult = new[] { "trailing", "commas" })]
    [TestCase("subject", new[] { "Learningmaterial=salaby-skole;subject=kunst-og-haandverk,musikk,krle,kroppsoeving", "Subject=samfunnsfag" }, ExpectedResult = new[] { "kunst-og-haandverk", "musikk", "krle", "kroppsoeving", "samfunnsfag" })]
    [TestCase("foo", new[] { "foo=1,2,3;bar=4,5,6" }, ExpectedResult = new[] { "1", "2", "3" })]
    // invalid syntax: replaced semi colon with comma
    [TestCase("foo", new[] { "foo=1,2,3,bar=4,5,6" }, ExpectedResult = new string[0])]
    public IEnumerable<string> Should_Parse_PublicationTags_Correctly(string tagKey, IEnumerable<string> publicationDescriptors)
    {
        var publications = publicationDescriptors.Select(x => new Publication { Descriptor = x });

        return publications.GetTagsByKey(tagKey);
    }

    [TestCase("foo=1,2,3;bar=4,5,6", ExpectedResult = true)]
    [TestCase("foo=1,2,3,bar=4,5,6", ExpectedResult = false)] // replaced semi colon with comma
    [TestCase("foo=1,2,3;;;;", ExpectedResult = true)] // normalize removes extra valid characters
    [TestCase("foo=1,2,3,,", ExpectedResult = true)]
    [TestCase("foo==1", ExpectedResult = false)]
    [TestCase("foo=", ExpectedResult = false)]
    [TestCase("foo,bar", ExpectedResult = false)]
    [TestCase("", ExpectedResult = true)] // empty string is valid, but should cause FAIL_UserPublicationsEmpty in CheckUserPublicationAccess
    [TestCase("!#¤%&()@£$€{[]}....", ExpectedResult = true)] // normalize removes invalid characters
    public bool Should_Validate_Descriptor_Syntax(string descriptor)
    {
        return PublicationExtensions.IsDescriptorSyntaxValid(descriptor);
    }
}
