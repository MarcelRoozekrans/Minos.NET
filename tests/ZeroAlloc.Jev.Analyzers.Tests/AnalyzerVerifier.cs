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

        // The generator skips an invalid set, so its partial question properties stay unimplemented and the compiler
        // reports each of them. Only the analyzer's diagnostics are under test here.
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        return test.RunAsync();
    }

    /// <summary>
    /// Verifies a set the analyzer accepts. The generator runs too and implements the partial properties, so the
    /// default compiler-error check stays on: the set reports nothing and the generated code compiles.
    /// </summary>
    public static Task VerifyNoDiagnosticsAsync(string source) => Create<GeneratingTest>(source).RunAsync();

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
