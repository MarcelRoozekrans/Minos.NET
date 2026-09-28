using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
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
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;

        var description = WordSplitter.ToSentence(member.Identifier.ValueText);
        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName(attributeName),
                SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(description))))))));

        var newMember = member
            .AddAttributeLists(attributeList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = EnsureUsing(root.ReplaceNode(member, newMember));
        var newDocument = document.WithSyntaxRoot(newRoot);

        // The formatter's own default new line is "\r\n" regardless of platform or of the document's existing
        // convention; matching the document's own line ending here keeps this fix from mixing endings into a
        // file that consistently uses "\n" (or vice versa).
        var options = await newDocument.GetOptionsAsync(cancellationToken).ConfigureAwait(false);
        if (DetectNewLine(await document.GetTextAsync(cancellationToken).ConfigureAwait(false)) is { } newLine)
        {
            options = options.WithChangedOption(FormattingOptions.NewLine, newLine);
        }

        return await Formatter.FormatAsync(newDocument, Formatter.Annotation, options, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Adds <c>using ZeroAlloc.Jev;</c> at the top of the file when it is missing, so the unqualified
    /// attribute name binds; the fix leaves an existing using directive (with any spelling of the namespace name)
    /// alone.</summary>
    private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root)
    {
        if (root.Usings.Any(existing => existing.Name?.ToString() == JevNamespace))
        {
            return root;
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(JevNamespace))
            .WithAdditionalAnnotations(Formatter.Annotation)
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        return root.WithUsings(root.Usings.Insert(0, usingDirective));
    }
}
