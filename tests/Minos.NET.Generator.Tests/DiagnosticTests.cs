using Microsoft.CodeAnalysis;

namespace Minos.Generator.Tests;

/// <summary>
/// The generator reports no diagnostics; the analyzer does. For an invalid set it emits no question set, only
/// throwing stubs for the partial question properties it can implement, so the compiler reports no CS9248 for them.
/// </summary>
public sealed class DiagnosticTests
{
    // Invalid sets: the analyzer reports MIN001, MIN002 and MIN101–107 for these (Minos.NET.Analyzers.Tests:
    // MovedDiagnosticTests, ApiRuleTests, InvalidSetBuildTests). The generator stubs every unimplemented partial
    // question property, whatever its shape, wherever a partial part can reach it: that includes MIN101 types, apart
    // from those in Min101UnstubbableSources.
    public static TheoryData<string> InvalidSources => new()
    {
        "[Questions] public class NotPartial { }",
        "[Questions] public partial class Generic<T> { }",
        "public partial class Outer { [Questions] public partial class Inner { } }",
        "[Questions] public abstract partial class Base { }",
        "[Questions] file partial class FileLocal { }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul Create { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul Definition { get; } }",
        "[Questions] public partial class C { [Choice(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Choice(\"q\")] public partial Choice<int> Answer { get; } }",
        "[Questions] public partial class C { [Choice(\"q\")][Score(\"q\")] public partial Noul Answer { get; } }",
        "public enum L { [Level(\"a\")] A, B } [Questions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, B } [Questions] public partial class C { "
            + "[Score(\"q1\")] public partial Score<L> Answer1 { get; } [Score(\"q2\")] internal partial Score<L> Answer2 { get; } }",
        "[Questions] public partial record R(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class C { public required int Foo; [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public class Base { public required int Foo; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"a\")] public partial Noul IsUrgent { get; } [Noul(\"b\", Key = \"is_urgent\")] private partial Noul Other { get; } }",
        "public enum E { [Criteria(\"x\", Key = \"b\")] A, [Criteria(\"y\")] B } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"q\", Key = \"\")] public partial Noul Answer { get; } }",
        "public enum E { [Criteria(\"x\", Key = \"\")] A, [Criteria(\"y\")] B } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "[Questions(State = typeof(IFoo))] public partial class C { [Noul(\"q\")] public partial Noul Answer { get; } } public interface IFoo { }",
        "[Questions(State = typeof(System.Collections.Generic.List<>))] public partial class C { }",
        "[Questions(State = typeof(void))] public partial class C { }",
        "[Questions(State = typeof(MyDelegate))] public partial class C { } public delegate void MyDelegate();",
        "[Questions(State = typeof(MyEnum))] public partial class C { } public enum MyEnum { A }",
        "[Questions(State = typeof(StaticState))] public partial class C { } public static class StaticState { }",
        "public enum E { } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { } [Questions] public partial record C { [Score(\"q\")] internal partial Score<L> Answer { get; } }",
        "namespace @class; public enum E { } [Questions] public partial class @event { [Choice(\"q\")] public partial Choice<E> @int { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul Answer { get; set; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul Answer { internal get; private init; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public static partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public virtual partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public sealed partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public required partial Noul Answer { get; set; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public override partial Noul Answer { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public sealed override partial Noul Answer { get; } }",
        "public class Base { public Noul Answer => default; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public new partial Noul Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void InvalidDeclaration_ReportsNothing_AndEmitsOnlyStubsThatCompile(string source)
    {
        var driver = GeneratorHarness.Run("using Minos;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        AssertNoQuestionSet(driver);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => string.Equals(diagnostic.Id, "CS9248", StringComparison.Ordinal));
        AssertStubsAddNoError(driver, output);
    }

    // Declared sets are text only. Json is no longer a named argument of any question-set attribute, so setting it is
    // the compiler's own error at the argument, and the generator adds nothing to it.
    public static TheoryData<string> JsonNamedArgumentSources => new()
    {
        "[Questions] public partial class C { [Noul(\"{}\", Json = true)] public partial Noul Answer { get; } }",
        "public enum E { [Criteria(\"a\")] A } [Questions] public partial class C { [Choice(\"[]\", Json = true)] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, [Level(\"b\")] B } [Questions] public partial class C { [Score(\"[]\", Json = true)] public partial Score<L> Answer { get; } }",
        "public enum E { [Criteria(\"{}\", Json = true)] A } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"{}\", Json = true)] A, [Level(\"b\")] B } [Questions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(JsonNamedArgumentSources))]
    public void JsonNamedArgument_DoesNotCompile(string source)
    {
        GeneratorHarness.Run("using Minos;\n" + source, out var output, out var diagnostics);

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
            "using Minos;\n[Questions] public partial class C { [Noul(\"q\")] public Noul Answer { get; } }",
            out _,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void InvalidSet_StubsEveryPartialQuestionProperty_MirroringItsDeclaration()
    {
        var driver = GeneratorHarness.Run(
            "using Minos;\nnamespace Demo; public enum E { } [Questions] public partial record C { "
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
            "public partial global::Minos.Choice<global::Demo.E> A1 { get => throw new global::System.InvalidOperationException(\"This [Questions] set is invalid; see the MIN diagnostics.\"); }",
            stub,
            StringComparison.Ordinal);
        Assert.Contains("internal partial global::Minos.Noul A2 { get => throw", stub, StringComparison.Ordinal);
        Assert.Contains(
            "public virtual partial global::Minos.Noul A3 { get => throw new global::System.InvalidOperationException(\"This [Questions] set is invalid; see the MIN diagnostics.\"); "
                + "private set => throw new global::System.InvalidOperationException(\"This [Questions] set is invalid; see the MIN diagnostics.\"); }",
            stub,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NotAQuestion", stub, StringComparison.Ordinal);
    }

    // MIN101 types a partial part can still complete: nested in partial types, generic, abstract, static, or not a
    // class at all. Each gets its stubs, so a build shows MIN101 rather than CS9248.
    public static TheoryData<string> Min101StubbableSources => new()
    {
        "public partial class Outer { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "[Questions] public partial class Set<T> { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class Set<T> where T : struct, System.Enum { [Choice(\"q\")] public partial Choice<T> Answer { get; } }",
        "[Questions] public partial class Set<T> where T : class? { [Noul(\"q\")] public partial Noul Answer { get; } public T? Value => default; }",
        "[Questions] public abstract partial class Set { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public abstract partial record Set { [Noul(\"q\")] protected internal partial Noul Answer { get; } }",
        "[Questions] public static partial class Set { [Noul(\"q\")] public static partial Noul Answer { get; } }",
        "[Questions] public partial struct Set { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "namespace Demo; public partial struct Outer<TKey> where TKey : notnull { internal partial record Middle { "
            + "[Questions] private partial class Inner<T> { [Noul(\"q\")] public partial Noul Answer { get; } } } }",
        "public partial interface IOuter<in TIn, out TOut> { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public readonly partial struct Outer { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public ref partial struct Outer { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public static partial class Outer { [Questions] internal partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public sealed partial record class Outer { [Questions] public partial record Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public partial record struct Outer { [Questions] protected internal partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "namespace @class; public partial class @event<@int> { [Questions] public partial class @string { [Noul(\"q\")] public partial Noul @bool { get; } } }",
    };

    [Theory]
    [MemberData(nameof(Min101StubbableSources))]
    public void Min101TypeAPartialPartCanComplete_EmitsStubsThatCompile(string source)
    {
        var driver = GeneratorHarness.Run("using Minos;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        AssertNoQuestionSet(driver);
        Assert.NotEmpty(driver.GetRunResult().GeneratedTrees);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => string.Equals(diagnostic.Id, "CS9248", StringComparison.Ordinal));
        AssertStubsAddNoError(driver, output);
    }

    // MIN101 types no partial part can complete: the type is not partial or is file-local, or a type containing it is
    // not partial or is file-local. A declaration in another file could not reach it, so nothing is generated.
    public static TheoryData<string> Min101UnstubbableSources => new()
    {
        "[Questions] public class NotPartial { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] file partial class FileLocal { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public class Outer { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "public partial class Outer { public class Middle { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } } }",
        "file partial class Outer { [Questions] public partial class Inner { [Noul(\"q\")] public partial Noul Answer { get; } } }",
    };

    [Theory]
    [MemberData(nameof(Min101UnstubbableSources))]
    public void Min101TypeNoPartialPartCanComplete_GeneratesNothing(string source)
    {
        var driver = GeneratorHarness.Run("using Minos;\n" + source, out _, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public void Min101NestedGenericSet_StubRepeatsItsContainingTypesAndTypeParameters()
    {
        var driver = GeneratorHarness.Run(
            "using Minos;\nnamespace Demo; public partial struct Outer<TKey> where TKey : notnull { internal partial record Middle { "
                + "[Questions] private partial class Inner<T> { [Noul(\"q\")] public partial Noul Answer { get; } } } }",
            out _,
            out _);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single() (#38).
#pragma warning disable HLQ005
        var tree = Assert.Single(driver.GetRunResult().GeneratedTrees);
#pragma warning restore HLQ005

        Assert.EndsWith("Demo.Outer`1.Middle.Inner`1.Questions.g.cs", tree.FilePath, StringComparison.Ordinal);
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
                        public partial global::Minos.Noul Answer { get => throw new global::System.InvalidOperationException("This [Questions] set is invalid; see the MIN diagnostics."); }
                    }
                }
            }

            """.ReplaceLineEndings("\n"),
            tree.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Min101SetsSharingAName_GetDistinctStubFiles()
    {
        var driver = GeneratorHarness.Run(
            "using Minos;\nnamespace Demo; "
                + "[Questions] public abstract partial class Set { [Noul(\"q\")] public partial Noul Answer { get; } } "
                + "[Questions] public partial class Set<T> { [Noul(\"q\")] public partial Noul Answer { get; } } "
                + "[Questions] public partial class Set<T1, T2> { [Noul(\"q\")] public partial Noul Answer { get; } } "
                + "public partial class Outer { [Questions] public partial class Set { [Noul(\"q\")] public partial Noul Answer { get; } } }",
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

    // The sources the analyzer warns about with MIN003–006: advice, not errors, so the generator still emits the set.
    public static TheoryData<string> AdvisorySources => new()
    {
        "[Questions] public partial class C { [Noul(\"  \")] public partial Noul Answer { get; } }",
        "public enum E { [Criteria(\"\")] A } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, [Level(\" \")] B } [Questions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public sealed class S { public int Known { get; set; } } "
            + "[Questions(State = typeof(S))] public partial class C { [Noul(\"Is `unknown` set?\")] public partial Noul Answer { get; } }",
        "public enum L { [Level(\"a\")] A } [Questions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum E { A, B } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(AdvisorySources))]
    public void AdvisoryOnlyDeclaration_ReportsNothing_AndGenerates(string source)
    {
        var driver = GeneratorHarness.Run("using Minos;\n" + source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains(driver.GetRunResult().GeneratedTrees, tree => tree.ToString().Contains("IQuestionSet", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    /// <summary>
    /// The generator no longer emits <c>Parse</c> or <c>QuestionsUtf8</c>, so a question property may take either name:
    /// the set is generated and compiles.
    /// </summary>
    [Theory]
    [InlineData("Parse")]
    [InlineData("QuestionsUtf8")]
    public void PropertyNamedLikeARemovedMember_IsNoLongerReserved(string name)
    {
        var source = "using Minos;\nnamespace Demo;\n[Questions] public partial class Named { [Noul(\"Is this urgent?\")] public partial Noul "
            + name + " { get; } }";

        var driver = GeneratorHarness.Run(source, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains(driver.GetRunResult().GeneratedTrees, tree => tree.ToString().Contains("IQuestionSet", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    /// <summary>No generated source carries the question set: no <c>IQuestionSet</c>, <c>Definition</c> or <c>Create</c>.</summary>
    private static void AssertNoQuestionSet(GeneratorDriver driver)
    {
        foreach (var tree in driver.GetRunResult().GeneratedTrees)
        {
            var text = tree.ToString();
            Assert.DoesNotContain("IQuestionSet", text, StringComparison.Ordinal);
            Assert.DoesNotContain("QuestionSetDefinition Definition", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Create(global::Minos.AnswerSlots", text, StringComparison.Ordinal);
        }
    }
}
