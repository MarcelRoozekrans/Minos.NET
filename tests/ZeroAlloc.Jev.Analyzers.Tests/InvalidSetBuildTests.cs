namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>
/// An invalid set as a build sees it, with the analyzer and the generator together. The generator's throwing stubs
/// implement the partial question properties, so the JEV error is the only error, with no CS9248 beside it. On the
/// command line, CS9248 would stop the build before the analyzer ran, hiding the JEV error.
/// </summary>
public sealed class InvalidSetBuildTests
{
    public static TheoryData<string> Cases => new()
    {
        "public enum {|JEV001:E|} { } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum {|JEV002:L|} { } [JevQuestions] public partial class C { [Score(\"q\")] internal partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, {|JEV104:B|} } [JevQuestions] public partial record C { "
            + "[Score(\"q\")] public partial Score<L> Answer { get; } [Noul(\"q2\")] private partial Noul Other { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul {|JEV103:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Parse|} { get; } }",
        "[JevQuestions] public partial record {|JEV105:R|}(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class {|JEV106:C|} { [Noul(\"a\")] public partial Noul IsUrgent { get; } "
            + "[Noul(\"b\", Key = \"is_urgent\")] public partial Noul Other { get; } }",
        "public interface IFoo { } [JevQuestions({|JEV107:State = typeof(IFoo)|})] public partial class C { "
            + "[Noul(\"q\")] public partial Noul Answer { get; } }",
        "namespace Demo.Nested { public enum {|JEV001:E|} { } [JevQuestions] public partial class C { "
            + "[Choice(\"q\")] public partial Choice<E> Answer { get; } } }",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task InvalidSet_ReportsOnlyTheJevError(string source) => AnalyzerVerifier.VerifyWithGeneratorAsync(source);
}
