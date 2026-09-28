namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>JEV108 and JEV109: attribute text marked Json = true.</summary>
public sealed class StructuredTextTests
{
    public static TheoryData<string> InvalidJsonCases => new()
    {
        "[JevQuestions] public partial class C { [Noul({|JEV108:\"{ \\\"q\\\": 1,}\"|}, Json = true)] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul({|JEV108:\"\\\"just text\\\"\"|}, Json = true)] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Choice({|JEV108:\"not json\"|}, Json = true)] public partial Choice<E> Answer { get; } } "
            + "public enum E { [Criteria(\"a\")] A }",
        "public enum E { [Criteria({|JEV108:\"{oops}\"|}, Json = true)] A } "
            + "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level({|JEV108:\"[1,]\"|}, Json = true)] A, [Level(\"b\")] B } "
            + "[JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(InvalidJsonCases))]
    public Task InvalidJson_ReportsError(string source) => AnalyzerVerifier.VerifyAsync(source);

    [Fact]
    public Task ValidJson_ReportsNothing()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "public enum E { [Criteria(\"{\\\"description\\\":\\\"a\\\"}\", Json = true)] A } "
                + "public enum L { [Level(\"[\\\"low\\\"]\", Json = true)] A, [Level(\"b\")] B } "
                + "[JevQuestions] public partial class C { "
                + "[Noul(\"{\\\"question\\\":\\\"Is it urgent?\\\"}\", Json = true)] public partial Noul N { get; } "
                + "[Choice(\"[\\\"Which team?\\\"]\", Json = true)] public partial Choice<E> Ch { get; } "
                + "[Score(\"q\")] public partial Score<L> S { get; } }");

    [Fact]
    public Task JsonFalse_TreatsTextAsPlain()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "[JevQuestions] public partial class C { [Noul(\"{ not json\", Json = false)] public partial Noul Answer { get; } }");

    public static TheoryData<string> JsonWithExamplesCases => new()
    {
        "public enum E { [Criteria(\"{}\", {|JEV109:Json = true|}, Examples = [\"x\"])] A } "
            + "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"[]\", NotFor = new[] { \"x\" }, {|JEV109:Json = true|})] A, [Level(\"b\")] B } "
            + "[JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        // Invalid JSON beside Examples: JEV109 alone, with no JEV108 piled onto the same attribute.
        "public enum E { [Criteria(\"{oops\", {|JEV109:Json = true|}, Examples = [\"x\"])] A } "
            + "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(JsonWithExamplesCases))]
    public Task JsonWithExamples_ReportsError(string source) => AnalyzerVerifier.VerifyAsync(source);

    [Fact]
    public Task JsonWithEmptyExamples_ReportsNothing()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "public enum E { [Criteria(\"{\\\"a\\\":1}\", Json = true, Examples = [])] A } "
                + "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");
}
