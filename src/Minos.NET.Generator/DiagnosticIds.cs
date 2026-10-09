namespace Minos.Generator;

/// <summary>
/// The ids of the rules <c>ModelBuilder</c> checks. Plain strings, not descriptors: the generator shares the
/// model builder but defines no rules. Minos.NET.Analyzers links this file and owns the descriptors.
/// </summary>
internal static class DiagnosticIds
{
    // The Jev API's own rules. JEV001 and JEV002 make a set invalid; JEV003–006 are advice and never block generation.
    public const string EmptyChoiceEnum = "JEV001";
    public const string EmptyScoreEnum = "JEV002";
    public const string EmptyText = "JEV003";
    public const string UnknownStateReference = "JEV004";
    public const string OptionCountOutsideGuidance = "JEV005";
    public const string MissingCriteria = "JEV006";

    // Declarations the generator cannot produce code for: each makes the set invalid.
    public const string UnsupportedType = "JEV101";
    public const string UnsupportedProperty = "JEV102";
    public const string AttributeTypeMismatch = "JEV103";
    public const string MissingLevel = "JEV104";
    public const string NoParameterlessConstructor = "JEV105";
    public const string DuplicateKey = "JEV106";
    public const string InvalidStateType = "JEV107";

    // Reported only by the run-time builder, for a built set's JSON nested too deep: declared sets are text only.
    public const string InvalidJson = "JEV108";

    /// <summary>Whether a diagnostic with <paramref name="id"/> is advice only: the set is still valid and generated.</summary>
    public static bool IsAdvisory(string id)
        => id is EmptyText or UnknownStateReference or OptionCountOutsideGuidance or MissingCriteria;
}
