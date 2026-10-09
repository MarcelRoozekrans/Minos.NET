using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minos.AotSmoke.Tests;

/// <summary>
/// The AOT surface host must instantiate every public generic type and generic method of both packages. Rooting an
/// assembly makes ILC analyse a generic member only through an instantiation it can share across reference types, and a
/// value-type generic, such as <c>Choice&lt;T&gt;</c> with its <c>where T : struct, Enum</c>, has none, so the
/// <c>aot-surface</c> job analyses such a member only through the host's instantiation of it. A generic type counts when
/// the host names a closed instantiation of it in a <c>[DynamicDependency]</c> for all members, and a generic method
/// counts when the host's code binds to a closed instantiation of it.
/// </summary>
public sealed class SurfaceGenericsTests
{
    private const string DynamicDependency = "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute";

    // DynamicallyAccessedMemberTypes.All, which is ~None.
    private const int AllMembers = -1;

    private static readonly string[] Packages = ["Minos.NET", "Minos.NET.DependencyInjection"];

    [Fact]
    public void The_rebuilt_surface_compilation_has_no_warnings_or_errors_like_the_real_build()
    {
        var surface = SampleCompilation.Surface;

        Assert.True(surface.GeneratorCount > 0, "No source generator was loaded from the surface host's analyzers.");
        Assert.Empty(surface.GeneratorErrors.Select(diagnostic => diagnostic.ToString()));
        Assert.Empty(surface.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void The_rebuilt_surface_compilation_generates_the_files_the_real_build_did()
    {
        var surface = SampleCompilation.Surface;

        Assert.NotEmpty(surface.BuildGeneratedFiles);
        Assert.Equal(surface.BuildGeneratedFiles, surface.GeneratedFiles);
    }

    [Fact]
    public void The_packages_have_public_generics_to_instantiate()
    {
        // Guards the two tests below against passing vacuously, for example if the packages stopped being found.
        Assert.NotEmpty(PublicGenericTypes(SampleCompilation.Surface.Compilation));
        Assert.NotEmpty(PublicGenericMethods(SampleCompilation.Surface.Compilation));
    }

    [Fact]
    public void Every_public_generic_type_is_instantiated_in_the_surface_host()
    {
        var compilation = SampleCompilation.Surface.Compilation;

        var missing = Missing(PublicGenericTypes(compilation), InstantiatedTypes(compilation));

        Assert.True(
            missing.Count == 0,
            "The AOT surface host names no closed instantiation of these public generic types in a "
                + "[DynamicDependency(DynamicallyAccessedMemberTypes.All, …)], so aot-surface does not analyse them:\n"
                + string.Join('\n', missing));
    }

    [Fact]
    public void Every_public_generic_method_is_called_in_the_surface_host()
    {
        var compilation = SampleCompilation.Surface.Compilation;

        var missing = Missing(PublicGenericMethods(compilation), CalledGenericMethods(compilation));

        Assert.True(
            missing.Count == 0,
            "The AOT surface host calls no closed instantiation of these public generic methods, so aot-surface does not "
                + "analyse them:\n" + string.Join('\n', missing));
    }

    /// <summary>The public generic types of both packages: generic types, and types nested in one.</summary>
    internal static List<INamedTypeSymbol> PublicGenericTypes(Compilation compilation)
        => [.. Packages.SelectMany(name => PublicTypes(FindAssembly(compilation, name).GlobalNamespace)).Where(type => type.IsGenericType)];

    /// <summary>The public and protected generic methods of both packages' public types.</summary>
    internal static List<IMethodSymbol> PublicGenericMethods(Compilation compilation)
        => [.. Packages
            .SelectMany(name => PublicTypes(FindAssembly(compilation, name).GlobalNamespace))
            .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
            .Where(method => method.IsGenericMethod
                && method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)];

    /// <summary>The type definitions the host's <c>[DynamicDependency]</c> attributes instantiate, all members kept.</summary>
    internal static HashSet<ISymbol> InstantiatedTypes(Compilation compilation)
        => SourceSymbols(compilation.Assembly.GlobalNamespace)
            .SelectMany(symbol => symbol.GetAttributes())
            .Where(attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), DynamicDependency, StringComparison.Ordinal)
                && attribute.ConstructorArguments is [{ Value: AllMembers }, { Value: INamedTypeSymbol }])
            .Select(attribute => (INamedTypeSymbol)attribute.ConstructorArguments[1].Value!)
            .Where(IsClosed)
            .Select(type => (ISymbol)type.OriginalDefinition)
            .ToHashSet(SymbolEqualityComparer.Default);

    /// <summary>The generic method definitions the host's code, generated code included, binds to a closed
    /// instantiation of: a call or a method group.</summary>
    internal static HashSet<ISymbol> CalledGenericMethods(Compilation compilation)
    {
        var called = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>())
            {
                if (model.GetSymbolInfo(node).Symbol is IMethodSymbol { IsGenericMethod: true } method
                    && method.TypeArguments.All(IsClosed)
                    && IsClosed(method.ContainingType))
                {
                    called.Add((method.ReducedFrom ?? method).OriginalDefinition);
                }
            }
        }

        return called;
    }

    private static List<string> Missing<TSymbol>(List<TSymbol> required, HashSet<ISymbol> instantiated)
        where TSymbol : ISymbol
        => [.. required
            .Where(symbol => !instantiated.Contains(symbol.OriginalDefinition))
            .Select(symbol => symbol.ToDisplayString())
            .Order(StringComparer.Ordinal)];

    // A type with no type parameter anywhere in it: in its arguments, its elements or its containing types.
    private static bool IsClosed(ITypeSymbol type)
        => type switch
        {
            ITypeParameterSymbol => false,
            IArrayTypeSymbol array => IsClosed(array.ElementType),
            IPointerTypeSymbol pointer => IsClosed(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments.All(IsClosed) && (named.ContainingType is null || IsClosed(named.ContainingType)),
            _ => true,
        };

    private static IAssemblySymbol FindAssembly(Compilation compilation, string name)
        => compilation.References
            .Select(compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .First(assembly => string.Equals(assembly.Name, name, StringComparison.Ordinal));

    private static IEnumerable<INamedTypeSymbol> PublicTypes(INamespaceSymbol ns)
        => ns.GetNamespaceMembers().SelectMany(PublicTypes)
            .Concat(ns.GetTypeMembers().SelectMany(PublicTypes));

    private static IEnumerable<INamedTypeSymbol> PublicTypes(INamedTypeSymbol type)
        => type.DeclaredAccessibility != Accessibility.Public
            ? []
            : type.GetTypeMembers().SelectMany(PublicTypes).Prepend(type);

    // Every type and member the host declares, generated ones included.
    private static IEnumerable<ISymbol> SourceSymbols(INamespaceSymbol ns)
        => ns.GetNamespaceMembers().SelectMany(SourceSymbols)
            .Concat(ns.GetTypeMembers().SelectMany(SourceSymbols));

    private static IEnumerable<ISymbol> SourceSymbols(INamedTypeSymbol type)
        => type.GetMembers().Concat(type.GetTypeMembers().SelectMany(SourceSymbols)).Prepend(type);
}
