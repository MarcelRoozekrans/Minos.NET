using System.Collections.Concurrent;
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

    private static readonly ImmutableArray<DiagnosticDescriptor> Descriptors = ImmutableArray.Create(
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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var attributeType = start.Compilation.GetTypeByMetadataName(JevQuestionsAttribute);
            if (attributeType is not null)
            {
                // An enum shared by two question-set types (or by two properties in the same type) would
                // otherwise be walked, and its missing-[Level] member reported, once per property that uses it.
                var reportedEnumMembers = new ConcurrentDictionary<Location, byte>();
                start.RegisterSymbolAction(symbol => Analyze(symbol, attributeType, reportedEnumMembers), SymbolKind.NamedType);
            }
        });
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol attributeType,
        ConcurrentDictionary<Location, byte> reportedEnumMembers)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                continue;
            }

            foreach (var info in ModelBuilder.Build(type, attribute, context.CancellationToken).Diagnostics)
            {
                if (info.Id == DiagnosticIds.MissingLevel
                    && info.Location is { } location
                    && !reportedEnumMembers.TryAdd(location, 0))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DescriptorsById[info.Id], info.Location, info.Arguments.Cast<object>().ToArray()));
            }

            return;
        }
    }
}
