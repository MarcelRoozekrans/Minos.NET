using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.CodeFixes;

/// <summary>
/// Adds a <c>[Criteria("…")]</c> (JEV006) or <c>[Level("…")]</c> (JEV104) to an enum member that is missing one,
/// with the description derived from the member's own name by <see cref="WordSplitter"/>.
/// </summary>
/// <remarks>
/// Both diagnostics can instead land on the question property, when the enum is declared in a referenced assembly
/// rather than in source (<c>ZeroAlloc.Jev.Analyzers.QuestionSetAnalyzer</c>'s own doc comment explains why). There
/// is no enum member syntax to attach an attribute to in that case, so this provider offers no fix for it.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddDescriptionCodeFixProvider))]
[Shared]
public sealed class AddDescriptionCodeFixProvider : CodeFixProvider
{
    private const string JevNamespace = "ZeroAlloc.Jev";
    private const string EndOfLineKey = "end_of_line";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(DiagnosticIds.MissingCriteria, DiagnosticIds.MissingLevel);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan) is not EnumMemberDeclarationSyntax member)
            {
                continue;
            }

            var attributeName = diagnostic.Id == DiagnosticIds.MissingCriteria ? "Criteria" : "Level";
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add [{attributeName}(\"{WordSplitter.ToSentence(member.Identifier.ValueText)}\")]",
                    cancellationToken => AddAttributeAsync(context.Document, member, attributeName, cancellationToken),
                    equivalenceKey: diagnostic.Id),
                diagnostic);
        }
    }

    private static async Task<Document> AddAttributeAsync(
        Document document, EnumMemberDeclarationSyntax member, string attributeName, CancellationToken cancellationToken)
    {
        // The document has not changed since RegisterCodeFixesAsync found member in this same root, so
        // GetSyntaxRootAsync returns the identical cached tree and member's identity is still valid within it.
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false))!;
        var metadataName = $"{JevNamespace}.{attributeName}Attribute";

        // The attribute is written fully qualified and left to the simplifier to shorten to what binds at the member.
        // ImportAdder adds a using for it only when nothing in scope reaches it yet: in Roslyn 5.0.0 it overlooks a
        // using inside a namespace and a global using in another file, and adds a redundant top-level one.
        var name = SyntaxFactory.ParseName("global::" + metadataName).WithAdditionalAnnotations(Simplifier.Annotation);
        if (model.Compilation.GetTypeByMetadataName(metadataName) is not { } attributeType
            || !IsInScope(model, member.SpanStart, attributeType))
        {
            name = name.WithAdditionalAnnotations(Simplifier.AddImportsAnnotation);
        }

        // Only the attribute inside the new brackets is annotated for formatting: Insert lays the brackets out like
        // the rest of the member, and the formatter would otherwise redo the whitespace around them.
        var description = WordSplitter.ToSentence(member.Identifier.ValueText);
        var attribute = SyntaxFactory.Attribute(
                name,
                SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(description))))))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        var newDocument = document.WithSyntaxRoot(root.ReplaceNode(member, Insert(member, attributeList)));
        var newLine = await NewLineAsync(document, cancellationToken).ConfigureAwait(false);
        var options = (await document.GetOptionsAsync(cancellationToken).ConfigureAwait(false))
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, newLine);

        newDocument = await ImportAdder.AddImportsAsync(newDocument, Simplifier.AddImportsAnnotation, options, cancellationToken)
            .ConfigureAwait(false);
        newDocument = await Simplifier.ReduceAsync(newDocument, Simplifier.Annotation, options, cancellationToken)
            .ConfigureAwait(false);
        newDocument = await Formatter.FormatAsync(newDocument, Formatter.Annotation, options, cancellationToken)
            .ConfigureAwait(false);
        return await WithNewLineAsync(document, newDocument, newLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <paramref name="changed"/> as plain text, every line break the fix wrote in <paramref name="newLine"/>. ImportAdder
    /// ends a using it adds with a line break of its own choosing, which the formatter keeps whatever the new line
    /// option says, so a using added to a one-line file under <c>end_of_line = lf</c> would still end in <c>"\r\n"</c>.
    /// </summary>
    /// <remarks>
    /// Only the text the fix changed is rewritten, so the user's own lines keep their endings. The result carries no
    /// annotations either: the fix is fully cleaned up here, and the code action's own cleanup, which formats with the
    /// workspace's options rather than the document's line ending, finds nothing left to redo.
    /// </remarks>
    private static async Task<Document> WithNewLineAsync(
        Document original, Document changed, string newLine, CancellationToken cancellationToken)
    {
        var text = await original.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var changes = await changed.GetTextChangesAsync(original, cancellationToken).ConfigureAwait(false);
        return original.WithText(text.WithChanges(changes.Select(change => new TextChange(
            change.Span,
            change.NewText!.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newLine)))));
    }

    /// <summary>
    /// Whether <paramref name="attributeType"/> is reachable at <paramref name="position"/> without a new using: by
    /// its simple name through any using in scope, or through an alias of it or of its namespace.
    /// </summary>
    private static bool IsInScope(SemanticModel model, int position, INamedTypeSymbol attributeType)
        => model.LookupNamespacesAndTypes(position, name: attributeType.Name)
                .Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, attributeType))
            || model.LookupNamespacesAndTypes(position)
                .OfType<IAliasSymbol>()
                .Any(alias => SymbolEqualityComparer.Default.Equals(alias.Target, attributeType)
                    || SymbolEqualityComparer.Default.Equals(alias.Target, attributeType.ContainingNamespace));

    /// <summary>
    /// Adds <paramref name="attributeList"/> as the member's last attribute list, laid out like the member's own
    /// declaration, so the rest of the member keeps its layout.
    /// </summary>
    private static EnumMemberDeclarationSyntax Insert(EnumMemberDeclarationSyntax member, AttributeListSyntax attributeList)
    {
        if (member.AttributeLists.Count == 0)
        {
            // The member's leading trivia, its indentation and any comment, moves in front of the new list, which
            // sits on the name's line.
            return member
                .WithoutLeadingTrivia()
                .AddAttributeLists(attributeList
                    .WithLeadingTrivia(member.GetLeadingTrivia())
                    .WithTrailingTrivia(SyntaxFactory.Space));
        }

        // After the last list, separated from it as that list is from the name, and indented as the name is: on the
        // same line when the member is written on one line, on a line of its own when each list has one.
        var lastList = member.AttributeLists[member.AttributeLists.Count - 1];
        return member.AddAttributeLists(attributeList
            .WithLeadingTrivia(member.Identifier.LeadingTrivia)
            .WithTrailingTrivia(lastList.GetTrailingTrivia()));
    }

    /// <summary>
    /// The new line the document uses: the <c>.editorconfig</c> <c>end_of_line</c>, else the document's own first line
    /// break, else <see cref="FormattingOptions.NewLine"/> as the workspace has it. That option's default is
    /// <c>"\r\n"</c> whatever the platform, which alone would mix line endings into an LF file.
    /// </summary>
    private static async Task<string> NewLineAsync(Document document, CancellationToken cancellationToken)
    {
        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (((tree is null ? null : EditorConfigNewLine(document, tree)) ?? DetectNewLine(text)) is { } newLine)
        {
            return newLine;
        }

        var options = await document.GetOptionsAsync(cancellationToken).ConfigureAwait(false);
        return options.GetOption(FormattingOptions.NewLine);
    }

    /// <summary>The new line <c>.editorconfig</c> sets for <paramref name="tree"/>, or <see langword="null"/>.</summary>
    private static string? EditorConfigNewLine(Document document, SyntaxTree tree)
    {
        if (!document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(EndOfLineKey, out var value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "LF" => "\n",
            "CRLF" => "\r\n",
            "CR" => "\r",
            _ => null,
        };
    }

    /// <summary>The line break text of the document's first line that has one, or <see langword="null"/> for a
    /// document with no line breaks at all (a single line, or empty).</summary>
    private static string? DetectNewLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            if (line.EndIncludingLineBreak > line.End)
            {
                return text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));
            }
        }

        return null;
    }
}
