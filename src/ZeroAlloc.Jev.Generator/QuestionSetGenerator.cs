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
                    => ModelBuilder.Build(
                        (INamedTypeSymbol)attributeContext.TargetSymbol,
                        attributeContext.Attributes[0],
                        cancellationToken).Model)
            .WithTrackingName(TrackingName);

        // The generator reports nothing: ZeroAlloc.Jev.Analyzers reports the diagnostics from the same model builder.
        // The pipeline carries only the value-equatable model, null for an invalid set, which is skipped here. The
        // null is kept rather than filtered out so an invalid set still has a tracked, cacheable pipeline entry.
        context.RegisterSourceOutput(questionSets, static (output, model) =>
        {
            if (model is not null)
            {
                output.AddSource(SourceEmitter.HintName(model), SourceEmitter.Emit(model));
            }
        });
    }
}
