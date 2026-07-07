namespace Bff.Entitlements;

/// <summary>
/// The closed vocabulary of tag keys. Descriptor keys and content label keys
/// that do not map to a member of this enum are dropped — visibly, via the
/// decision trace (see AssemblyReport / PublicationTrace.UnknownDescriptorKeys).
/// </summary>
public enum TagType
{
    LearningMaterial,
    LearningComponent,
    Isbn,
    Subject,
    ContentType,
    ContentStructure,
    Differentiation,
    Pricing,
    EducationalRole,
}

public static class TagTypes
{
    /// <summary>
    /// Tag types that participate in license checks. Content label keys outside
    /// this set (e.g. EducationalRole) are not transported to the evaluator.
    /// </summary>
    public static readonly IReadOnlySet<TagType> LicenseRelevant = new HashSet<TagType>
    {
        TagType.LearningMaterial,
        TagType.LearningComponent,
        TagType.Isbn,
        TagType.Subject,
        TagType.ContentType,
        TagType.ContentStructure,
        TagType.Differentiation,
        TagType.Pricing,
    };

    public static bool TryParse(string? key, out TagType tagType)
        => Enum.TryParse(key, ignoreCase: true, out tagType) && Enum.IsDefined(tagType);
}
