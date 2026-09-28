using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>Runs <see cref="QuestionSetAnalyzer"/> over a source with <c>{|JEV10x:…|}</c> markup at each expected diagnostic.</summary>
internal static class AnalyzerVerifier
{
    public static Task VerifyAsync(string source)
    {
        var test = new CSharpAnalyzerTest<QuestionSetAnalyzer, DefaultVerifier>
        {
            TestCode = "using ZeroAlloc.Jev;\n" + source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,

            // The generator, which implements the partial question properties, does not run here, so the compiler
            // reports every partial property as unimplemented. Only the analyzer's diagnostics are under test.
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(JevQuestionsAttribute).Assembly.Location));
        return test.RunAsync();
    }
}
