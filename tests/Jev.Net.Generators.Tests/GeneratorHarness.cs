using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Jev.Net.Generators.Tests;

internal static class GeneratorHarness
{
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly MetadataReference[] References = LoadReferences();

    public static CSharpCompilation Compile(string source)
        => CSharpCompilation.Create(
            "Demo",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            [new QuestionSetGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public static GeneratorDriver Run(string source, out Compilation output, out ImmutableArray<Diagnostic> diagnostics)
        => CreateDriver().RunGeneratorsAndUpdateCompilation(Compile(source), out output, out diagnostics);

    // The test host's trusted platform assemblies are the .NET 10 runtime plus this project's dependencies,
    // Jev.Net among them; adding Jev.Net explicitly guards against a host that trims the list.
    private static MetadataReference[] LoadReferences()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Noul).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. paths.Select(path => MetadataReference.CreateFromFile(path))];
    }
}
