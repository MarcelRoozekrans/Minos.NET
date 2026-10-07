using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Jev.AotSmoke.Tests;

/// <summary>
/// The Native AOT smoke app must exercise every public entry point. Each check declares the entry points it calls
/// with <c>[Covers("…")]</c>, written as the PublicAPI line; these tests fail when an entry point is declared by no
/// check, when a declaration names no entry point, when a check does not really call what it declares, and when a
/// declaring check is not run by <c>Main</c>. <see cref="PublicApi"/> lists what is and is not an entry point, and why.
/// </summary>
public sealed class EntryPointCoverageTests
{
    private static readonly string[] Packages = ["ZeroAlloc.Jev", "ZeroAlloc.Jev.DependencyInjection"];

    [Fact]
    public void The_rebuilt_smoke_compilation_has_no_errors()
    {
        var smoke = SmokeCompilation.Instance;

        Assert.True(smoke.GeneratorCount > 0, "No source generator was loaded from the smoke app's analyzers.");
        Assert.Empty(smoke.GeneratorErrors.Select(diagnostic => diagnostic.ToString()));
        Assert.Empty(smoke.Errors.Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void The_signature_format_reproduces_every_entry_point()
    {
        var printed = Packages
            .Select(FindAssembly)
            .SelectMany(assembly => PublicMethods(assembly.GlobalNamespace))
            .Select(ApiSignature.Of)
            .ToHashSet(StringComparer.Ordinal);

        var unmatched = PublicApi.EntryPoints.Where(entryPoint => !printed.Contains(entryPoint)).ToList();

        Assert.True(
            unmatched.Count == 0,
            "These PublicAPI lines match no public method as ApiSignature prints it:\n" + string.Join('\n', unmatched));
    }

    [Fact]
    public void Every_entry_point_is_declared_by_a_smoke_check()
    {
        var declared = SmokeChecks.All.SelectMany(check => check.Declared).ToHashSet(StringComparer.Ordinal);

        var uncovered = PublicApi.EntryPoints.Where(entryPoint => !declared.Contains(entryPoint)).ToList();

        Assert.True(
            uncovered.Count == 0,
            uncovered.Count + " of " + PublicApi.EntryPoints.Count + " public entry points are declared by no smoke check:\n"
                + string.Join('\n', uncovered.Select(entryPoint => entryPoint + ReachedBy(entryPoint))));
    }

    [Fact]
    public void Every_declaration_names_a_public_entry_point()
    {
        var entryPoints = PublicApi.EntryPoints.ToHashSet(StringComparer.Ordinal);

        var unknown = SmokeChecks.All
            .SelectMany(check => check.Declared.Where(entryPoint => !entryPoints.Contains(entryPoint)).Select(entryPoint => check.Name + ": " + entryPoint))
            .ToList();

        Assert.True(unknown.Count == 0, "These [Covers] declarations name no public entry point:\n" + string.Join('\n', unknown));
    }

    [Fact]
    public void Every_check_calls_the_entry_points_it_declares()
    {
        var dishonest = SmokeChecks.All
            .SelectMany(check => check.Declared.Where(entryPoint => !check.Called.Contains(entryPoint)).Select(entryPoint => check.Name + ": " + entryPoint))
            .ToList();

        Assert.True(dishonest.Count == 0, "These checks declare an entry point they never call:\n" + string.Join('\n', dishonest));
    }

    [Fact]
    public void Every_declaring_check_is_run_by_Main()
    {
        var notRun = SmokeChecks.All
            .Where(check => !SmokeChecks.RunByMain.Contains(check.Method.OriginalDefinition))
            .Select(check => check.Name)
            .ToList();

        Assert.True(notRun.Count == 0, "These checks declare entry points but Main never runs them:\n" + string.Join('\n', notRun));
    }

    private static string ReachedBy(string entryPoint)
    {
        var callers = SmokeChecks.CalledByMainsCallees
            .Where(pair => pair.Value.Contains(entryPoint))
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        return callers.Count == 0 ? "  (called by nothing Main runs)" : "  (called by " + string.Join(", ", callers) + ")";
    }

    private static IAssemblySymbol FindAssembly(string name)
        => SmokeCompilation.Instance.Compilation.References
            .Select(SmokeCompilation.Instance.Compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .First(assembly => string.Equals(assembly.Name, name, StringComparison.Ordinal));

    private static IEnumerable<IMethodSymbol> PublicMethods(INamespaceSymbol ns)
        => ns.GetNamespaceMembers().SelectMany(PublicMethods)
            .Concat(ns.GetTypeMembers().SelectMany(PublicMethods));

    private static IEnumerable<IMethodSymbol> PublicMethods(INamedTypeSymbol type)
        => type.DeclaredAccessibility != Accessibility.Public
            ? []
            : type.GetMembers().OfType<IMethodSymbol>()
                .Where(method => method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
                .Concat(type.GetTypeMembers().SelectMany(PublicMethods));
}
