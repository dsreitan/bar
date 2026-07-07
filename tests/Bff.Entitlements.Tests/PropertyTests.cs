using Bff.Entitlements;
using Bff.Entitlements.Oracle;
using CsCheck;
using FluentAssertions;

namespace Bff.Entitlements.Tests;

/// <summary>
/// Invariants that must hold for ANY combination of publications and content —
/// the answer to "users can combine everything, it's a mess". Example tests
/// can't cover the combination space; these properties do.
/// </summary>
[TestFixture]
public class PropertyTests
{
    private const int Iterations = 2000;

    // -- generators ---------------------------------------------------------

    private static Gen<T> Pick<T>(params T[] items) => Gen.Int[0, items.Length - 1].Select(i => items[i]);

    private static readonly Gen<string> GenBaseValue =
        Pick("a", "b", "multi", "salto", "refleks", "naturfag", "samfunnsfag", "fagrom");

    private static readonly Gen<string> GenValue =
        Gen.Select(GenBaseValue, Pick("", "/x", "/x/y"), (root, suffix) => root + suffix);

    private static readonly Gen<TagType> GenTagType =
        Pick(TagType.LearningMaterial, TagType.LearningComponent, TagType.Subject, TagType.ContentType, TagType.Differentiation);

    private static readonly Gen<(TagType Key, List<string> Values)> GenSegment =
        Gen.Select(GenTagType, GenValue.List[1, 3], (key, values) => (key, values));

    /// <summary>Structurally valid descriptor over the real tag vocabulary.</summary>
    private static readonly Gen<string> GenValidDescriptor =
        GenSegment.List[1, 3].Select(segments =>
            string.Join(";", segments.Select(s => $"{s.Key}={string.Join(",", s.Values)}")));

    /// <summary>Descriptor mix including data defects: invalid syntax, unknown keys, garbage.</summary>
    private static readonly Gen<string> GenAnyDescriptor = Gen.Frequency(
        (8, GenValidDescriptor),
        (1, Pick("LearningMaterial=multi,Subject=matematikk", "product=pilot,skillsho", "", "foo==1", "!#%&")),
        (1, Gen.String[0, 20]));

