using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Minos.Analyzers.Tests;

/// <summary>
/// Every diagnostic is local: it points inside the declaration of the symbol whose action reported it. The IDE
/// analyses the open document alone and shows only the local diagnostics it finds there, so a rule about an enum
/// must come from the enum's own action to be seen, and to be offered its code fix, while the enum's file is open.
/// </summary>
public sealed class LocalDiagnosticTests
{
    private const string SetFile = "Set.cs";
    private const string EnumFile = "Enum.cs";

    /// <summary>An enum rule, the enum it fires on and the set that uses it, and the text the diagnostic spans.</summary>
    public static TheoryData<string, string, string, string> EnumRules => new()
    {
        { "JEV006", "public enum E { [Criteria(\"x\")] A, B }", "[Choice(\"q\")] public partial Choice<E> Answer { get; }", "B" },
        { "JEV104", "public enum E { [Level(\"a\")] A, [Level(\"b\")] B, C }", "[Score(\"q\")] public partial Score<E> Answer { get; }", "C" },
        { "JEV001", "public enum E { }", "[Choice(\"q\")] public partial Choice<E> Answer { get; }", "E" },
        { "JEV002", "public enum E { }", "[Score(\"q\")] public partial Score<E> Answer { get; }", "E" },
        { "JEV005", "public enum E { [Level(\"a\")] A }", "[Score(\"q\")] public partial Score<E> Answer { get; }", "E" },
        { "JEV003", "public enum E { [Criteria(\" \")] A }", "[Choice(\"q\")] public partial Choice<E> Answer { get; }", "\" \"" },
        { "JEV003", "public enum E { [Level(\"a\")] A, [Level(\"\")] B }", "[Score(\"q\")] public partial Score<E> Answer { get; }", "\"\"" },
    };

    [Theory]
    [MemberData(nameof(EnumRules))]
    public async Task EnumRule_IsFoundAnalysingTheEnumDocumentAlone(string id, string enumSource, string question, string span)
    {
        var (compilation, enumTree, _) = await CompileAsync(enumSource, $"[Questions] public partial class Q {{ {question} }}");
        var analyzers = compilation.WithAnalyzers([new QuestionSetAnalyzer()]);

        var diagnostics = await analyzers.GetAnalyzerSemanticDiagnosticsAsync(
            compilation.GetSemanticModel(enumTree), filterSpan: null, CancellationToken.None);

        var diagnostic = SingleWithId(diagnostics, id);
        Assert.Equal(span, SpanText(diagnostic));
    }

    [Theory]
    [MemberData(nameof(EnumRules))]
    public async Task EnumRule_IsLocal(string id, string enumSource, string question, string span)
    {
        _ = span;
        var (compilation, enumTree, setTree) = await CompileAsync(enumSource, $"[Questions] public partial class Q {{ {question} }}");

        var result = await compilation.WithAnalyzers([new QuestionSetAnalyzer()]).GetAnalysisResultAsync(CancellationToken.None);

        // Nothing is non-local: the compilation-wide bucket, which no single-document analysis ever reads, is empty.
        Assert.Empty(result.CompilationDiagnostics.SelectMany(pair => pair.Value));
        Assert.Contains(Local(result, enumTree), diagnostic => string.Equals(diagnostic.Id, id, StringComparison.Ordinal));

        // The set's own document carries nothing about the enum.
        Assert.DoesNotContain(Local(result, setTree), diagnostic => string.Equals(diagnostic.Id, id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnumSharedByTwoSets_ReportsOncePerMember()
    {
        var (compilation, _, _) = await CompileAsync(
            "public enum E { [Criteria(\"x\")] A, B, C }",
            "[Questions] public partial class Q { [Choice(\"q1\")] public partial Choice<E> A1 { get; } "
                + "[Choice(\"q2\")] public partial Choice<E> A2 { get; } } "
                + "[Questions] public partial class R { [Choice(\"q3\")] public partial Choice<E> A3 { get; } }");

        var diagnostics = await compilation.WithAnalyzers([new QuestionSetAnalyzer()]).GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        var members = diagnostics
            .Where(diagnostic => string.Equals(diagnostic.Id, "JEV006", StringComparison.Ordinal))
            .Select(SpanText)
            .OrderBy(member => member, StringComparer.Ordinal);
        Assert.Equal(["B", "C"], members);
    }

    [Fact]
    public async Task EnumUsedAsChoiceAndScore_AppliesBothRuleSets()
    {
        var (compilation, _, _) = await CompileAsync(
            "public enum E { [Criteria(\"x\")] A, [Level(\"b\")] B }",
            "[Questions] public partial class Q { [Choice(\"q1\")] public partial Choice<E> A1 { get; } "
                + "[Score(\"q2\")] public partial Score<E> A2 { get; } }");

        var diagnostics = await compilation.WithAnalyzers([new QuestionSetAnalyzer()]).GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        var found = diagnostics
            .Select(diagnostic => diagnostic.Id + ":" + SpanText(diagnostic))
            .OrderBy(entry => entry, StringComparer.Ordinal);
        Assert.Equal(["JEV006:B", "JEV104:A"], found);
    }

    [Fact]
    public async Task EnumNoSetUses_ReportsNothing()
    {
        var (compilation, _, _) = await CompileAsync(
            "public enum E { }",
            "[Questions] public partial class Q { [Noul(\"q\")] public partial Noul Answer { get; } }");

        var diagnostics = await compilation.WithAnalyzers([new QuestionSetAnalyzer()]).GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    /// <summary>Compiles the enum and the set as two documents, so each can be analysed alone.</summary>
    private static async Task<(Compilation Compilation, SyntaxTree EnumTree, SyntaxTree SetTree)> CompileAsync(
        string enumSource, string setSource)
    {
        var enumTree = CompilationHelper.Parse("using Minos;\n" + enumSource, EnumFile);
        var setTree = CompilationHelper.Parse("using Minos;\n" + setSource, SetFile);
        return (await CompilationHelper.CompileAsync([enumTree, setTree]), enumTree, setTree);
    }

    /// <summary>The local diagnostics the analysis found in <paramref name="tree"/>.</summary>
    private static IEnumerable<Diagnostic> Local(AnalysisResult result, SyntaxTree tree)
        => result.SemanticDiagnostics.TryGetValue(tree, out var byAnalyzer)
            ? byAnalyzer.SelectMany(pair => pair.Value)
            : [];

    private static string SpanText(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single()
    // (#38 tracks this repo-wide false positive).
#pragma warning disable HLQ005
    private static Diagnostic SingleWithId(ImmutableArray<Diagnostic> diagnostics, string id)
        => Assert.Single(diagnostics, diagnostic => string.Equals(diagnostic.Id, id, StringComparison.Ordinal));
#pragma warning restore HLQ005
}
