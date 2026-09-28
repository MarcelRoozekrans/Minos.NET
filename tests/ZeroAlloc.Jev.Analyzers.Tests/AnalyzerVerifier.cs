using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>Runs <see cref="QuestionSetAnalyzer"/> over a source with <c>{|JEV10x:…|}</c> markup at each expected diagnostic.</summary>
internal static class AnalyzerVerifier
{
    /// <summary>Verifies a set the analyzer rejects: exactly the marked-up diagnostics, and nothing else.</summary>
    /// <param name="source">The source under test.</param>
    /// <param name="additionalReferences">
    /// Extra metadata references the source needs, such as a compiled-in-test assembly for an enum declared
    /// outside the source under test.
    /// </param>
    public static Task VerifyAsync(string source, params MetadataReference[] additionalReferences)
    {
        var test = Create<CSharpAnalyzerTest<QuestionSetAnalyzer, DefaultVerifier>>(source);
        foreach (var reference in additionalReferences)
        {
            test.TestState.AdditionalReferences.Add(reference);
        }

        // The generator does not run here, so the partial question properties stay unimplemented and the compiler
        // reports each of them. Only the analyzer's diagnostics are under test; VerifyWithGeneratorAsync checks the
        // analyzer and the generator's stubs together.
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        return test.RunAsync();
    }

    /// <summary>
    /// Verifies a set the analyzer accepts. The generator runs too and implements the partial properties, so the
    /// default compiler-error check stays on: the set reports nothing and the generated code compiles.
    /// </summary>
    public static Task VerifyNoDiagnosticsAsync(string source) => Create<GeneratingTest>(source).RunAsync();

    /// <summary>
    /// Verifies a set the analyzer rejects, as a build sees it: the generator runs too and the compiler's errors are
    /// checked, so the marked-up JEV diagnostics must be the only errors. The generator's throwing stubs implement the
    /// partial question properties, so no CS9248 may appear.
    /// </summary>
    public static Task VerifyWithGeneratorAsync(string source)
    {
        var test = Create<GeneratingTest>(source);
        test.CompilerDiagnostics = CompilerDiagnostics.Errors;
        return test.RunAsync();
    }

    private static TTest Create<TTest>(string source)
        where TTest : CSharpAnalyzerTest<QuestionSetAnalyzer, DefaultVerifier>, new()
    {
        var test = new TTest
        {
            TestCode = "using ZeroAlloc.Jev;\n" + source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
        };
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(JevQuestionsAttribute).Assembly.Location));
        return test;
    }

    private sealed class GeneratingTest : CSharpAnalyzerTest<QuestionSetAnalyzer, DefaultVerifier>
    {
        public GeneratingTest()
        {
            // The generated sources are the generator tests' concern, pinned by their snapshots.
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck;
        }

        protected override IEnumerable<Type> GetSourceGenerators()
        {
            yield return typeof(QuestionSetGenerator);
        }
    }
}
