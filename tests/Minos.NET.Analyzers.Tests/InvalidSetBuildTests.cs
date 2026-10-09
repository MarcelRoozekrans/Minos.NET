namespace Minos.Analyzers.Tests;

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
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; set; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; private set; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; init; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public static partial Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public virtual partial Noul {|JEV102:Answer|} { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public sealed override partial Noul {|JEV102:Answer|} { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public override partial Noul {|JEV102:Answer|} { get; } }",
        "public class Base { public Noul Answer => default; } "
            + "[JevQuestions] public partial class C : Base { [Noul(\"q\")] public new partial Noul {|JEV102:Answer|} { get; } }",
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; set; } "
            + "[Noul(\"q2\")] internal partial Noul Other { get; } }",
        // 'sealed' without 'override' is the user's own C# error, CS0238, which the stub cannot remove: it is still reported.
        "[JevQuestions] public partial class C { [Noul(\"q\")] public sealed partial Noul {|CS0238:{|JEV102:Answer|}|} { get; } }",
        "[JevQuestions] public partial class {|JEV105:C|} { [Noul(\"q\")] public required partial Noul {|JEV102:Answer|} { get; set; } }",
        "namespace Demo.Nested { public enum {|JEV001:E|} { } [JevQuestions] public partial class C { "
            + "[Choice(\"q\")] public partial Choice<E> Answer { get; } } }",
        // JEV101 types a partial part can still complete get their stubs too: nested in partial types, generic, abstract.
        "public partial class Outer { [JevQuestions] public partial class {|JEV101:Inner|} { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "[JevQuestions] public partial class {|JEV101:Set|}<T> where T : struct, System.Enum { "
            + "[Choice(\"q\")] public partial Choice<T> Answer { get; } [Noul(\"q2\")] internal partial Noul Other { get; } }",
        "[JevQuestions] public abstract partial class {|JEV101:Set|} { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "namespace Demo; public partial struct Outer<TKey> where TKey : notnull { internal partial record Middle { "
            + "[JevQuestions] private partial class {|JEV101:Inner|}<T> { [Noul(\"q\")] public partial Noul Answer { get; set; } } } }",
        "[JevQuestions] public static partial class {|JEV101:Set|} { [Noul(\"q\")] public static partial Noul Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task InvalidSet_ReportsOnlyTheJevError(string source) => AnalyzerVerifier.VerifyWithGeneratorAsync(source);
}
