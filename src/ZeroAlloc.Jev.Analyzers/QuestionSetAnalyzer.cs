using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Analyzers;

/// <summary>
/// Reports the <c>[JevQuestions]</c> diagnostics. It runs the same model builder as the generator, which reports
/// nothing and emits only throwing stubs for an invalid set, so the two cannot disagree about what is valid.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QuestionSetAnalyzer : DiagnosticAnalyzer
{
    private const string JevQuestionsAttribute = "ZeroAlloc.Jev.JevQuestionsAttribute";

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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var attributeType = start.Compilation.GetTypeByMetadataName(JevQuestionsAttribute);
            if (attributeType is not null)
            {
                // An enum is walked once per property that uses it, across every question-set type, so a finding on
                // the enum or one of its members (JEV001–003, JEV005, JEV006, JEV104) would repeat once per property.
                // Each distinct finding (same rule, location and message arguments) is reported once per compilation.
                // The arguments keep apart the members of an enum from another assembly, which all land on the
                // property; the location keeps apart findings on two properties.
                var reported = new ConcurrentDictionary<DiagnosticInfo, byte>();
                start.RegisterSymbolAction(symbol => Analyze(symbol, attributeType, reported), SymbolKind.NamedType);
            }
        });
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol attributeType,
        ConcurrentDictionary<DiagnosticInfo, byte> reported)
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
                if (!reported.TryAdd(info, 0))
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
