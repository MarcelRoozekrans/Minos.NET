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
    public void StructuredCriteria_Generates() => AssertGenerates(Sources.StructuredCriteria);

    [Fact]
    public void JsonText_Generates() => AssertGenerates(Sources.JsonText);

    [Fact]
    public void WithState_Generates() => AssertGenerates(Sources.WithState);

    [Fact]
    public void WithAbstractState_Generates() => AssertGenerates(Sources.WithAbstractState);

    [Fact]
    public void WithNestedState_Generates() => AssertGenerates(Sources.WithNestedState);

    [Fact]
    public void WithClosedGenericState_Generates() => AssertGenerates(Sources.WithClosedGenericState);

    [Fact]
    public void WithArrayState_Generates() => AssertGenerates(Sources.WithArrayState);

    [Fact]
    public void KeywordNamespaceAndType_Generates() => AssertGenerates(Sources.KeywordNamespaceAndType);

    [Fact]
    public void KeywordMembers_Generates() => AssertGenerates(Sources.KeywordMembers);

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

        // An invalid set has no model, and with no question property to stub it has no stubs either: nothing is emitted.
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.Equal(new QuestionSetGenerator.GeneratorInput(null, null), output.Value));
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Step output was {output.Reason}."));
    }

    [Fact]
    public void WithState_UnrelatedEdit_KeepsQuestionSetsCached()
    {
        var compilation = GeneratorHarness.Compile(Sources.WithState);
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
    public void InvalidState_UnrelatedEdit_KeepsQuestionSetsCached()
    {
        var compilation = GeneratorHarness.Compile(
            "using ZeroAlloc.Jev;\n[JevQuestions(State = typeof(IFoo))] public partial class C { } public interface IFoo { }");
        var driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("internal static class Unrelated { }", GeneratorHarness.ParseOptions));
        driver = driver.RunGenerators(edited);

        var steps = driver.GetRunResult().Results[0].TrackedSteps[QuestionSetGenerator.TrackingName];
        Assert.NotEmpty(steps);

        // An invalid set has no model, and with no question property to stub it has no stubs either: nothing is emitted.
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.Equal(new QuestionSetGenerator.GeneratorInput(null, null), output.Value));
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Step output was {output.Reason}."));
    }

    [Fact]
    public void InvalidSetWithStubs_UnrelatedEdit_KeepsQuestionSetsCached()
    {
        var compilation = GeneratorHarness.Compile(Sources.ChoiceOverEmptyEnum);
        var driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("internal static class Unrelated { }", GeneratorHarness.ParseOptions));
        driver = driver.RunGenerators(edited);

        var steps = driver.GetRunResult().Results[0].TrackedSteps[QuestionSetGenerator.TrackingName];
        Assert.NotEmpty(steps);

        // The stub model is value-equatable like the full model, so an unrelated edit leaves it cached.
        Assert.All(
            steps.SelectMany(step => step.Outputs),
            output => Assert.True(
                output.Value is QuestionSetGenerator.GeneratorInput { Model: null, InvalidSet: not null },
                $"Step output was {output.Value}."));
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
}
