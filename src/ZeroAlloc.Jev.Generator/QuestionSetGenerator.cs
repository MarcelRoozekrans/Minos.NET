using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Jev.Generator;

/// <summary>Generates the question JSON and typed answer parser for every <c>[JevQuestions]</c> type.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class QuestionSetGenerator : IIncrementalGenerator
{
    internal const string TrackingName = "QuestionSets";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var questionSets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroAlloc.Jev.JevQuestionsAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, cancellationToken)
                    => ModelBuilder.Build((INamedTypeSymbol)attributeContext.TargetSymbol, cancellationToken))
            .WithTrackingName(TrackingName);

        context.RegisterSourceOutput(questionSets, static (output, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                output.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (result.Model is { } model)
            {
                output.AddSource(SourceEmitter.HintName(model), SourceEmitter.Emit(model));
            }
        });
    }
}
