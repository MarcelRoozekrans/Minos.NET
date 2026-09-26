using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Jev.Net.Generators;

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
internal sealed record QuestionSetModel(
    string? Namespace,
    string TypeName,
    string FullyQualifiedName,
    bool IsRecord,
    EquatableArray<QuestionModel> Questions);

/// <summary>The pipeline value: a model when the type is valid, and the diagnostics found.</summary>
internal sealed record QuestionSetResult(QuestionSetModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>A source location without a reference to the syntax tree, so it can be cached.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location?.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

/// <summary>A diagnostic in cacheable form.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Where, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] arguments)
        => new(descriptor, LocationInfo.From(symbol), new EquatableArray<string>(arguments));

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Descriptor, Where?.ToLocation(), Arguments.Cast<object>().ToArray());
}
