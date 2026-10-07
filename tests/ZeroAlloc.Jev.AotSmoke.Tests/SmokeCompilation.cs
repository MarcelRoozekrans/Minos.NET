using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Jev.AotSmoke.Tests;

/// <summary>
/// The AOT smoke app's compilation, rebuilt from the inputs its <c>JevWriteCompileInputs</c> target writes: the same
/// sources, references and defines, with the same source generators run over them, so the generated question sets
/// and JSON context bind exactly as they do in the real build.
/// </summary>
internal sealed class SmokeCompilation
{
    private SmokeCompilation(Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics, int generatorCount)
    {
        Compilation = compilation;
        Errors = [.. compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        GeneratorErrors = [.. generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        GeneratorCount = generatorCount;
    }

    /// <summary>The smoke app's compilation, built once for every test.</summary>
    public static SmokeCompilation Instance { get; } = Create();

    /// <summary>The compilation, generator output included.</summary>
    public Compilation Compilation { get; }

    /// <summary>The compilation's errors, generator output included.</summary>
    public ImmutableArray<Diagnostic> Errors { get; }

    /// <summary>The generators' own errors, such as a generator that threw.</summary>
    public ImmutableArray<Diagnostic> GeneratorErrors { get; }

    /// <summary>How many source generators ran.</summary>
    public int GeneratorCount { get; }

    private static SmokeCompilation Create()
    {
        var inputsPath = typeof(SmokeCompilation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(attribute => string.Equals(attribute.Key, "AotSmokeCompileInputs", StringComparison.Ordinal))
            .Value!;
        var inputs = File.ReadAllLines(inputsPath)
            .Select(line => line.Split('|', 2))
            .ToLookup(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: inputs["define"].SelectMany(defines => defines.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        var trees = inputs["compile"]
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
            .ToList();
        var references = inputs["reference"].Select(path => MetadataReference.CreateFromFile(path)).ToList();
        var compilation = CSharpCompilation.Create(
            "ZeroAlloc.Jev.AotSmoke",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

        var loader = new AnalyzerLoader();
        var generators = inputs["analyzer"]
            .Select(path => new AnalyzerFileReference(path, loader))
            .SelectMany(reference => reference.GetGenerators(LanguageNames.CSharp))
            .ToList();
        CSharpGeneratorDriver
            .Create(generators, parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        return new SmokeCompilation(output, generatorDiagnostics, generators.Count);
    }

    // Loads each analyzer assembly once, beside its own dependencies; Microsoft.CodeAnalysis itself resolves to the
    // copy this test already runs on.
    private sealed class AnalyzerLoader() : AssemblyLoadContext("AotSmokeAnalyzers"), IAnalyzerAssemblyLoader
    {
        private readonly Dictionary<string, string> dependencies = new(StringComparer.OrdinalIgnoreCase);

        public void AddDependencyLocation(string fullPath)
            => dependencies[Path.GetFileNameWithoutExtension(fullPath)] = fullPath;

        public Assembly LoadFromPath(string fullPath)
        {
            AddDependencyLocation(fullPath);
            var name = AssemblyName.GetAssemblyName(fullPath);
            return Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                ?? LoadFromAssemblyPath(fullPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
            => assemblyName.Name is { } name
                && !name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                && dependencies.TryGetValue(name, out var path)
                    ? LoadFromAssemblyPath(path)
                    : null;
    }
}
