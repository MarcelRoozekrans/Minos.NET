using System.Globalization;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>JEV101–107, which the generator used to report, now reported by the analyzer at the same locations.</summary>
public sealed class MovedDiagnosticTests
{
    public static TheoryData<string> Cases => new()
    {
        "[JevQuestions] public class {|JEV101:NotPartial|} { }",
        "[JevQuestions] public partial class {|JEV101:Generic|}<T> { }",
        "public partial class Outer { [JevQuestions] public partial class {|JEV101:Inner|} { } }",
        "[JevQuestions] public abstract partial class {|JEV101:Base|} { }",
        "[JevQuestions] file partial class {|JEV101:FileLocal|} { }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; set; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public static partial Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Parse|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:QuestionsUtf8|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public virtual partial Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public sealed partial Noul {|JEV102:Answer|} { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public override partial Noul {|JEV102:Answer|} { get; } }",
        "public class Base { public Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public new partial Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Noul {|JEV103:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul {|JEV103:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<int> {|JEV103:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")][Score(\"q\")] public partial Noul {|JEV103:Answer|} { get; } }",
        "public enum L { [Level(\"a\")] A, {|JEV104:B|} } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, {|JEV104:B|} } [JevQuestions] public partial class C { "
            + "[Score(\"q1\")] public partial Score<L> Answer1 { get; } [Score(\"q2\")] public partial Score<L> Answer2 { get; } }",
        "[JevQuestions] public partial record {|JEV105:R|}(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class {|JEV105:C|} { public required int Foo; [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public class Base { public required int Foo; } "
            + "[JevQuestions] public partial class {|JEV105:C|} : Base { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class {|JEV106:C|} { [Noul(\"a\")] public partial Noul IsUrgent { get; } [Noul(\"b\", Key = \"is_urgent\")] public partial Noul Other { get; } }",
        "public enum E { [Criteria(\"x\", Key = \"b\")] A, [Criteria(\"y\")] B } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> {|JEV106:Answer|} { get; } }",
        "[JevQuestions({|JEV107:State = typeof(IFoo)|})] public partial class C { } public interface IFoo { }",
        "[JevQuestions({|JEV107:State = typeof(System.Collections.Generic.List<>)|})] public partial class C { }",
        "[JevQuestions({|JEV107:State = typeof(void)|})] public partial class C { }",
        "[JevQuestions({|JEV107:State = typeof(MyDelegate)|})] public partial class C { } public delegate void MyDelegate();",
        "[JevQuestions({|JEV107:State = typeof(MyEnum)|})] public partial class C { } public enum MyEnum { A }",
        "[JevQuestions({|JEV107:State = typeof(StaticState)|})] public partial class C { } public static class StaticState { }",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task InvalidDeclaration_ReportsError(string source) => AnalyzerVerifier.VerifyAsync(source);

    public static TheoryData<string> ValidCases => new()
    {
        "public enum L { [Level(\"a\")] A, [Level(\"b\")] B } "
            + "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul A { get; } [Score(\"q\")] public partial Score<L> B { get; } }",
        "public enum E { [Criteria(\"x\")] A, [Criteria(\"y\")] B } "
            + "[JevQuestions(State = typeof(S))] public partial record C { [Choice(\"q\")] public partial Choice<E> Answer { get; } } public sealed class S { }",
        "[JevQuestions] public partial record R(int X = 0) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { public C(int x = 0) { } [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { public required int Foo; "
            + "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public C() { } [Noul(\"q\")] public partial Noul Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(ValidCases))]
    public Task ValidDeclaration_ReportsNothing(string source) => AnalyzerVerifier.VerifyNoDiagnosticsAsync(source);

    [Fact]
    public async Task AttributeTypeMismatch_MultipleAttributes_NamesEveryAttribute()
    {
        var (type, attribute) = await GetQuestionSetAsync(
            "[JevQuestions] public partial class C { [Choice(\"q\")][Score(\"q\")] public partial Noul Answer { get; } }");

        var info = SingleDiagnostic(
            ModelBuilder.Build(type, attribute, CancellationToken.None).Diagnostics, DiagnosticIds.AttributeTypeMismatch);

        var message = Format(Diagnostics.AttributeTypeMismatch, info);

        // Pins the join order: the attributes are named in declaration order, not sorted.
        Assert.Contains("Choice, Score", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoParameterlessConstructor_RequiredMember_NamesTheMember()
    {
        var (type, attribute) = await GetQuestionSetAsync(
            "[JevQuestions] public partial class C { public required int Foo; [Noul(\"q\")] public partial Noul Answer { get; } }");

        var info = SingleDiagnostic(
            ModelBuilder.Build(type, attribute, CancellationToken.None).Diagnostics, DiagnosticIds.NoParameterlessConstructor);

        var message = Format(Diagnostics.NoParameterlessConstructor, info);

        Assert.Contains("Foo", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoParameterlessConstructor_RequiredMemberOnBaseType_NamesTheMember()
    {
        var (type, attribute) = await GetQuestionSetAsync(
            "public class Base { public required int Foo; } "
                + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public partial Noul Answer { get; } }");

        var info = SingleDiagnostic(
            ModelBuilder.Build(type, attribute, CancellationToken.None).Diagnostics, DiagnosticIds.NoParameterlessConstructor);

        var message = Format(Diagnostics.NoParameterlessConstructor, info);

        Assert.Contains("Foo", message, StringComparison.Ordinal);
    }

    // JEV003 is a new rule, not a moved one; it sits here to share the message helpers below.
    [Fact]
    public async Task EmptyText_InstructionText_MessageNamesTheText()
    {
        var (type, attribute) = await GetQuestionSetAsync(
            "[JevQuestions] public partial class C { [Noul(\" \")] public partial Noul Answer { get; } }");

        var info = SingleDiagnostic(ModelBuilder.Build(type, attribute, CancellationToken.None).Diagnostics, DiagnosticIds.EmptyText);

        Assert.Equal(
            "The instruction text of 'Answer' is empty or whitespace: write the text, or pass null to send none",
            Format(Diagnostics.EmptyText, info));
    }

    [Fact]
    public async Task EmptyText_EmptyEntry_MessageAdvisesRemovingTheEntry()
    {
        var compilation = await CompilationHelper.CompileAsync(
            "using ZeroAlloc.Jev;\npublic enum E { [Criteria(\"a\", Examples = [\" \"])] A }");
        var enumType = compilation.GetTypeByMetadataName("E")
            ?? throw new InvalidOperationException("Type 'E' not found in the compiled source.");

        var info = SingleDiagnostic(
            ModelBuilder.ValidateEnum(enumType, QuestionKind.Choice, CancellationToken.None), DiagnosticIds.EmptyText);

        Assert.Equal(
            "An entry of Examples on the [Criteria] description of 'E.A' is empty or whitespace: write the text, or remove the entry",
            Format(Diagnostics.EmptyText, info));
    }

    [Fact]
    public async Task EmptyText_LevelDescription_MessageNamesTheText()
    {
        // An enum's rules come from the enum's own analysis, not the set's build.
        var compilation = await CompilationHelper.CompileAsync(
            "using ZeroAlloc.Jev;\npublic enum E { [Level(\"a\")] A, [Level(\"\")] B }");
        var enumType = compilation.GetTypeByMetadataName("E")
            ?? throw new InvalidOperationException("Type 'E' not found in the compiled source.");

        var info = SingleDiagnostic(
            ModelBuilder.ValidateEnum(enumType, QuestionKind.Score, CancellationToken.None), DiagnosticIds.EmptyText);

        Assert.StartsWith(
            "The [Level] description of 'E.B' is empty or whitespace", Format(Diagnostics.EmptyText, info), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingLevel_OnEnumFromReferencedAssembly_ReportsOnProperty()
    {
        const string externalSource = """
            using ZeroAlloc.Jev;

            namespace External;

            public enum Grade
            {
                [Level("Good")] Good,
                Bad,
            }
            """;

        var externalCompilation = await CompilationHelper.CompileAsync(externalSource, "External");
        var reference = externalCompilation.EmitToReference();

        const string source = """
            using External;

            [JevQuestions]
            public partial class C
            {
                [Score("q")]
                public partial Score<Grade> {|JEV104:Answer|} { get; }
            }
            """;

        await AnalyzerVerifier.VerifyAsync(source, reference);
    }

    [Fact]
    public async Task MissingLevel_TwoMembersOfEnumFromReferencedAssembly_ReportsEachOnProperty()
    {
        var reference = (await CompilationHelper.CompileAsync(
                "using ZeroAlloc.Jev; namespace External; public enum Grade { [Level(\"Good\")] Good, Bad, Worse }", "External"))
            .EmitToReference();

        // Both members land on the same property location; only their names tell them apart, so neither may be dropped.
        await AnalyzerVerifier.VerifyAsync(
            "using External; [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<Grade> {|JEV104:{|JEV104:Answer|}|} { get; } }",
            reference);
    }

    /// <summary>Builds the symbol and the <c>[JevQuestions]</c> application for a source's <c>C</c> type, so a
    /// diagnostic's message can be inspected directly instead of only its id and location.</summary>
    private static async Task<(INamedTypeSymbol Type, AttributeData Attribute)> GetQuestionSetAsync(string source)
    {
        var compilation = await CompilationHelper.CompileAsync("using ZeroAlloc.Jev;\n" + source);
        var type = compilation.GetTypeByMetadataName("C")
            ?? throw new InvalidOperationException("Type 'C' not found in the compiled source.");
        var attribute = type.GetAttributes()
            .First(a => string.Equals(a.AttributeClass?.Name, "JevQuestionsAttribute", StringComparison.Ordinal));
        return (type, attribute);
    }

    /// <summary>The one diagnostic with <paramref name="id"/>, for the message-content facts.</summary>
    // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single()
    // (#38 tracks this repo-wide false positive; the pragma lives here once instead of at every call site).
#pragma warning disable HLQ005
    private static DiagnosticInfo SingleDiagnostic(IEnumerable<DiagnosticInfo> diagnostics, string id)
        => Assert.Single(diagnostics, d => string.Equals(d.Id, id, StringComparison.Ordinal));
#pragma warning restore HLQ005

    private static string Format(DiagnosticDescriptor descriptor, DiagnosticInfo info)
        => string.Format(
            CultureInfo.InvariantCulture,
            descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture),
            [.. info.Arguments.Cast<object>()]);
}
