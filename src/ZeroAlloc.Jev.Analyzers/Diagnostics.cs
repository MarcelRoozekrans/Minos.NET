using Microsoft.CodeAnalysis;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Analyzers;

/// <summary>
/// Errors for declarations the generator cannot produce code for. The shared model builder records them by
/// <see cref="DiagnosticIds"/>; <see cref="QuestionSetAnalyzer"/> reports them.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "ZeroAlloc.Jev";

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
        "Property '{0}' must carry exactly one question attribute, and [{1}] requires the type {2}",
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
        "'{0}' needs a parameterless constructor so the generated Parse method can create it",
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
