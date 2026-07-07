using Bff.Entitlements;
using Bff.Entitlements.Oracle;
using FluentAssertions;

namespace Bff.Entitlements.Tests;

/// <summary>
/// Runs the whole scenario corpus against the evaluator — and against the
/// oracle, so the corpus and the naive reference implementation keep each
/// other honest.
/// </summary>
[TestFixture]
public class ScenarioTests
{
    public static IEnumerable<TestCaseData> Corpus()
        => Scenario.LoadAll().Select(s => new TestCaseData(s).SetName($"scenario: {s.Name}"));

    [TestCaseSource(nameof(Corpus))]
    public void Evaluator_satisfies_scenario(Scenario scenario)
    {
        var trace = EntitlementEvaluator.Evaluate(scenario.ToInput());
        var explanation = string.Join("\n", trace.Explain());

        trace.Verdict.Should().Be(ExpectedVerdict(scenario), explanation);

        if (scenario.Reason is not null)
            trace.Reason.Should().Be(Enum.Parse<DecisionReason>(scenario.Reason), explanation);

        if (scenario.Because is not null)
            explanation.Should().Contain(scenario.Because);
    }

    [TestCaseSource(nameof(Corpus))]
    public void Oracle_satisfies_scenario(Scenario scenario)
    {
        var (verdict, reason) = OracleEvaluator.Evaluate(scenario.ToInput());

        verdict.Should().Be(ExpectedVerdict(scenario));

        if (scenario.Reason is not null)
            reason.Should().Be(Enum.Parse<DecisionReason>(scenario.Reason));
    }

    private static AccessVerdict ExpectedVerdict(Scenario scenario) => scenario.Expect.ToLowerInvariant() switch
    {
        "allow" => AccessVerdict.Allow,
        "deny" => AccessVerdict.Deny,
        _ => throw new ArgumentException($"scenario '{scenario.Name}': expect must be allow or deny, was '{scenario.Expect}'"),
    };
}
