using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minos.Generator;

/// <summary>Generates the question JSON and typed answer parser for every <c>[Questions]</c> type.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class QuestionSetGenerator : IIncrementalGenerator
{
    internal const string TrackingName = "QuestionSets";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var questionSets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Minos.QuestionsAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                {
                    var result = ModelBuilder.Build(
                        (INamedTypeSymbol)attributeContext.TargetSymbol,
                        attributeContext.Attributes[0],
                        cancellationToken);
                    return new GeneratorInput(result.Model, result.InvalidSet);
                })
            .WithTrackingName(TrackingName);

        // The generator reports nothing: Minos.NET.Analyzers reports the diagnostics from the same model builder.
        // The pipeline carries only value-equatable models, never the diagnostics, so it stays cacheable. A valid set
        // gets its full implementation. An invalid one gets only throwing stubs for its unimplemented partial question
        // properties, so the compiler reports no CS9248 for them and the analyzer's JEV errors are what a build shows.
        context.RegisterSourceOutput(questionSets, static (output, input) =>
        {
            if (input.Model is { } model)
            {
                output.AddSource(SourceEmitter.HintName(model), SourceEmitter.Emit(model));
            }
            else if (input.InvalidSet is { } invalidSet)
            {
                output.AddSource(SourceEmitter.HintName(invalidSet), SourceEmitter.EmitStubs(invalidSet));
            }
        });
    }

    /// <summary>What the pipeline carries for one set: its model when valid, else its stubs, if it has any.</summary>
    internal sealed record GeneratorInput(QuestionSetModel? Model, InvalidSetModel? InvalidSet);
}
