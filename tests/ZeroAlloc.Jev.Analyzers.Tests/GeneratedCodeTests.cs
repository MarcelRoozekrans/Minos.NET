namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>
/// The rules decide what the generator emits, so they report wherever the declaration lives, generated code included.
/// Without them, an invalid set or enum in a <c>// &lt;auto-generated/&gt;</c> file would build to an unexplained
/// compiler error, such as CS0117 for the missing <c>QuestionsUtf8</c>.
/// </summary>
public sealed class GeneratedCodeTests
{
    [Fact]
    public Task EnumInGeneratedFile_ReportsItsRule() => AnalyzerVerifier.VerifyWithGeneratedFileAsync(
        "[JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, {|JEV104:B|} }");

    public static TheoryData<string> GeneratedSets => new()
    {
        "[JevQuestions] public partial class C { [Noul(\"q\")] public partial Noul {|JEV102:Answer|} { get; set; } }",
        "[JevQuestions] public partial class {|JEV101:Set|}<T> { [Noul(\"q\")] public partial Noul Answer { get; } }",
        "public enum {|JEV001:E|} { } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
        "public enum E { [Criteria(\"x\")] A, {|JEV006:B|} } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
    };

    [Theory]
    [MemberData(nameof(GeneratedSets))]
    public Task SetInGeneratedFile_ReportsItsRule(string generatedSource)
        => AnalyzerVerifier.VerifyWithGeneratedFileAsync(string.Empty, generatedSource);
}