    private static readonly Gen<Dictionary<string, IReadOnlyList<string?>>> GenContentLabels =
        Gen.Select(GenTagType, GenValue.List[1, 2], (key, values) => (Key: key.ToString(), Values: values))
            .List[0, 3]
            .Select(pairs => pairs
                .GroupBy(p => p.Key)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string?>)g.First().Values.Cast<string?>().ToList()));

    private static readonly Gen<EvaluationInput> GenInput =
        Gen.Select(GenAnyDescriptor.List[0, 4], GenContentLabels, Gen.Int[0, 9], (descriptors, content, roll) =>
            BuildInput(descriptors, content, roll == 0 ? [999] : Array.Empty<int>()));

    private static EvaluationInput BuildInput(
        IReadOnlyList<string> descriptors,
        Dictionary<string, IReadOnlyList<string?>> content,
        IReadOnlyList<int> unknownIds)
    {
        var registry = descriptors
            .Select((descriptor, index) => new PublicationRecord(index + 1, $"pub-{index + 1}", descriptor))
            .ToDictionary(p => p.Id);

        return new EvaluationInput
        {
            UserPublicationIds = registry.Keys.Concat(unknownIds).ToList(),
            PublicationRegistry = registry,
            RawContentLabels = content,
        };
    }

    // -- properties ---------------------------------------------------------

    [Test]
    public void Evaluator_always_agrees_with_the_oracle()
    {
        GenInput.Sample(input =>
        {
            var trace = EntitlementEvaluator.Evaluate(input);
            var (verdict, reason) = OracleEvaluator.Evaluate(input);

            trace.Verdict.Should().Be(verdict, string.Join("\n", trace.Explain()));
            trace.Reason.Should().Be(reason);
        }, iter: Iterations);
    }

    [Test]
    public void Buying_more_never_revokes_access()
    {
        // The "bought more, now less works" bug class: the model is purely
        // additive, so adding any publication must preserve every Allow.
        Gen.Select(GenInput, GenAnyDescriptor, (input, extraDescriptor) => (input, extraDescriptor))
            .Sample(t =>
            {
                var before = EntitlementEvaluator.Evaluate(t.input);

                var extraId = t.input.PublicationRegistry.Keys.DefaultIfEmpty(0).Max() + 1;
                var registry = new Dictionary<int, PublicationRecord>(t.input.PublicationRegistry)
                {
                    [extraId] = new PublicationRecord(extraId, "extra", t.extraDescriptor),
                };
                var after = EntitlementEvaluator.Evaluate(t.input with
                {
                    UserPublicationIds = t.input.UserPublicationIds.Append(extraId).ToList(),
                    PublicationRegistry = registry,
                });

                if (before.Verdict == AccessVerdict.Allow)
                    after.Verdict.Should().Be(AccessVerdict.Allow);
            }, iter: Iterations);
    }

    [Test]
    public void Extending_a_descriptor_restricts_on_new_keys_and_widens_on_existing_keys()
    {
        // Two distinct monotonicity directions, both real semantics:
        //  - a segment with a NEW key adds a constraint: it never grants more,
        //    provided key overlap with the content already existed (without
        //    overlap it can create the first overlap and turn deny into allow),
        //  - a segment reusing an EXISTING key merges values into that key:
        //    it never revokes.
        // (This distinction was discovered by this property's first failure.)
        Gen.Select(GenValidDescriptor, GenSegment, GenContentLabels, (descriptor, extra, content) => (descriptor, extra, content))
            .Sample(t =>
            {
                var (contentTags, _) = ContentTagTransport.ToLicenseTagSet(t.content);
                var narrow = new PublicationRecord(1, "narrow", t.descriptor);
                var wide = new PublicationRecord(1, "wide", $"{t.descriptor};{t.extra.Key}={string.Join(",", t.extra.Values)}");

                var narrowCheck = EntitlementEvaluator.CheckPublication(narrow, contentTags);
                var wideCheck = EntitlementEvaluator.CheckPublication(wide, contentTags);
                var keyAlreadyConstrained = DescriptorParser.Parse(t.descriptor).Grants.ContainsKey(t.extra.Key);

                if (keyAlreadyConstrained)
                {
                    if (narrowCheck.HasAccess)
                        wideCheck.HasAccess.Should().BeTrue("merging extra values into an existing key never revokes");
                }
                else if (narrowCheck.Outcome is not (PublicationOutcome.NoKeyOverlap or PublicationOutcome.Empty))
                {
                    if (wideCheck.HasAccess)
                        narrowCheck.HasAccess.Should().BeTrue("a new-key constraint never grants more when overlap already existed");
                }
            }, iter: Iterations);
    }

    [Test]
    public void Publication_root_covers_all_descendants_and_slash_is_a_hard_boundary()
    {
        Gen.Select(GenBaseValue, Pick("x", "x/y", "1"), (root, tail) => (root, tail))
            .Sample(t =>
            {
                EntitlementEvaluator.ValuesMatch([t.root], [$"{t.root}/{t.tail}"]).Matched
                    .Should().BeTrue("a publication root covers descendant content");

                EntitlementEvaluator.ValuesMatch([t.root], [t.root + t.tail]).Matched
                    .Should().BeFalse("a prefix without the / boundary is not a match");

                EntitlementEvaluator.ValuesMatch([$"{t.root}/{t.tail}"], [t.root]).Matched
                    .Should().BeFalse("a child publication does not grant parent content");
            }, iter: Iterations);
    }

    [Test]
    public void Publication_order_never_changes_the_verdict()
    {
        GenInput.Sample(input =>
        {
            var forward = EntitlementEvaluator.Evaluate(input);
            var reversed = EntitlementEvaluator.Evaluate(input with
            {
                UserPublicationIds = input.UserPublicationIds.Reverse().ToList(),
            });

            reversed.Verdict.Should().Be(forward.Verdict);
        }, iter: Iterations);
    }

    [Test]
    public void The_trace_never_lies_about_which_publication_decided()
    {
        // Allow -> the publication the trace names grants access on its own.
        // Deny  -> no publication on the license grants access on its own.
        GenInput.Sample(input =>
        {
            var trace = EntitlementEvaluator.Evaluate(input);
            if (trace.Publications.Count == 0)
                return; // decided by an open-content gate

            EvaluationInput SinglePublication(int id) => input with { UserPublicationIds = [id] };

            if (trace.Verdict == AccessVerdict.Allow)
            {
                var winner = trace.Publications.First(p => p.HasAccess);
                EntitlementEvaluator.Evaluate(SinglePublication(winner.PublicationId))
                    .Verdict.Should().Be(AccessVerdict.Allow);
            }
            else
            {
                foreach (var publication in trace.Publications)
                    EntitlementEvaluator.Evaluate(SinglePublication(publication.PublicationId))
                        .Verdict.Should().Be(AccessVerdict.Deny);
            }
        }, iter: Iterations);
    }

    [Test]
    public void Normalization_is_idempotent_and_the_parser_never_throws()
    {
        Gen.String[0, 30].Sample(raw =>
        {
            var once = Normalizer.NormalizeDescriptor(raw);
            Normalizer.NormalizeDescriptor(once).Should().Be(once);

            var act = () => DescriptorParser.Parse(raw);
            act.Should().NotThrow();
        }, iter: Iterations);
    }
}
