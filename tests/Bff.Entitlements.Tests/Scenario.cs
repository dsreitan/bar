using Bff.Entitlements;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Bff.Entitlements.Tests;

/// <summary>
/// One entry in the scenario corpus (tests/scenarios/*.yaml) — the executable
/// specification. Production incidents captured from the why endpoint are
/// added here and become permanent regression tests.
/// </summary>
public sealed class Scenario
{
    public string Name { get; set; } = "";

    /// <summary>Publication descriptors on the user's license; registry ids are synthesized as 1..n.</summary>
    public List<string> Publications { get; set; } = [];

    /// <summary>License publication ids that do NOT exist in the registry (suspect 1).</summary>
    public List<int> UnknownPublicationIds { get; set; } = [];

    public bool LicenseCheckDisabled { get; set; }

    /// <summary>Raw content labels — string keys on purpose, so transport drops are testable.</summary>
    public Dictionary<string, List<string?>> Content { get; set; } = [];

    /// <summary>"allow" or "deny".</summary>
    public string Expect { get; set; } = "";

    /// <summary>Optional: exact expected DecisionReason.</summary>
    public string? Reason { get; set; }

    /// <summary>Optional: substring that must appear in the trace explanation.</summary>
    public string? Because { get; set; }

    public EvaluationInput ToInput()
    {
        var registry = Publications
            .Select((descriptor, index) => new PublicationRecord(index + 1, $"scenario-pub-{index + 1}", descriptor))
            .ToDictionary(p => p.Id);

        return new EvaluationInput
        {
            UserPublicationIds = registry.Keys.Concat(UnknownPublicationIds).ToList(),
            PublicationRegistry = registry,
            RawContentLabels = Content.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string?>)pair.Value),
            LicenseCheckDisabled = LicenseCheckDisabled,
        };
    }

    public override string ToString() => Name;

    public static IEnumerable<Scenario> LoadAll()
    {
        var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "scenarios");
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml").OrderBy(f => f))
        foreach (var scenario in deserializer.Deserialize<List<Scenario>>(File.ReadAllText(file)))
            yield return scenario;
    }
}
