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
        "[JevQuestions] public partial class C { [Noul(\"q\")] public Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; set; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public static partial Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Noul {|JEV103:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul {|JEV103:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<int> {|JEV103:Answer|} { get; } }",
        "public enum L { [Level(\"a\")] A, {|JEV104:B|} } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "[JevQuestions] public partial record {|JEV105:R|}(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class {|JEV106:C|} { [Noul(\"a\")] public partial Noul IsUrgent { get; } [Noul(\"b\", Key = \"is_urgent\")] public partial Noul Other { get; } }",
        "public enum E { [Criteria(\"x\", Key = \"b\")] A, B } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> {|JEV106:Answer|} { get; } }",
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

    [Fact]
    public Task ValidDeclaration_ReportsNothing() => AnalyzerVerifier.VerifyAsync(
        "public enum L { [Level(\"a\")] A, [Level(\"b\")] B } "
        + "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul A { get; } [Score(\"q\")] public partial Score<L> B { get; } }");
}
