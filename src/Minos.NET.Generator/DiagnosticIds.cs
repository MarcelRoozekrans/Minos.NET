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
    public const string MissingCriterion = "MIN006";

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

    /// <summary>A provider cannot answer a question of this kind.</summary>
    public const string UnsupportedQuestionKind = "MIN201";

    /// <summary>A request holds more questions than the provider accepts.</summary>
    public const string TooManyQuestions = "MIN202";

    /// <summary>A Choice question has more options than the provider accepts.</summary>
    public const string TooManyChoiceOptions = "MIN203";

    /// <summary>
    /// A Noul question has <c>WhenTrue</c> or <c>WhenFalse</c>, which the provider cannot send. A run-time provider rule
    /// with no analyzer, like MIN201 to MIN203.
    /// </summary>
    public const string NoulCriteriaUnsupported = "MIN204";

    /// <summary>
    /// A criterion has <c>Examples</c> or <c>NotFor</c>, which the provider cannot send. A run-time provider rule with no
    /// analyzer, like MIN201 to MIN203.
    /// </summary>
    public const string CriterionDetailUnsupported = "MIN205";

    /// <summary>
    /// A request has images and the provider does not accept them. A run-time provider rule with no analyzer, like
    /// MIN201 to MIN203.
    /// </summary>
    public const string ImagesUnsupported = "MIN206";

    // MIN207 to MIN211: what an upstream Microsoft.Extensions.AI decision client cannot take, which the adapter over it
    // checks before sending. Run-time provider rules with no analyzer, like MIN201 to MIN203.

    /// <summary>A request has no questions, and the provider requires at least one.</summary>
    public const string EmptyQuestionSet = "MIN207";

    /// <summary>A question key or a Choice option key is white space only, which the provider refuses as an id.</summary>
    public const string BlankId = "MIN208";

    /// <summary>A Noul's <c>WhenTrue</c> or <c>WhenFalse</c>, or a text criterion, is empty or white space only.</summary>
    public const string BlankDescription = "MIN209";

    /// <summary>A Noul's instructions are blank text and it lacks <c>WhenTrue</c> or <c>WhenFalse</c>.</summary>
    public const string NoulWithoutInstructions = "MIN210";

    /// <summary>A Score question has one level, and the provider requires at least two.</summary>
    public const string SingleLevelScore = "MIN211";

    /// <summary>Whether a diagnostic with <paramref name="id"/> is advice only: the set is still valid and generated.</summary>
    public static bool IsAdvisory(string id)
        => id is EmptyText or UnknownStateReference or OptionCountOutsideGuidance or MissingCriterion;
}
