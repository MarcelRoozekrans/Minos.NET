using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Jev.AotSmoke.Tests;

/// <summary>
/// A sample's compilation, rebuilt from the inputs its <c>JevWriteCompileInputs</c> target writes: the same sources,
/// references, defines, language version, nullable context, parser features and warning options, with the same source
/// generators run over them under the same editorconfig and MSBuild options. Tests check the result against the real
/// build: no errors or warnings, as the real build's TreatWarningsAsErrors guarantees, and the same generated files the
/// real build wrote.
/// </summary>
internal sealed class SampleCompilation
{
    private SampleCompilation(
        Compilation compilation,
        ImmutableArray<Diagnostic> generatorDiagnostics,
        int generatorCount,
        List<string> generatedFiles,
        string buildGeneratedFilesDirectory)
    {
        Compilation = compilation;
        Diagnostics = [.. compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)];
        GeneratorErrors = [.. generatorDiagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)];
        GeneratorCount = generatorCount;
        GeneratedFiles = generatedFiles;
        BuildGeneratedFiles = Directory.Exists(buildGeneratedFilesDirectory)
            ? [.. Directory.GetFiles(buildGeneratedFilesDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The AOT smoke app's compilation, built once for every test.</summary>
    public static SampleCompilation Smoke { get; } = Create("AotSmokeCompileInputs", "ZeroAlloc.Jev.AotSmoke");

    /// <summary>The AOT surface host's compilation, built once for every test.</summary>
    public static SampleCompilation Surface { get; } = Create("AotSurfaceCompileInputs", "ZeroAlloc.Jev.AotSurface");

    /// <summary>The compilation, generator output included.</summary>
    public Compilation Compilation { get; }

    /// <summary>The compilation's warnings and errors, generator output included.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>The generators' own warnings and errors, such as a generator that threw.</summary>
    public ImmutableArray<Diagnostic> GeneratorErrors { get; }

    /// <summary>How many source generators ran.</summary>
    public int GeneratorCount { get; }

    /// <summary>The paths of the files the generators added here, laid out as the real build writes them.</summary>
    public List<string> GeneratedFiles { get; }

    /// <summary>The files the real build's generators wrote, through EmitCompilerGeneratedFiles.</summary>
    public List<string> BuildGeneratedFiles { get; }

    private static SampleCompilation Create(string inputsKey, string assemblyName)
    {
        var inputsPath = typeof(SampleCompilation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(attribute => string.Equals(attribute.Key, inputsKey, StringComparison.Ordinal))
            .Value!;
        var inputs = File.ReadAllLines(inputsPath)
            .Select(line => line.Split('|', 2))
            .ToLookup(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        if (!LanguageVersionFacts.TryParse(Single(inputs, "langversion"), out var languageVersion))
        {
            throw new InvalidOperationException("Unknown LangVersion " + Single(inputs, "langversion") + ".");
        }

        var parseOptions = new CSharpParseOptions(languageVersion, preprocessorSymbols: List(inputs, "define"))
            .WithFeatures(Features(inputs));
        var trees = inputs["compile"]
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
            .ToList();
        var references = inputs["reference"].Select(path => MetadataReference.CreateFromFile(path)).ToList();
        var configs = AnalyzerConfigSet.Create(
            inputs["editorconfig"].Select(path => AnalyzerConfig.Parse(SourceText.From(File.ReadAllText(path)), path)).ToList());
        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            nullableContextOptions: Enum.Parse<NullableContextOptions>(Single(inputs, "nullable"), ignoreCase: true),
            allowUnsafe: bool.Parse(Single(inputs, "allowunsafeblocks")),
            warningLevel: int.Parse(Single(inputs, "warninglevel"), CultureInfo.InvariantCulture),
            specificDiagnosticOptions: List(inputs, "nowarn")
                .Select(id => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? "CS" + number.ToString("D4", CultureInfo.InvariantCulture) : id)
                .Distinct(StringComparer.Ordinal)
                .Select(id => KeyValuePair.Create(id, ReportDiagnostic.Suppress)))
            .WithSyntaxTreeOptionsProvider(new ConfigTreeOptions(configs));
        var compilation = CSharpCompilation.Create(assemblyName, trees, references, options);

        var loader = new AnalyzerLoader(assemblyName + " analyzers");
        var generators = inputs["analyzer"]
            .Select(path => new AnalyzerFileReference(path, loader))
            .SelectMany(reference => reference.GetGenerators(LanguageNames.CSharp))
            .ToList();
        var generatedRoot = Single(inputs, "generatedfiles");
        CSharpGeneratorDriver
            .Create(
                generators,
                inputs["additionalfile"].Select(path => (AdditionalText)new FileText(path)),
                parseOptions,
                new ConfigOptionsProvider(configs),
                new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: false, baseDirectory: generatedRoot))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var generatedFiles = output.SyntaxTrees
            .Skip(trees.Count)
            .Select(tree => tree.FilePath)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new SampleCompilation(output, generatorDiagnostics, generators.Count, generatedFiles, generatedRoot);
    }

    private static string Single(ILookup<string, string> inputs, string key) => inputs[key].First();

    // A list property, written with its ';' separators turned into ','.
    private static string[] List(ILookup<string, string> inputs, string key)
        => [.. inputs[key].SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

    // The parser features csc gets: each Features entry as name=value, or name=true, and the interceptor namespaces
    // as one feature each, their namespaces joined by ';' as the Csc task passes them.
    private static Dictionary<string, string> Features(ILookup<string, string> inputs)
    {
        var features = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in List(inputs, "features"))
        {
            var parts = feature.Split('=', 2);
            features[parts[0]] = parts.Length == 2 ? parts[1] : "true";
        }

        AddNamespaces(features, "InterceptorsNamespaces", List(inputs, "interceptorsnamespaces"));
        AddNamespaces(features, "InterceptorsPreviewNamespaces", List(inputs, "interceptorspreviewnamespaces"));
        return features;
    }

    private static void AddNamespaces(Dictionary<string, string> features, string name, string[] namespaces)
    {
        if (namespaces.Length > 0)
        {
            features[name] = string.Join(';', namespaces);
        }
    }

    // The editorconfig and global config options, as the compiler hands them to generators.
    private sealed class ConfigOptionsProvider(AnalyzerConfigSet configs) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new DictionaryOptions(configs.GlobalConfigOptions.AnalyzerOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
            => new DictionaryOptions(configs.GetOptionsForSourcePath(tree.FilePath).AnalyzerOptions);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => new DictionaryOptions(configs.GetOptionsForSourcePath(textFile.Path).AnalyzerOptions);
    }

    private sealed class DictionaryOptions(ImmutableDictionary<string, string> options) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => options.TryGetValue(key, out value);
    }

    // The editorconfig and global config diagnostic severities and generated-code markers, as the compiler applies them.
    private sealed class ConfigTreeOptions(AnalyzerConfigSet configs) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
            => configs.GetOptionsForSourcePath(tree.FilePath).AnalyzerOptions.TryGetValue("generated_code", out var generated)
                ? bool.TryParse(generated, out var isGenerated) && isGenerated ? GeneratedKind.MarkedGenerated : GeneratedKind.NotGenerated
                : GeneratedKind.Unknown;

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
            => configs.GetOptionsForSourcePath(tree.FilePath).TreeOptions.TryGetValue(diagnosticId, out severity);

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
            => configs.GlobalConfigOptions.TreeOptions.TryGetValue(diagnosticId, out severity);
    }

    private sealed class FileText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(File.ReadAllText(Path));
    }

    // Loads each analyzer assembly once, beside its own dependencies; Microsoft.CodeAnalysis itself resolves to the
    // copy this test already runs on.
    private sealed class AnalyzerLoader(string contextName) : AssemblyLoadContext(contextName), IAnalyzerAssemblyLoader
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
