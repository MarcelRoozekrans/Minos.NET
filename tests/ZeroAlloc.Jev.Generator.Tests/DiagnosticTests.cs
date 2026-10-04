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
    // question property, whatever its shape, wherever a partial part can reach it: that includes JEV101 types, apart
    // from those in Jev101UnstubbableSources.
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
        AssertStubsAddNoError(driver, output);
    }

    // Declared sets are text only. Json is no longer a named argument of any question-set attribute, so setting it is
    // the compiler's own error at the argument, and the generator adds nothing to it.
    public static TheoryData<string> JsonNamedArgumentSources => new()
    {
        "[JevQuestions] public partial class C { [Noul(\"{}\", Json = true)] public partial Noul Answer { get; } }",
        "public enum E { [Criteria(\"a\")] A } [JevQuestions] public partial class C { [Choice(\"[]\", Json = true)] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, [Level(\"b\")] B } [JevQuestions] public partial class C { [Score(\"[]\", Json = true)] public partial Score<L> Answer { get; } }",
        "public enum E { [Criteria(\"{}\", Json = true)] A } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"{}\", Json = true)] A, [Level(\"b\")] B } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(JsonNamedArgumentSources))]
    public void JsonNamedArgument_DoesNotCompile(string source)
    {
        GeneratorHarness.Run("using ZeroAlloc.Jev;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Equal(["CS0246"], errors.Select(error => error.Id));
        Assert.Equal("Json", errors[0].Location.SourceTree!.GetText().ToString(errors[0].Location.SourceSpan));
    }

    /// <summary>
    /// The stubs themselves compile. A stub repeats its definition's declared type, so a user's own type error, such as
    /// <c>Choice&lt;int&gt;</c> breaking the enum constraint, recurs in it; the stub adds no error the source does not have.
    /// </summary>
    private static void AssertStubsAddNoError(GeneratorDriver driver, Compilation output)
    {
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

    // JEV101 types a partial part can still complete: nested in partial types, generic, abstract, static, or not a
    // class at all. Each gets its stubs, so a build shows JEV101 rather than CS9248.
    public static TheoryData<string> Jev101StubbableSources => new()
    {
        "public partial class Outer { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "[JevQuestions] public partial class Set<T> { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class Set<T> where T : struct, System.Enum { [Choice(\"q\")] public partial Choice<T> Answer { get; } }",
        "[JevQuestions] public partial class Set<T> where T : class? { [Noul(\"q\")] public partial Noul Answer { get; } public T? Value => default; }",
        "[JevQuestions] public abstract partial class Set { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public abstract partial record Set { [Noul(\"q\")] protected internal partial Noul Answer { get; } }",
        "[JevQuestions] public static partial class Set { [Noul(\"q\")] public static partial Noul Answer { get; } }",
        "[JevQuestions] public partial struct Set { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "namespace Demo; public partial struct Outer<TKey> where TKey : notnull { internal partial record Middle { "
            + "[JevQuestions] private partial class Inner<T> { [Noul(\"q\")] public partial Noul Answer { get; } } } }",
        "public partial interface IOuter<in TIn, out TOut> { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public readonly partial struct Outer { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public ref partial struct Outer { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public static partial class Outer { [JevQuestions] internal partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public sealed partial record class Outer { [JevQuestions] public partial record Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public partial record struct Outer { [JevQuestions] protected internal partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "namespace @class; public partial class @event<@int> { [JevQuestions] public partial class @string { [Noul(\"q\")] public partial Noul @bool { get; } } }",
    };

    [Theory]
    [MemberData(nameof(Jev101StubbableSources))]
    public void Jev101TypeAPartialPartCanComplete_EmitsStubsThatCompile(string source)
    {
        var driver = GeneratorHarness.Run("using ZeroAlloc.Jev;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        AssertNoQuestionSet(driver);
        Assert.NotEmpty(driver.GetRunResult().GeneratedTrees);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => string.Equals(diagnostic.Id, "CS9248", StringComparison.Ordinal));
        AssertStubsAddNoError(driver, output);
    }

    // JEV101 types no partial part can complete: the type is not partial or is file-local, or a type containing it is
    // not partial or is file-local. A declaration in another file could not reach it, so nothing is generated.
    public static TheoryData<string> Jev101UnstubbableSources => new()
    {
        "[JevQuestions] public class NotPartial { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] file partial class FileLocal { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public class Outer { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public partial class Outer { public class Middle { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } } }",
        "file partial class Outer { [JevQuestions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
    };

    [Theory]
    [MemberData(nameof(Jev101UnstubbableSources))]
    public void Jev101TypeNoPartialPartCanComplete_GeneratesNothing(string source)
    {
        var driver = GeneratorHarness.Run("using ZeroAlloc.Jev;\n" + source, out _, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void Jev101NestedGenericSet_StubRepeatsItsContainingTypesAndTypeParameters()
    {
        var driver = GeneratorHarness.Run(
            "using ZeroAlloc.Jev;\nnamespace Demo; public partial struct Outer<TKey> where TKey : notnull { internal partial record Middle { "
                + "[JevQuestions] private partial class Inner<T> { [Noul(\"q\")] public partial Noul Answer { get; } } } }",
            out _,
            out _);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single() (#38).
#pragma warning disable HLQ005
        var tree = Assert.Single(driver.GetRunResult().GeneratedTrees);
#pragma warning restore HLQ005

        Assert.EndsWith("Demo.Outer`1.Middle.Inner`1.JevQuestions.g.cs", tree.FilePath, StringComparison.Ordinal);
        Assert.Equal(
            """
            // <auto-generated/>
            #nullable enable

            namespace Demo;

            public partial struct Outer<TKey>
            {
                internal partial record Middle
                {
                    partial class Inner<T>
                    {
                        public partial global::ZeroAlloc.Jev.Noul Answer { get => throw new global::System.InvalidOperationException("This [JevQuestions] set is invalid; see the JEV diagnostics."); }
                    }
                }
            }

            """.ReplaceLineEndings("\n"),
            tree.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Jev101SetsSharingAName_GetDistinctStubFiles()
    {
        var driver = GeneratorHarness.Run(
            "using ZeroAlloc.Jev;\nnamespace Demo; "
                + "[JevQuestions] public abstract partial class Set { [Noul(\"q\")] public partial Noul Answer { get; } } "
                + "[JevQuestions] public partial class Set<T> { [Noul(\"q\")] public partial Noul Answer { get; } } "
                + "[JevQuestions] public partial class Set<T1, T2> { [Noul(\"q\")] public partial Noul Answer { get; } } "
                + "public partial class Outer { [JevQuestions] public partial class Set { [Noul(\"q\")] public partial Noul Answer { get; } } }",
            out var output,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(4, driver.GetRunResult().GeneratedTrees.Select(tree => tree.FilePath).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
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
