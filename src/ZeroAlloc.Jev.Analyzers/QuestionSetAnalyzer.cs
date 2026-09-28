using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Analyzers;

/// <summary>
/// Reports the <c>[JevQuestions]</c> diagnostics. It runs the same model builder as the generator, which only
/// skips an invalid set, so the two cannot disagree about what is valid.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QuestionSetAnalyzer : DiagnosticAnalyzer
{
    private const string JevQuestionsAttribute = "ZeroAlloc.Jev.JevQuestionsAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Diagnostics.UnsupportedType,
        Diagnostics.UnsupportedProperty,
        Diagnostics.AttributeTypeMismatch,
        Diagnostics.MissingLevel,
        Diagnostics.NoParameterlessConstructor,
        Diagnostics.DuplicateKey,
        Diagnostics.InvalidStateType);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var attributeType = start.Compilation.GetTypeByMetadataName(JevQuestionsAttribute);
            if (attributeType is not null)
            {
                start.RegisterSymbolAction(symbol => Analyze(symbol, attributeType), SymbolKind.NamedType);
            }
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol attributeType)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                continue;
            }

            foreach (var diagnostic in ModelBuilder.Build(type, attribute, context.CancellationToken).Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            return;
        }
    }
}
