namespace Minos.Generator;

/// <summary>
/// The ids of the rules <c>ModelBuilder</c> checks. Plain strings, not descriptors: the generator shares the
/// model builder but defines no rules. Minos.NET.Analyzers links this file and owns the descriptors.
/// </summary>
internal static class DiagnosticIds
{
    // The Jev API's own rules. MIN001 and MIN002 make a set invalid; MIN003–006 are advice and never block generation.
    public const string EmptyChoiceEnum = "MIN001";
    public const string EmptyScoreEnum = "MIN002";
    public const string EmptyText = "MIN003";
    public const string UnknownStateReference = "MIN004";
    public const string OptionCountOutsideGuidance = "MIN005";
    public const string MissingCriteria = "MIN006";

    // Declarations the generator cannot produce code for: each makes the set invalid.
    public const string UnsupportedType = "MIN101";
    public const string UnsupportedProperty = "MIN102";
    public const string AttributeTypeMismatch = "MIN103";
    public const string MissingLevel = "MIN104";
    public const string NoParameterlessConstructor = "MIN105";
    public const string DuplicateKey = "MIN106";
    public const string InvalidStateType = "MIN107";

    // Reported only by the run-time builder, for a built set's JSON nested too deep: declared sets are text only.
    public const string InvalidJson = "MIN108";

    /// <summary>Whether a diagnostic with <paramref name="id"/> is advice only: the set is still valid and generated.</summary>
    public static bool IsAdvisory(string id)
        => id is EmptyText or UnknownStateReference or OptionCountOutsideGuidance or MissingCriteria;
}
