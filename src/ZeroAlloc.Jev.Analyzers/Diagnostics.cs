using Microsoft.CodeAnalysis;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Analyzers;

/// <summary>
/// The Jev API's rules (JEV001–006) and the declarations the generator cannot produce code for (JEV101–107). The
/// shared model builder records them by <see cref="DiagnosticIds"/>; <see cref="QuestionSetAnalyzer"/> reports them.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "ZeroAlloc.Jev";

    public static readonly DiagnosticDescriptor EmptyChoiceEnum = new(
        DiagnosticIds.EmptyChoiceEnum,
        "Choice enum has no members",
        "Choice enum '{0}' has no members: the API needs at least one option",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EmptyScoreEnum = new(
        DiagnosticIds.EmptyScoreEnum,
        "Score enum has no members",
        "Score enum '{0}' has no members: the API needs at least one level",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EmptyText = new(
        DiagnosticIds.EmptyText,
        "Empty instructions or description",
        "The {0} is an empty or whitespace string: write the text, or pass null to send none",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownStateReference = new(
        DiagnosticIds.UnknownStateReference,
        "Instructions refer to an unknown state member",
        "The instructions refer to '{0}', which matches no public property or field of the state type '{1}'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionCountOutsideGuidance = new(
        DiagnosticIds.OptionCountOutsideGuidance,
        "Option or level count outside the API guidance",
        "{0} enum '{1}' has {2} members; the Jev API guidance is {3}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingCriteria = new(
        DiagnosticIds.MissingCriteria,
        "Choice option has no description",
        "Choice option '{0}.{1}' has no [Criteria]: the API sees only its name, and a description usually helps",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        DiagnosticIds.UnsupportedType,
        "Unsupported question set type",
        "'{0}' must be a non-generic, non-abstract, top-level partial class or record to use [JevQuestions]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedProperty = new(
        DiagnosticIds.UnsupportedProperty,
        "Unsupported question property",
        "Question property '{0}' must be a partial, get-only instance property",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AttributeTypeMismatch = new(
        DiagnosticIds.AttributeTypeMismatch,
        "Question attribute does not match the property type",
        "Property '{0}' must carry exactly one question attribute matching its type; found [{1}]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingLevel = new(
        DiagnosticIds.MissingLevel,
        "Score level has no description",
        "Score level '{0}.{1}' needs a [Level] attribute: the API does not accept a level without a description",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        DiagnosticIds.NoParameterlessConstructor,
        "Question set has no parameterless constructor",
        "'{0}' needs a parameterless constructor so the generated Parse method can create it{1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateKey = new(
        DiagnosticIds.DuplicateKey,
        "Duplicate wire key",
        "The wire key '{0}' is used more than once in '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidStateType = new(
        DiagnosticIds.InvalidStateType,
        "Invalid state type",
        "State type '{0}' must be a class, struct, record or array type",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
