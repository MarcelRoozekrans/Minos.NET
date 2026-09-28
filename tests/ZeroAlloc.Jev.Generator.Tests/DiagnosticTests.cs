using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Jev.Generator.Tests;

/// <summary>The generator reports no diagnostics: for an invalid set it only skips emitting; the analyzer reports.</summary>
public sealed class DiagnosticTests
{
    // The sources the analyzer reports JEV101–107 for (ZeroAlloc.Jev.Analyzers.Tests.MovedDiagnosticTests).
    public static TheoryData<string> InvalidSources => new()
    {
        "[JevQuestions] public class NotPartial { }",
        "[JevQuestions] public partial class Generic<T> { }",
        "public partial class Outer { [JevQuestions] public partial class Inner { } }",
        "[JevQuestions] public abstract partial class Base { }",
        "[JevQuestions] file partial class FileLocal { }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul Answer { get; set; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public static partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul Parse { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul QuestionsUtf8 { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public virtual partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public sealed partial Noul Answer { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public override partial Noul Answer { get; } }",
        "public class Base { public Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public new partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<int> Answer { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")][Score(\"q\")] public partial Noul Answer { get; } }",
        "public enum L { [Level(\"a\")] A, B } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, B } [JevQuestions] public partial class C { "
            + "[Score(\"q1\")] public partial Score<L> Answer1 { get; } [Score(\"q2\")] public partial Score<L> Answer2 { get; } }",
        "[JevQuestions] public partial record R(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { public required int Foo; [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public class Base { public required int Foo; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"a\")] public partial Noul IsUrgent { get; } [Noul(\"b\", Key = \"is_urgent\")] public partial Noul Other { get; } }",
        "public enum E { [Criteria(\"x\", Key = \"b\")] A, [Criteria(\"y\")] B } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "[JevQuestions(State = typeof(IFoo))] public partial class C { } public interface IFoo { }",
        "[JevQuestions(State = typeof(System.Collections.Generic.List<>))] public partial class C { }",
        "[JevQuestions(State = typeof(void))] public partial class C { }",
        "[JevQuestions(State = typeof(MyDelegate))] public partial class C { } public delegate void MyDelegate();",
        "[JevQuestions(State = typeof(MyEnum))] public partial class C { } public enum MyEnum { A }",
        "[JevQuestions(State = typeof(StaticState))] public partial class C { } public static class StaticState { }",
    };

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void InvalidDeclaration_ReportsNothing_AndGeneratesNothing(string source)
    {
        var driver = GeneratorHarness.Run("using ZeroAlloc.Jev;\n" + source, out _, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Theory]
    [InlineData(Sources.ChoiceOverEmptyEnum)]
    [InlineData(Sources.ScoreOverEmptyEnum)]
    public void EmptyEnum_ReportsNothing_AndGeneratesNothing(string source)
    {
        var driver = GeneratorHarness.Run(source, out _, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
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
        Assert.NotEmpty(driver.GetRunResult().GeneratedTrees);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }
}
