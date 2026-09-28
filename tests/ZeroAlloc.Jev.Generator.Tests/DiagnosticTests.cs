using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Jev.Generator.Tests;

/// <summary>
/// The generator reports no diagnostics; the analyzer does. For an invalid set it emits no question set, only
/// throwing stubs for the partial question properties it can implement, so the compiler reports no CS9248 for them.
/// </summary>
public sealed class DiagnosticTests
{
    // Invalid sets: the analyzer reports JEV001, JEV002 and JEV101–107 for these (ZeroAlloc.Jev.Analyzers.Tests:
    // MovedDiagnosticTests, ApiRuleTests, InvalidSetBuildTests). The generator stubs every unimplemented partial
    // question property, whatever its shape; a type JEV101 rejects gets no stub, since a partial part could not help.
    public static TheoryData<string> InvalidSources => new()
    {
        "[JevQuestions] public class NotPartial { }",
        "[JevQuestions] public partial class Generic<T> { }",
        "public partial class Outer { [JevQuestions] public partial class Inner { } }",
        "[JevQuestions] public abstract partial class Base { }",
        "[JevQuestions] file partial class FileLocal { }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul Parse { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul QuestionsUtf8 { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<int> Answer { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")][Score(\"q\")] public partial Noul Answer { get; } }",
        "public enum L { [Level(\"a\")] A, B } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, B } [JevQuestions] public partial class C { "
            + "[Score(\"q1\")] public partial Score<L> Answer1 { get; } [Score(\"q2\")] internal partial Score<L> Answer2 { get; } }",
        "[JevQuestions] public partial record R(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { public required int Foo; [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public class Base { public required int Foo; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"a\")] public partial Noul IsUrgent { get; } [Noul(\"b\", Key = \"is_urgent\")] private partial Noul Other { get; } }",
        "public enum E { [Criteria(\"x\", Key = \"b\")] A, [Criteria(\"y\")] B } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "[JevQuestions(State = typeof(IFoo))] public partial class C { [Noul(\"q\")] public partial Noul Answer { get; } } public interface IFoo { }",
        "[JevQuestions(State = typeof(System.Collections.Generic.List<>))] public partial class C { }",
        "[JevQuestions(State = typeof(void))] public partial class C { }",
        "[JevQuestions(State = typeof(MyDelegate))] public partial class C { } public delegate void MyDelegate();",
        "[JevQuestions(State = typeof(MyEnum))] public partial class C { } public enum MyEnum { A }",
        "[JevQuestions(State = typeof(StaticState))] public partial class C { } public static class StaticState { }",
        "public enum E { } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { } [JevQuestions] public partial record C { [Score(\"q\")] internal partial Score<L> Answer { get; } }",
        "namespace @class; public enum E { } [JevQuestions] public partial class @event { [Choice(\"q\")] public partial Choice<E> @int { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul Answer { get; set; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul Answer { internal get; private init; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public static partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public virtual partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public sealed partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public required partial Noul Answer { get; set; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public override partial Noul Answer { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public sealed override partial Noul Answer { get; } }",
        "public class Base { public Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public new partial Noul Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void InvalidDeclaration_ReportsNothing_AndEmitsOnlyStubsThatCompile(string source)
    {
        var driver = GeneratorHarness.Run("using ZeroAlloc.Jev;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        AssertNoQuestionSet(driver);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => string.Equals(diagnostic.Id, "CS9248", StringComparison.Ordinal));

        // The stubs themselves compile. A stub repeats its definition's declared type, so a user's own type error, such
        // as Choice<int> breaking the enum constraint, recurs in it; the stub adds no error the source does not have.
        var generated = driver.GetRunResult().GeneratedTrees;
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        var sourceErrorIds = errors
            .Where(diagnostic => diagnostic.Location.SourceTree is not { } tree || !generated.Contains(tree))
            .Select(diagnostic => diagnostic.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            errors.Where(diagnostic => diagnostic.Location.SourceTree is { } tree && generated.Contains(tree)),
            diagnostic => Assert.Contains(diagnostic.Id, sourceErrorIds));
    }

    [Fact]
    public void NonPartialQuestionProperty_NeedsNoStub_AndGeneratesNothing()
    {
        var driver = GeneratorHarness.Run(
            "using ZeroAlloc.Jev;\n[JevQuestions] public partial class C { [Noul(\"q\")] public Noul Answer { get; } }",
            out _,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void InvalidSet_StubsEveryPartialQuestionProperty_MirroringItsDeclaration()
    {
        var driver = GeneratorHarness.Run(
            "using ZeroAlloc.Jev;\nnamespace Demo; public enum E { } [JevQuestions] public partial record C { "
                + "[Choice(\"q\")] public partial Choice<E> A1 { get; } [Noul(\"q\")] internal partial Noul A2 { get; } "
                + "[Noul(\"q\")] public virtual partial Noul A3 { get; private set; } public partial int NotAQuestion { get; } }",
            out _,
            out _);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single() (#38).
#pragma warning disable HLQ005
        var stub = Assert.Single(driver.GetRunResult().GeneratedTrees).ToString();
#pragma warning restore HLQ005

        Assert.Contains("partial record C", stub, StringComparison.Ordinal);
        Assert.Contains(
            "public partial global::ZeroAlloc.Jev.Choice<global::Demo.E> A1 { get => throw new global::System.InvalidOperationException(\"This [JevQuestions] set is invalid; see the JEV diagnostics.\"); }",
            stub,
            StringComparison.Ordinal);
        Assert.Contains("internal partial global::ZeroAlloc.Jev.Noul A2 { get => throw", stub, StringComparison.Ordinal);
        Assert.Contains(
            "public virtual partial global::ZeroAlloc.Jev.Noul A3 { get => throw new global::System.InvalidOperationException(\"This [JevQuestions] set is invalid; see the JEV diagnostics.\"); "
                + "private set => throw new global::System.InvalidOperationException(\"This [JevQuestions] set is invalid; see the JEV diagnostics.\"); }",
            stub,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NotAQuestion", stub, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Sources.ChoiceOverEmptyEnum)]
    [InlineData(Sources.ScoreOverEmptyEnum)]
    public void EmptyEnum_ReportsNothing_AndEmitsOnlyStubs(string source)
    {
        var driver = GeneratorHarness.Run(source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        AssertNoQuestionSet(driver);
        Assert.NotEmpty(driver.GetRunResult().GeneratedTrees);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    // The sources the analyzer warns about with JEV003–006: advice, not errors, so the generator still emits the set.
    public static TheoryData<string> AdvisorySources => new()
    {
        "[JevQuestions] public partial class C { [Noul(\"  \")] public partial Noul Answer { get; } }",
        "public enum E { [Criteria(\"\")] A } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, [Level(\" \")] B } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public sealed class S { public int Known { get; set; } } "
            + "[JevQuestions(State = typeof(S))] public partial class C { [Noul(\"Is `unknown` set?\")] public partial Noul Answer { get; } }",
        "public enum L { [Level(\"a\")] A } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum E { A, B } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(AdvisorySources))]
    public void AdvisoryOnlyDeclaration_ReportsNothing_AndGenerates(string source)
    {
        var driver = GeneratorHarness.Run("using ZeroAlloc.Jev;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains(driver.GetRunResult().GeneratedTrees, tree => tree.ToString().Contains("IJevQuestionSet", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    /// <summary>No generated source carries the question set: no <c>IJevQuestionSet</c>, <c>QuestionsUtf8</c> or <c>Parse</c>.</summary>
    private static void AssertNoQuestionSet(GeneratorDriver driver)
    {
        foreach (var tree in driver.GetRunResult().GeneratedTrees)
        {
            var text = tree.ToString();
            Assert.DoesNotContain("IJevQuestionSet", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ReadOnlySpan<byte> QuestionsUtf8", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Parse(ref", text, StringComparison.Ordinal);
        }
    }
}
