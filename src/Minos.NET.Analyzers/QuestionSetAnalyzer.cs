using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Minos.Generator;

namespace Minos.Analyzers;

/// <summary>
/// Reports the <c>[Questions]</c> diagnostics. It runs the same model builder as the generator, which reports
/// nothing and emits only throwing stubs for an invalid set, so the two cannot disagree about what is valid.
/// </summary>
/// <remarks>
/// Every diagnostic is local: it points inside the declaration of the symbol whose action reports it. The IDE analyses
/// the open document alone and shows only the local diagnostics it finds there, together with their code fixes. So a
/// set reports its own rules, and those about an enum from another assembly, on the property that uses it; an enum
/// declared in this compilation reports its own rules, for every way the sets use it, from its own action. Each
/// finding is then reported exactly once, by the one symbol it belongs to, and needs no deduplication across sets.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QuestionSetAnalyzer : DiagnosticAnalyzer
{
    private const string QuestionsAttribute = "Minos.QuestionsAttribute";

    private static readonly ImmutableArray<DiagnosticDescriptor> Descriptors = ImmutableArray.Create(
        Diagnostics.EmptyChoiceEnum,
        Diagnostics.EmptyScoreEnum,
        Diagnostics.EmptyText,
        Diagnostics.UnknownStateReference,
        Diagnostics.OptionCountOutsideGuidance,
        Diagnostics.MissingCriteria,
        Diagnostics.UnsupportedType,
        Diagnostics.UnsupportedProperty,
        Diagnostics.AttributeTypeMismatch,
        Diagnostics.MissingLevel,
        Diagnostics.NoParameterlessConstructor,
        Diagnostics.DuplicateKey,
        Diagnostics.InvalidStateType);

    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> DescriptorsById
        = Descriptors.ToImmutableDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Descriptors;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // These rules decide what the generator emits, and the generator reads a declaration wherever it lives. A set or
        // enum in generated code must report here too, or its build shows only a follow-on error, such as CS0117 for the
        // missing QuestionsUtf8, without the MIN diagnostic that explains it.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(static start =>
        {
            var attributeType = start.Compilation.GetTypeByMetadataName(QuestionsAttribute);
            if (attributeType is not null)
            {
                var enumUsages = new EnumUsageIndex(start.Compilation, attributeType);
                start.RegisterSymbolAction(
                    symbol =>
                    {
                        var type = (INamedTypeSymbol)symbol.Symbol;
                        if (type.TypeKind == TypeKind.Enum)
                        {
                            AnalyzeEnum(symbol, type, enumUsages);
                        }
                        else
                        {
                            AnalyzeSet(symbol, type, attributeType);
                        }
                    },
                    SymbolKind.NamedType);
            }
        });
    }

    /// <summary>The rules of a <c>[Questions]</c> type and its properties.</summary>
    private static void AnalyzeSet(SymbolAnalysisContext context, INamedTypeSymbol type, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                Report(context, ModelBuilder.Build(type, attribute, context.CancellationToken).Diagnostics);
                return;
            }
        }
    }

    /// <summary>The rules of an enum, once for each way the sets use it: as a Choice, a Score, or both.</summary>
    private static void AnalyzeEnum(SymbolAnalysisContext context, INamedTypeSymbol enumType, EnumUsageIndex enumUsages)
    {
        var usage = enumUsages.For(enumType, context.CancellationToken);
        if ((usage & EnumUsage.Choice) != 0)
        {
            Report(context, ModelBuilder.ValidateEnum(enumType, QuestionKind.Choice, context.CancellationToken));
        }

        if ((usage & EnumUsage.Score) != 0)
        {
            Report(context, ModelBuilder.ValidateEnum(enumType, QuestionKind.Score, context.CancellationToken));
        }
    }

    private static void Report(SymbolAnalysisContext context, EquatableArray<DiagnosticInfo> diagnostics)
    {
        foreach (var info in diagnostics)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DescriptorsById[info.Id], info.Location, info.Arguments.Cast<object>().ToArray()));
        }
    }
}
