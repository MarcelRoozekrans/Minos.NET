using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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

/// <summary>The pipeline value: a model when the type is valid, and the diagnostics found.</summary>
internal sealed record QuestionSetResult(QuestionSetModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// A source location kept as a syntax tree and span. This deliberately holds the <see cref="SyntaxTree"/>
/// itself (rather than, say, a file path and line span) so that <see cref="ToLocation"/> produces a real
/// <see cref="LocationKind.SourceFile"/> location with <c>Location.IsInSource == true</c> — diagnostics
/// the generator reports stay attached to the offending source line instead of degrading to an
/// <see cref="LocationKind.ExternalFile"/> location, which the file-path-only
/// <see cref="Location.Create(string, TextSpan, LinePositionSpan)"/> form always produces.
/// This is only reached for an invalid type (one that reports a diagnostic), so at most one tree per
/// invalid <c>[JevQuestions]</c> type is ever carried in the pipeline model.
/// Record equality (and therefore incremental caching) then relies on the tree instance being unchanged
/// when unrelated files are edited elsewhere in the compilation — Roslyn reuses the same
/// <see cref="SyntaxTree"/> object for files an edit did not touch, so this holds in practice, but it is a
/// deliberate departure from the generator cookbook's general guidance to avoid carrying syntax/symbols in
/// cached pipeline values.
/// </summary>
internal sealed record LocationInfo(SyntaxTree Tree, TextSpan Span)
{
    public static LocationInfo? From(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location?.SourceTree is null ? null : new LocationInfo(location.SourceTree, location.SourceSpan);
    }

    /// <summary>Builds a location from a syntax node, for diagnostics that have no owning symbol to hang off.</summary>
    public static LocationInfo From(SyntaxNode node) => new(node.SyntaxTree, node.Span);

    public Location ToLocation() => Location.Create(Tree, Span);
}

/// <summary>A diagnostic in cacheable form.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Where, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] arguments)
        => new(descriptor, LocationInfo.From(symbol), new EquatableArray<string>(arguments));

    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
        => new(descriptor, location, new EquatableArray<string>(arguments));

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Descriptor, Where?.ToLocation(), Arguments.Cast<object>().ToArray());
}
