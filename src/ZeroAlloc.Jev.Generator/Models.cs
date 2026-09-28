using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Jev.Generator;

internal enum QuestionKind
{
    Noul,
    Choice,
    Score,
}

/// <summary>One Choice option or Score level, in wire order.</summary>
/// <param name="MemberName">The enum member, escaped as a C# identifier.</param>
/// <param name="Key">The wire key: snake_case name or override for a Choice, the level index for a Score.</param>
/// <param name="Description">The criterion or level text; <see langword="null"/> sends JSON <c>null</c>.</param>
internal sealed record OptionModel(string MemberName, string Key, string? Description);

/// <summary>One question property.</summary>
/// <param name="PropertyName">The property, escaped as a C# identifier.</param>
/// <param name="Modifiers">The declaration's accessibility modifiers, repeated on the implementation.</param>
/// <param name="EnumType">The fully qualified enum for Choice and Score; empty for Noul.</param>
internal sealed record QuestionModel(
    string PropertyName,
    string Modifiers,
    QuestionKind Kind,
    string Key,
    string Instructions,
    string? WhenTrue,
    string? WhenFalse,
    string EnumType,
    EquatableArray<OptionModel> Options);

/// <summary>One <c>[JevQuestions]</c> type.</summary>
/// <param name="StateTypeName">
/// The fully qualified name of the <c>State</c> named argument's type, or <see langword="null"/> when the
/// attribute carries no <c>State</c>. A string, never an <see cref="ISymbol"/>, so the model stays
/// value-equatable for incremental caching.
/// </param>
internal sealed record QuestionSetModel(
    string? Namespace,
    string TypeName,
    string FullyQualifiedName,
    bool IsRecord,
    string? StateTypeName,
    EquatableArray<QuestionModel> Questions);

/// <summary>
/// What <see cref="ModelBuilder"/> finds for one <c>[JevQuestions]</c> type: a model when the type is valid, and the
/// problems found otherwise. The generator keeps only the model; the analyzer reports the diagnostics.
/// </summary>
internal sealed record QuestionSetResult(QuestionSetModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// A problem with a <c>[JevQuestions]</c> type, by rule id. It carries no <see cref="DiagnosticDescriptor"/>:
/// the descriptors live only in ZeroAlloc.Jev.Analyzers, so the generator, which shares this file, defines no rules.
/// </summary>
/// <param name="Id">One of the <see cref="DiagnosticIds"/>.</param>
/// <param name="Location">Where to report it, or <see langword="null"/> when there is no source location.</param>
/// <param name="Arguments">The message format arguments.</param>
internal sealed record DiagnosticInfo(string Id, Location? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(string id, ISymbol symbol, params string[] arguments)
        => new(id, SourceLocation(symbol), new EquatableArray<string>(arguments));

    public static DiagnosticInfo Create(string id, Location? location, params string[] arguments)
        => new(id, location, new EquatableArray<string>(arguments));

    /// <summary>The symbol's first location in source, or <see langword="null"/> when it has none.</summary>
    public static Location? SourceLocation(ISymbol symbol) => symbol.Locations.FirstOrDefault(l => l.IsInSource);
}
