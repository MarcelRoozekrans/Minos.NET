using Microsoft.CodeAnalysis;

namespace Minos.Generator;

internal enum QuestionKind
{
    Noul,
    Choice,
    Score,
}

/// <summary>One Choice option or Score level, in wire order.</summary>
/// <param name="MemberName">The enum member, escaped as a C# identifier.</param>
/// <param name="Key">The wire key: snake_case name or override for a Choice, the level index for a Score.</param>
/// <param name="DescriptionJson">
/// The criterion or level as a wire fragment: a JSON string, or a criterion object when <c>Examples</c> or
/// <c>NotFor</c> is set. <see langword="null"/> sends JSON <c>null</c>.
/// </param>
internal sealed record OptionModel(string MemberName, string Key, string? DescriptionJson);

/// <summary>One question property.</summary>
/// <param name="PropertyName">The property, escaped as a C# identifier.</param>
/// <param name="Modifiers">The declaration's accessibility modifiers, repeated on the implementation.</param>
/// <param name="EnumType">The fully qualified enum for Choice and Score; empty for Noul.</param>
/// <param name="InstructionsJson">The instructions as a wire fragment: a JSON string.</param>
internal sealed record QuestionModel(
    string PropertyName,
    string Modifiers,
    QuestionKind Kind,
    string Key,
    string InstructionsJson,
    string? WhenTrue,
    string? WhenFalse,
    string EnumType,
    EquatableArray<OptionModel> Options);

/// <summary>One <c>[Questions]</c> type.</summary>
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

/// <summary>One accessor of a stubbed property, as the definition declares it.</summary>
/// <param name="Modifiers">The accessor's own modifiers, such as <c>private</c>; empty when it has none.</param>
/// <param name="Keyword"><c>get</c>, <c>set</c> or <c>init</c>.</param>
internal sealed record StubAccessorModel(string Modifiers, string Keyword);

/// <summary>One partial question property of an invalid set that the generator implements with a throwing stub.</summary>
/// <param name="Modifiers">The definition's modifiers as written, in order, <c>partial</c> included.</param>
/// <param name="TypeName">The property's fully qualified type, as declared.</param>
/// <param name="PropertyName">The property, escaped as a C# identifier.</param>
/// <param name="Accessors">The definition's accessors, in order.</param>
internal sealed record StubPropertyModel(
    string Modifiers,
    string TypeName,
    string PropertyName,
    EquatableArray<StubAccessorModel> Accessors);

/// <summary>One type declaration a stub file repeats: the invalid set itself, or one of the types containing it.</summary>
/// <param name="Modifiers">
/// The accessibility modifiers as declared, for a containing type; empty when none is declared, and always empty for
/// the set itself, like the valid emitter's declaration part.
/// </param>
/// <param name="Keyword"><c>class</c>, <c>record</c>, <c>struct</c>, <c>record struct</c> or <c>interface</c>.</param>
/// <param name="Name">The type's name, escaped as a C# identifier.</param>
/// <param name="TypeParameters">
/// The type parameter list with each parameter's variance, such as <c>&lt;in T, U&gt;</c>, or empty. Constraints are
/// left out: a partial part may omit them.
/// </param>
/// <param name="Arity">The number of type parameters, which keeps the hint names of <c>Set</c> and <c>Set&lt;T&gt;</c> apart.</param>
internal sealed record StubTypeModel(string Modifiers, string Keyword, string Name, string TypeParameters, int Arity);

/// <summary>
/// An invalid <c>[Questions]</c> type whose partial question properties the generator implements anyway. The
/// stubs keep the compiler from reporting CS9248, an unimplemented partial property: that declaration error would
/// stop a command-line build before the analyzer runs, hiding the JEV error that explains the problem. The type may
/// be one MIN101 rejects, as long as a partial part can complete it: a nested, generic, abstract or static type, or
/// one that is not a class, so the model carries each declaration a partial part repeats.
/// </summary>
/// <param name="Namespace">The containing namespace, escaped; <see langword="null"/> for the global namespace.</param>
/// <param name="ContainingTypes">The types containing the set, outermost first; empty for a top-level set.</param>
/// <param name="Type">The set's own declaration.</param>
/// <param name="Properties">The stubs.</param>
internal sealed record InvalidSetModel(
    string? Namespace,
    EquatableArray<StubTypeModel> ContainingTypes,
    StubTypeModel Type,
    EquatableArray<StubPropertyModel> Properties);

/// <summary>
/// What <see cref="ModelBuilder"/> finds for one <c>[Questions]</c> type: a model when the type is valid, the stubs
/// to emit when it is invalid but has implementable question properties, and the problems found. The generator keeps
/// the model or the stubs; the analyzer reports the diagnostics.
/// </summary>
/// <remarks>
/// <see cref="Diagnostics"/> holds what the set reports itself. It leaves out the rules about an enum declared in the
/// same assembly, which the enum reports through <see cref="ModelBuilder.ValidateEnum"/>, though they still decide
/// whether the set is valid. The rules about an enum from another assembly are included, on the property using it.
/// </remarks>
internal sealed record QuestionSetResult(
    QuestionSetModel? Model,
    InvalidSetModel? InvalidSet,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// A problem with a <c>[Questions]</c> type, by rule id. It carries no <see cref="DiagnosticDescriptor"/>:
/// the descriptors live only in Minos.NET.Analyzers, so the generator, which shares this file, defines no rules.
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
