using Bff.Entitlements;
using FluentAssertions;

namespace Bff.Entitlements.Tests;

/// <summary>
/// Descriptor syntax and parsing rules, ported from the production
/// PublicationExtensionTests so the POC pins the same semantics.
/// </summary>
[TestFixture]
public class DescriptorParserTests
{
    [TestCase("foo=1,2,3;bar=4,5,6", ExpectedResult = true)]
    [TestCase("foo=1,2,3,bar=4,5,6", ExpectedResult = false)] // semicolon replaced with comma
    [TestCase("foo=1,2,3;;;;", ExpectedResult = true)]
    [TestCase("foo=1,2,3,,", ExpectedResult = true)]
    [TestCase(",test=trailing,commas,", ExpectedResult = true)]
    [TestCase("foo==1", ExpectedResult = false)]
    [TestCase("foo=", ExpectedResult = false)]
    [TestCase("foo,bar", ExpectedResult = false)]
    [TestCase("", ExpectedResult = true)] // valid but empty -> FAIL_PublicationsEmpty at evaluation
    [TestCase(null, ExpectedResult = true)]
    [TestCase("!#%&()@{[]}....", ExpectedResult = true)] // normalization strips invalid characters
    public bool Validates_descriptor_syntax(string? descriptor)
        => DescriptorParser.IsSyntaxValid(descriptor);

    [Test]
    public void Parses_known_keys_case_insensitively_and_records_unknown_keys()
    {
        var parsed = DescriptorParser.Parse("Learningcomponent=Salaby;Learningmaterial=salaby-skole;product=pilot");

        parsed.SyntaxValid.Should().BeTrue();
        parsed.Grants.Should().ContainKey(TagType.LearningComponent).WhoseValue.Should().Equal("salaby");
        parsed.Grants.Should().ContainKey(TagType.LearningMaterial).WhoseValue.Should().Equal("salaby-skole");
        parsed.UnknownKeys.Should().Equal("product");
    }

    [Test]
    public void Merges_duplicate_keys_and_normalizes_values()
    {
        var parsed = DescriptorParser.Parse("Subject=Kunst-og-Haandverk,musikk;SUBJECT=musikk,samfunnsfag");

        parsed.Grants[TagType.Subject].Should().Equal("kunst-og-haandverk", "musikk", "samfunnsfag");
    }

    [Test]
    public void Strips_whitespace_inside_values()
    {
        var parsed = DescriptorParser.Parse("subject=lots       of         white          space");

        parsed.Grants[TagType.Subject].Should().Equal("lotsofwhitespace");
    }

    [Test]
    public void An_all_garbage_descriptor_is_valid_and_empty()
    {
        var parsed = DescriptorParser.Parse("!#%&()@{[]}....");

        parsed.SyntaxValid.Should().BeTrue();
        parsed.Grants.Should().BeEmpty();
    }
}
