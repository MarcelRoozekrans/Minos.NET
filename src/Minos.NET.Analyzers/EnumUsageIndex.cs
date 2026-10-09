using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Minos.Generator;

namespace Minos.Analyzers;

/// <summary>How the <c>[Questions]</c> sets of a compilation use an enum.</summary>
[Flags]
internal enum EnumUsage
{
    None = 0,
    Choice = 1,
    Score = 2,
}

/// <summary>
/// Which enums of a compilation the <c>[Questions]</c> sets use, and how: the enum's own analysis needs it to
/// know which rules apply. Built once per compilation, on the first enum that asks, and safe to read from
/// concurrent symbol actions.
/// </summary>
internal sealed class EnumUsageIndex
{
    private readonly Compilation compilation;
    private readonly INamedTypeSymbol attributeType;
    private Dictionary<INamedTypeSymbol, EnumUsage>? usages;

    public EnumUsageIndex(Compilation compilation, INamedTypeSymbol attributeType)
    {
        this.compilation = compilation;
        this.attributeType = attributeType;
    }

    /// <summary>How the sets use <paramref name="enumType"/>; <see cref="EnumUsage.None"/> when none does.</summary>
    public EnumUsage For(INamedTypeSymbol enumType, CancellationToken cancellationToken)
    {
        // Built outside any lock, and published once: a concurrent caller may build a copy too, which is discarded.
        // A Lazy would cache the OperationCanceledException of whichever caller built it first and was cancelled.
        var index = Volatile.Read(ref usages);
        if (index is null)
        {
            Interlocked.CompareExchange(ref usages, Build(cancellationToken), null);
            index = usages!;
        }

        return index.TryGetValue(enumType.OriginalDefinition, out var usage) ? usage : EnumUsage.None;
    }

    private Dictionary<INamedTypeSymbol, EnumUsage> Build(CancellationToken cancellationToken)
    {
        var index = new Dictionary<INamedTypeSymbol, EnumUsage>(SymbolEqualityComparer.Default);
        var sets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            SemanticModel? model = null;
            foreach (var declaration in CandidateDeclarations(tree.GetRoot(cancellationToken)))
            {
                model ??= compilation.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type
                    && sets.Add(type)
                    && type.GetAttributes().Any(attribute =>
                        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType)))
                {
                    foreach (var (enumType, kind) in ModelBuilder.EnumUsages(type, cancellationToken))
                    {
                        index.TryGetValue(enumType.OriginalDefinition, out var usage);
                        index[enumType.OriginalDefinition] = usage | (kind == QuestionKind.Choice ? EnumUsage.Choice : EnumUsage.Score);
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// The class and record declarations that carry any attribute and are not nested in another type. The attribute
    /// is not matched by name, which an alias would defeat; binding it is left to the few declarations that remain.
    /// A nested type is skipped, as is everything inside a type: a nested set is unsupported, MIN101, and
    /// <see cref="ModelBuilder.EnumUsages"/> finds no questions in it.
    /// </summary>
    private static IEnumerable<TypeDeclarationSyntax> CandidateDeclarations(SyntaxNode root)
        => root
            .DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<TypeDeclarationSyntax>()
            .Where(declaration => declaration is ClassDeclarationSyntax or RecordDeclarationSyntax
                && declaration.AttributeLists.Count > 0);
}
