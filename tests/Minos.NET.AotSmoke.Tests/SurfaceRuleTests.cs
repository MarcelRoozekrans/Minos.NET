using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Minos.AotSmoke.Tests;

/// <summary>
/// The mechanical rules the six packages' public surface keeps for 1.0, checked over the package assemblies the AOT smoke
/// app references. Each test names every member that breaks its rule.
/// </summary>
public sealed class SurfaceRuleTests
{
    private const string ServiceCollection = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string ServiceCollectionNamespace = "Microsoft.Extensions.DependencyInjection";

    // Each package's assembly and the namespace its extension classes live in.
    private static readonly (string Assembly, string RootNamespace)[] Packages =
    [
        ("Minos.NET", "Minos"),
        ("Minos.NET.SystemOne", "Minos.SystemOne"),
        ("Minos.NET.OpenAI", "Minos.OpenAI"),
        ("Minos.NET.DependencyInjection", "Minos"),
        ("Minos.NET.Caching", "Minos"),
        ("Minos.NET.RateLimiting", "Minos"),
    ];

    // Public classes designed for inheritance that are not abstract.
    private static readonly Dictionary<string, string> Unsealed = new(StringComparer.Ordinal)
    {
        ["Minos.DecisionClientBuilder"] = "DecisionClientServiceBuilder in Minos.NET.DependencyInjection derives from it.",
        ["Minos.DecisionClientServiceBuilder"] = "SystemOneClientServiceBuilder and OpenAIDecisionClientServiceBuilder derive from it.",
    };

    // Public types no docs page or sample names.
    private static readonly Dictionary<string, string> Undocumented = new(StringComparer.Ordinal);

    // The folders under docs/ that are not published pages.
    private static readonly string[] UnpublishedDocs = ["design", "planning", "plans", "superpowers"];

    // The samples that exist to reach the whole surface, so naming a type there documents nothing.
    private static readonly string[] SurfaceHosts = ["Minos.NET.AotSmoke", "Minos.NET.AotSurface"];

    [Fact]
    public void The_packages_have_public_types_to_check()
        // Guards the tests below against passing vacuously, for example if the assemblies stopped being found.
        => Assert.True(PublicTypes().Count() > 50);

    [Fact]
    public void Every_public_class_is_sealed_abstract_static_or_designed_for_inheritance()
    {
        var offenders = PublicTypes()
            .Where(type => type.TypeKind == TypeKind.Class && !type.IsSealed && !type.IsAbstract && !type.IsStatic)
            .Select(Name)
            .Where(name => !Unsealed.ContainsKey(name))
            .ToList();

        Assert.True(offenders.Count == 0, "These public classes are neither sealed nor abstract; seal them or add them to Unsealed with a reason:\n" + string.Join('\n', offenders));
        Assert.All(Unsealed.Keys, name => Assert.Contains(name, PublicTypes().Select(Name)));
    }

    [Fact]
    public void Every_public_method_returning_a_task_ends_in_Async()
    {
        var offenders = PublicTypes()
            .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
            .Where(method => method.MethodKind == MethodKind.Ordinary
                && method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
                && IsTask(method.ReturnType)
                && !method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(method => method.ToDisplayString())
            .ToList();

        Assert.True(offenders.Count == 0, "These public methods return a Task or ValueTask but do not end in Async:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_public_type_is_named_in_the_docs_or_a_sample()
    {
        var text = DocumentationText();
        var offenders = PublicTypes()
            .Where(type => !Undocumented.ContainsKey(Name(type)) && !Mentions(text, type.Name))
            .Select(Name)
            .ToList();

        Assert.True(offenders.Count == 0, "No published docs page or sample names these public types; document them, make them internal, or add them to Undocumented with a reason:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_public_extension_class_is_in_the_namespace_its_users_import()
    {
        var offenders = Packages
            .SelectMany(package => PublicTypes(package.Assembly)
                .Where(type => type.IsStatic && type.MightContainExtensionMethods)
                .Select(type => (Type: type, Expected: ExpectedNamespace(type, package.RootNamespace))))
            .Where(entry => !string.Equals(entry.Type.ContainingNamespace.ToDisplayString(), entry.Expected, StringComparison.Ordinal))
            .Select(entry => $"{Name(entry.Type)} is in {entry.Type.ContainingNamespace.ToDisplayString()}, expected {entry.Expected}")
            .ToList();

        Assert.True(offenders.Count == 0, "These extension classes are not where their users look for them:\n" + string.Join('\n', offenders));
    }

    private static string ExpectedNamespace(INamedTypeSymbol type, string rootNamespace)
        => type.GetMembers().OfType<IMethodSymbol>()
            .Any(method => method.IsExtensionMethod && string.Equals(method.Parameters[0].Type.ToDisplayString(), ServiceCollection, StringComparison.Ordinal))
            ? ServiceCollectionNamespace
            : rootNamespace;

    private static IEnumerable<INamedTypeSymbol> PublicTypes() => Packages.SelectMany(package => PublicTypes(package.Assembly));

    private static IEnumerable<INamedTypeSymbol> PublicTypes(string assemblyName)
    {
        var compilation = SampleCompilation.Smoke.Compilation;
        var assembly = compilation.References
            .Select(compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .First(candidate => string.Equals(candidate.Name, assemblyName, StringComparison.Ordinal));
        return Types(assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
        => ns.GetNamespaceMembers().SelectMany(Types).Concat(ns.GetTypeMembers().SelectMany(Types));

    private static IEnumerable<INamedTypeSymbol> Types(INamedTypeSymbol type)
        => type.DeclaredAccessibility != Accessibility.Public ? [] : type.GetTypeMembers().SelectMany(Types).Prepend(type);

    private static string Name(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None));

    private static bool IsTask(ITypeSymbol type)
        => string.Equals(type.ContainingNamespace?.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal)
            && type.Name is "Task" or "ValueTask";

    private static string DocumentationText()
    {
        var docs = Path.Combine(Repository.Root, "docs");
        var pages = Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Where(page => !UnpublishedDocs.Contains(Path.GetRelativePath(docs, page).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0], StringComparer.Ordinal));
        var samples = Path.Combine(Repository.Root, "samples");
        var sources = Directory.EnumerateFiles(samples, "*.cs", SearchOption.AllDirectories)
            .Where(source =>
            {
                var relative = Path.GetRelativePath(samples, source).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !SurfaceHosts.Contains(relative[0], StringComparer.Ordinal) && !relative.Contains("obj", StringComparer.Ordinal) && !relative.Contains("bin", StringComparer.Ordinal);
            });
        return string.Join('\n', pages.Concat(sources).Select(File.ReadAllText));
    }

    private static bool Mentions(string text, string typeName) => Regex.IsMatch(text, $@"\b{Regex.Escape(typeName)}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
}
