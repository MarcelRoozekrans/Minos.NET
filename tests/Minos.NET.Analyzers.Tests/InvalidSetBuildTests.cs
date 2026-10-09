namespace Minos.Analyzers.Tests;

/// <summary>
/// An invalid set as a build sees it, with the analyzer and the generator together. The generator's throwing stubs
/// implement the partial question properties, so the MIN error is the only error, with no CS9248 beside it. On the
/// command line, CS9248 would stop the build before the analyzer ran, hiding the MIN error.
/// </summary>
public sealed class InvalidSetBuildTests
{
    public static TheoryData<string> Cases => new()
    {
        "public enum {|MIN001:E|} { } [Questions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum {|MIN002:L|} { } [Questions] public partial class C { [Score(\"q\")] internal partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, {|MIN104:B|} } [Questions] public partial record C { "
            + "[Score(\"q\")] public partial Score<L> Answer { get; } [Noul(\"q2\")] private partial Noul Other { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul {|MIN103:Answer|} { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul {|MIN102:Parse|} { get; } }",
        "[Questions] public partial record {|MIN105:R|}(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class {|MIN106:C|} { [Noul(\"a\")] public partial Noul IsUrgent { get; } "
            + "[Noul(\"b\", Key = \"is_urgent\")] public partial Noul Other { get; } }",
        "public interface IFoo { } [Questions({|MIN107:State = typeof(IFoo)|})] public partial class C { "
            + "[Noul(\"q\")] public partial Noul Answer { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul {|MIN102:Answer|} { get; set; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul {|MIN102:Answer|} { get; private set; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul {|MIN102:Answer|} { get; init; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public static partial Noul {|MIN102:Answer|} { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public virtual partial Noul {|MIN102:Answer|} { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public sealed override partial Noul {|MIN102:Answer|} { get; } }",
        "public class Base { public virtual Noul Answer => default; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public override partial Noul {|MIN102:Answer|} { get; } }",
        "public class Base { public Noul Answer => default; } "
            + "[Questions] public partial class C : Base { [Noul(\"q\")] public new partial Noul {|MIN102:Answer|} { get; } }",
        "[Questions] public partial class C { [Noul(\"q\")] public partial Noul {|MIN102:Answer|} { get; set; } "
            + "[Noul(\"q2\")] internal partial Noul Other { get; } }",
        // 'sealed' without 'override' is the user's own C# error, CS0238, which the stub cannot remove: it is still reported.
        "[Questions] public partial class C { [Noul(\"q\")] public sealed partial Noul {|CS0238:{|MIN102:Answer|}|} { get; } }",
        "[Questions] public partial class {|MIN105:C|} { [Noul(\"q\")] public required partial Noul {|MIN102:Answer|} { get; set; } }",
        "namespace Demo.Nested { public enum {|MIN001:E|} { } [Questions] public partial class C { "
            + "[Choice(\"q\")] public partial Choice<E> Answer { get; } } }",
        // MIN101 types a partial part can still complete get their stubs too: nested in partial types, generic, abstract.
        "public partial class Outer { [Questions] public partial class {|MIN101:Inner|} { [Noul(\"q\")] public partial Noul Answer { get; } } }",
        "[Questions] public partial class {|MIN101:Set|}<T> where T : struct, System.Enum { "
            + "[Choice(\"q\")] public partial Choice<T> Answer { get; } [Noul(\"q2\")] internal partial Noul Other { get; } }",
        "[Questions] public abstract partial class {|MIN101:Set|} { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "namespace Demo; public partial struct Outer<TKey> where TKey : notnull { internal partial record Middle { "
            + "[Questions] private partial class {|MIN101:Inner|}<T> { [Noul(\"q\")] public partial Noul Answer { get; set; } } } }",
        "[Questions] public static partial class {|MIN101:Set|} { [Noul(\"q\")] public static partial Noul Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task InvalidSet_ReportsOnlyTheAnalyzerError(string source) => AnalyzerVerifier.VerifyWithGeneratorAsync(source);
}
