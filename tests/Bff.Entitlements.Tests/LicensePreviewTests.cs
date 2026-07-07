using Bff.Entitlements;
using FluentAssertions;

namespace Bff.Entitlements.Tests;

/// <summary>
/// License dry run: given a descriptor, which content does it unlock — and is
/// the search-filter translation faithful to the matcher?
/// </summary>
[TestFixture]
public class LicensePreviewTests
{
    private static readonly List<(string ContentId, string Title, IReadOnlyDictionary<string, IReadOnlyList<string?>> Labels)> Catalog =
    [
        Item("refleks-naturfag", new() { ["LearningMaterial"] = ["refleks"], ["Subject"] = ["naturfag"] }),
        Item("refleks-samfunnsfag", new() { ["LearningMaterial"] = ["refleks"], ["Subject"] = ["samfunnsfag"] }),
        Item("refleks-usubjektet", new() { ["LearningMaterial"] = ["refleks"] }), // no Subject tag at all
        Item("multi-tavle", new() { ["LearningMaterial"] = ["multi"] }),
        Item("toppidrett-oevelser", new() { ["LearningMaterial"] = ["veien"], ["ContentStructure"] = ["veien-til-toppidrett/oevelser"] }),
    ];

    [Test]
    public void Preview_lists_exactly_the_content_a_new_license_would_unlock()
    {
        var preview = LicensePreviewer.Preview("LearningMaterial=refleks;Subject=naturfag", Catalog);

        preview.SyntaxValid.Should().BeTrue();
        preview.Matches.Select(m => m.ContentId).Should().BeEquivalentTo(
            "refleks-naturfag",
            // The subtlety a naive search filter gets wrong: constraints only
            // bind on keys the content carries, so untagged-Subject content matches.
            "refleks-usubjektet");
    }

    [Test]
    public void Preview_supports_hierarchical_grants()
    {
        var preview = LicensePreviewer.Preview("ContentStructure=veien-til-toppidrett", Catalog);

        preview.Matches.Select(m => m.ContentId).Should().BeEquivalentTo("toppidrett-oevelser");
        preview.Matches.Single().MatchedOn.Single()
            .Should().Contain("'veien-til-toppidrett' covers 'veien-til-toppidrett/oevelser'");
    }

    [Test]
    public void Preview_of_an_invalid_descriptor_warns_about_the_fail_open_blast_radius_instead_of_matching()
    {
        var preview = LicensePreviewer.Preview("LearningMaterial=multi,Subject=matematikk", Catalog);

        preview.SyntaxValid.Should().BeFalse();
        preview.Matches.Should().BeEmpty();
        preview.Filter.Should().BeNull();
        preview.Warnings.Should().ContainMatch("*ENTIRE catalog*fail-open*");
    }

    [Test]
    public void Preview_warns_about_unknown_keys_and_dead_grants()
    {
        var preview = LicensePreviewer.Preview("product=pilot", Catalog);

        preview.Warnings.Should().ContainMatch("*'product' is not a known tag type*");
        preview.Warnings.Should().ContainMatch("*would grant nothing*");

        LicensePreviewer.Preview("LearningMaterial=viov", Catalog)
            .Warnings.Should().ContainMatch("*no content in the catalog matches*");
    }

    [Test]
    public void The_search_filter_query_string_encodes_the_bind_only_when_tagged_rule()
    {
        var preview = LicensePreviewer.Preview("LearningMaterial=refleks;Subject=naturfag", Catalog);

        preview.Filter!.ToQueryString().Should().Be(
            "(_exists_:LearningMaterial OR _exists_:Subject)"
            + " AND (NOT _exists_:LearningMaterial OR LearningMaterial:refleks OR LearningMaterial:refleks/*)"
            + " AND (NOT _exists_:Subject OR Subject:naturfag OR Subject:naturfag/*)");
    }

    private static (string, string, IReadOnlyDictionary<string, IReadOnlyList<string?>>) Item(
        string id, Dictionary<string, IReadOnlyList<string?>> labels) => (id, $"title:{id}", labels);
}
