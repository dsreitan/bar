namespace Bff.Entitlements;

/// <summary>A publication as known by the publication registry.</summary>
public sealed record PublicationRecord(int Id, string Name, string? Descriptor);

/// <summary>
/// Everything the entitlement decision depends on, captured as plain data.
/// Evaluate(input) is deterministic: same input, same trace — which is what
/// makes production incidents replayable as tests.
/// </summary>
public sealed record EvaluationInput
{
    /// <summary>Publication ids on the user's license, as issued by the license system.</summary>
    public required IReadOnlyList<int> UserPublicationIds { get; init; }

    /// <summary>The publication registry: id → publication.</summary>
    public required IReadOnlyDictionary<int, PublicationRecord> PublicationRegistry { get; init; }

    /// <summary>Raw content labels as tagged upstream, before transport to the license vocabulary.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string?>> RawContentLabels { get; init; }

    /// <summary>Mirrors the LicenseCheckNone feature flag.</summary>
    public bool LicenseCheckDisabled { get; init; }
}
