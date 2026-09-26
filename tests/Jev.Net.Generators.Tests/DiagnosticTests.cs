using Microsoft.CodeAnalysis;

namespace Jev.Net.Generators.Tests;

public sealed class DiagnosticTests
{
    public static TheoryData<string, string> Cases => new()
    {
        { "JEV101", "[JevQuestions] public class NotPartial { }" },
        { "JEV101", "[JevQuestions] public partial class Generic<T> { }" },
        { "JEV101", "public partial class Outer { [JevQuestions] public partial class Inner { } }" },
        { "JEV101", "[JevQuestions] public abstract partial class Base { }" },
        { "JEV102", "[JevQuestions] public partial class C { [Noul(\"q\")] public Noul Answer { get; } }" },
        { "JEV102", "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul Answer { get; set; } }" },
        { "JEV102", "[JevQuestions] public partial class C { [Noul(\"q\")] public static partial Noul Answer { get; } }" },
        { "JEV103", "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Noul Answer { get; } }" },
        { "JEV103", "[JevQuestions] public partial class C { [Noul(\"q\")][Choice(\"q\")] public partial Noul Answer { get; } }" },
        { "JEV103", "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<int> Answer { get; } }" },
        { "JEV104", "public enum L { [Level(\"a\")] A, B } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }" },
        { "JEV105", "[JevQuestions] public partial record R(int X) { [Noul(\"q\")] public partial Noul Answer { get; } }" },
        { "JEV106", "[JevQuestions] public partial class C { [Noul(\"a\")] public partial Noul IsUrgent { get; } [Noul(\"b\", Key = \"is_urgent\")] public partial Noul Other { get; } }" },
        { "JEV106", "public enum E { [Criteria(\"x\", Key = \"b\")] A, B } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void InvalidDeclaration_ReportsError_AndGeneratesNothing(string id, string source)
    {
        var driver = GeneratorHarness.Run("using Jev.Net;\n" + source, out _, out var diagnostics);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable),
        // which asserts exactly one element and returns it, not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var diagnostic = Assert.Single(diagnostics);
#pragma warning restore HLQ005
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }
}
