using Bff.Entitlements;
using FluentAssertions;

namespace Bff.Entitlements.Tests;

/// <summary>
/// Value matching semantics (spec rule 8 details), ported from the production
/// IsMatchingTags tests — including the deliberate fail-open on invalid
/// content values and fail-closed on invalid publication values.
/// </summary>
[TestFixture]
public class ValueMatchingTests
{
    [TestCase(new[] { "foo/bar" }, new[] { "foo/bar" }, ExpectedResult = true)]
    [TestCase(new[] { "foo/bar/baz" }, new[] { "foo/bar/baz" }, ExpectedResult = true)]
    [TestCase(new[] { "foo" }, new[] { "foo" }, ExpectedResult = true)]
    [TestCase(new[] { "foo" }, new[] { "bar" }, ExpectedResult = false)]
    // hierarchical: a publication root grants access to descendant content
    [TestCase(new[] { "foo" }, new[] { "foo/bar" }, ExpectedResult = true)]
    [TestCase(new[] { "veien-til-toppidrett" }, new[] { "veien-til-toppidrett/oevelser" }, ExpectedResult = true)]
    [TestCase(new[] { "foo/bar" }, new[] { "foo" }, ExpectedResult = false)]
    [TestCase(new[] { "foo" }, new[] { "foobar" }, ExpectedResult = false)] // '/' is the boundary
    public bool Matches_values_hierarchically(string?[] publicationValues, string?[] contentValues)
        => EntitlementEvaluator.ValuesMatch(publicationValues, contentValues).Matched;

    [Test]
    public void Invalid_content_values_match_fail_open()
    {
        EntitlementEvaluator.ValuesMatch(["foo"], [null]).Matched.Should().BeTrue();
        EntitlementEvaluator.ValuesMatch(["foo"], null).Matched.Should().BeTrue();
    }

    [Test]
    public void Invalid_publication_values_never_match()
    {
        EntitlementEvaluator.ValuesMatch([null], ["foo"]).Matched.Should().BeFalse();
        EntitlementEvaluator.ValuesMatch(null, ["foo"]).Matched.Should().BeFalse();
    }
}
