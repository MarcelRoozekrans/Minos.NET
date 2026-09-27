using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Jev.Generator.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void NoulOnly_Generates() => AssertGenerates(Sources.NoulOnly);

    [Fact]
    public void ChoiceOnly_Generates() => AssertGenerates(Sources.ChoiceOnly);

    [Fact]
    public void ScoreOnly_Generates() => AssertGenerates(Sources.ScoreOnly);

    [Fact]
    public void Mixed_Generates() => AssertGenerates(Sources.Mixed);

    [Fact]
    public void ChoiceOverEmptyEnum_CompilesWithoutError() => AssertCompiles(Sources.ChoiceOverEmptyEnum);

    [Fact]
    public void ScoreOverEmptyEnum_CompilesWithoutError() => AssertCompiles(Sources.ScoreOverEmptyEnum);

    [Fact]
    public void KeywordNamespaceAndType_Generates() => AssertGenerates(Sources.KeywordNamespaceAndType);

    [Fact]
    public void UnrelatedEdit_KeepsQuestionSetsCached()
    {
        var compilation = GeneratorHarness.Compile(Sources.Mixed);
        var driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("internal static class Unrelated { }", GeneratorHarness.ParseOptions));
        driver = driver.RunGenerators(edited);

        var steps = driver.GetRunResult().Results[0].TrackedSteps[QuestionSetGenerator.TrackingName];
        Assert.NotEmpty(steps);
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Step output was {output.Reason}."));
    }

    [Fact]
    public void InvalidType_UnrelatedEdit_KeepsQuestionSetsCached()
    {
        var compilation = GeneratorHarness.Compile("using ZeroAlloc.Jev;\n[JevQuestions] public class NotPartial { }");
        var driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("internal static class Unrelated { }", GeneratorHarness.ParseOptions));
        driver = driver.RunGenerators(edited);

        var steps = driver.GetRunResult().Results[0].TrackedSteps[QuestionSetGenerator.TrackingName];
        Assert.NotEmpty(steps);
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Step output was {output.Reason}."));
    }

    private static void AssertGenerates(string source)
    {
        var driver = GeneratorHarness.Run(source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
        GeneratorSnapshot.Verify(driver);
    }

    // Like AssertGenerates, but without a snapshot: used for cases that only need to prove the
    // generated code compiles, not to pin its exact shape.
    private static void AssertCompiles(string source)
    {
        GeneratorHarness.Run(source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }
}
