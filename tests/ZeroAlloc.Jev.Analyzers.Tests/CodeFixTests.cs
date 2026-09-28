extern alias CodeFixes;

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using CodeFixes::ZeroAlloc.Jev.CodeFixes;

namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>
/// <see cref="AddDescriptionCodeFixProvider"/>: adds <c>[Criteria("…")]</c> (JEV006) or <c>[Level("…")]</c>
/// (JEV104) to an enum member, the text derived from the member name by <see cref="WordSplitter"/>.
/// </summary>
/// <remarks>
/// The generator does not run in these tests (only <see cref="QuestionSetAnalyzer"/> and the code fix are under
/// test), so a question set's partial properties stay unimplemented in both the original and the fixed source;
/// <see cref="CompilerDiagnostics.None"/> ignores that expected compiler error consistently in every case here,
/// the same way <c>AnalyzerVerifier.VerifyAsync</c> does for the analyzer-only tests.
///
/// <para>The testing framework's local-diagnostic check stays on: it refuses to fix a diagnostic that is not local,
/// that is, one that does not point inside the declaration of the symbol whose action reported it. That is what the
/// IDE shows for the open document, so the check proves the fix is offered there. Each JEV006 and JEV104 here comes
/// from the enum's own symbol action and is local; <c>LocalDiagnosticTests</c> checks the same directly.</para>
/// </remarks>
public sealed class CodeFixTests
{
    [Fact]
    public Task MissingCriteria_AddsAttributeFromMemberName()
        => VerifyAsync(
            "public enum E { [Criteria(\"x\")] A, {|JEV006:Billing|} } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
            "public enum E { [Criteria(\"x\")] A, [Criteria(\"Billing\")] Billing } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");

    [Fact]
    public Task MissingCriteria_LowercasesLaterWords()
        => VerifyAsync(
            "public enum E { [Criteria(\"x\")] A, {|JEV006:NeedsAttention|} } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
            "public enum E { [Criteria(\"x\")] A, [Criteria(\"Needs attention\")] NeedsAttention } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");

    [Fact]
    public Task MissingCriteria_KeepsAnAcronymUppercase()
        => VerifyAsync(
            "public enum E { [Criteria(\"x\")] A, {|JEV006:HTTPError|} } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
            "public enum E { [Criteria(\"x\")] A, [Criteria(\"HTTP error\")] HTTPError } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");

    [Fact]
    public Task MissingLevel_AddsAttributeFromMemberName()
        => VerifyAsync(
            "public enum L { [Level(\"a\")] A, {|JEV104:NeedsReview|} } "
                + "[JevQuestions] public partial class Q { [Score(\"q\")] public partial Score<L> Answer { get; } }",
            "public enum L { [Level(\"a\")] A, [Level(\"Needs review\")] NeedsReview } "
                + "[JevQuestions] public partial class Q { [Score(\"q\")] public partial Score<L> Answer { get; } }");

    [Fact]
    public async Task MissingLevel_OnPropertyFromReferencedAssembly_OffersNoFix()
    {
        // JEV104 lands on the property, not an enum member, when the enum has no source syntax; there is no
        // enum member declaration to attach [Level] to, so the provider must not offer a fix, and the fixed
        // state is identical to the original: no code action was applicable.
        var reference = (await CompilationHelper.CompileAsync("namespace External; public enum Levels { A, B }", "External"))
            .EmitToReference();

        // Neither enum member has [Level], so JEV104 fires once per member (Levels.A and Levels.B), both at the
        // same property location: the nested markup matches AnalyzerVerifier's own convention for this case.
        var source = "using ZeroAlloc.Jev; using External; [JevQuestions] public partial class Q { "
            + "[Score(\"q\")] public partial Score<Levels> {|JEV104:{|JEV104:Answer|}|} { get; } }";
        var test = new CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        AddReferences(test, reference);

        await test.RunAsync();
    }

    [Fact]
    public Task FixAll_InDocument_FixesEveryMember()
        => VerifyAsync(
            "public enum E { [Criteria(\"x\")] A, {|JEV006:B|}, {|JEV006:C|}, {|JEV006:HTTPError|} } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
            "public enum E { [Criteria(\"x\")] A, [Criteria(\"B\")] B, [Criteria(\"C\")] C, [Criteria(\"HTTP error\")] HTTPError } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }",
            batch: true);

    [Fact]
    public Task ExistingAttributesAndTrivia_AreKept()
        => VerifyAsync(
            """
            public enum E
            {
                [Criteria("x")] A,
                // A note about B
                [System.Obsolete] {|JEV006:B|},
            }
            [JevQuestions] public partial class Q { [Choice("q")] public partial Choice<E> Answer { get; } }
            """,
            """
            public enum E
            {
                [Criteria("x")] A,
                // A note about B
                [System.Obsolete] [Criteria("B")] B,
            }
            [JevQuestions] public partial class Q { [Choice("q")] public partial Choice<E> Answer { get; } }
            """);

    [Fact]
    public Task MissingUsing_IsAdded()
    {
        var test = new CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier>
        {
            TestCode = "public enum E { [ZeroAlloc.Jev.Criteria(\"x\")] A, {|JEV006:B|} } "
                + "[ZeroAlloc.Jev.JevQuestions] public partial class Q { "
                + "[ZeroAlloc.Jev.Choice(\"q\")] public partial ZeroAlloc.Jev.Choice<E> Answer { get; } }",
            // A single line has no line break of its own to follow and there is no .editorconfig, so the using ends
            // with the workspace's default new line, "\r\n".
            FixedCode = "using ZeroAlloc.Jev;\r\n\r\npublic enum E { [ZeroAlloc.Jev.Criteria(\"x\")] A, [Criteria(\"B\")] B } "
                + "[ZeroAlloc.Jev.JevQuestions] public partial class Q { "
                + "[ZeroAlloc.Jev.Choice(\"q\")] public partial ZeroAlloc.Jev.Choice<E> Answer { get; } }",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        AddJevReference(test);
        return test.RunAsync();
    }

    [Fact]
    public Task AliasUsing_FixCompiles()
        => VerifyCompilesAsync(
            [
                """
                using Jev = ZeroAlloc.Jev;
                public enum E { [Jev.Criteria("x")] A, {|JEV006:B|} }
                [Jev.JevQuestions] public partial class Q { [Jev.Choice("q")] public partial Jev.Choice<E> {|CS9248:Answer|} { get; } }
                """,
            ],
            [
                """
                using Jev = ZeroAlloc.Jev;
                public enum E { [Jev.Criteria("x")] A, [Jev.Criteria("B")] B }
                [Jev.JevQuestions] public partial class Q { [Jev.Choice("q")] public partial Jev.Choice<E> {|CS9248:Answer|} { get; } }
                """,
            ]);

    [Fact]
    public Task NamespaceScopedUsing_AddsNoTopLevelUsing()
        => VerifyCompilesAsync(
            [
                """
                namespace N
                {
                    using ZeroAlloc.Jev;

                    public enum E { [Criteria("x")] A, {|JEV006:B|} }
                    [JevQuestions] public partial class Q { [Choice("q")] public partial Choice<E> {|CS9248:Answer|} { get; } }
                }
                """,
            ],
            [
                """
                namespace N
                {
                    using ZeroAlloc.Jev;

                    public enum E { [Criteria("x")] A, [Criteria("B")] B }
                    [JevQuestions] public partial class Q { [Choice("q")] public partial Choice<E> {|CS9248:Answer|} { get; } }
                }
                """,
            ]);

    [Fact]
    public Task GlobalUsingInAnotherFile_AddsNoUsing()
        => VerifyCompilesAsync(
            [
                "global using ZeroAlloc.Jev;",
                """
                namespace N;

                public enum E { [Criteria("x")] A, {|JEV006:B|} }
                [JevQuestions] public partial class Q { [Choice("q")] public partial Choice<E> {|CS9248:Answer|} { get; } }
                """,
            ],
            [
                "global using ZeroAlloc.Jev;",
                """
                namespace N;

                public enum E { [Criteria("x")] A, [Criteria("B")] B }
                [JevQuestions] public partial class Q { [Choice("q")] public partial Choice<E> {|CS9248:Answer|} { get; } }
                """,
            ]);

    [Fact]
    public Task LfFileWithoutUsing_StaysLf()
    {
        // Built with explicit "\n" rather than a raw string literal, whose line breaks follow this file's own.
        const string Set = "[ZeroAlloc.Jev.JevQuestions]\npublic partial class Q\n{\n"
            + "    [ZeroAlloc.Jev.Choice(\"q\")] public partial ZeroAlloc.Jev.Choice<E> {|CS9248:Answer|} { get; }\n}\n";
        const string Source = "public enum E\n{\n    [ZeroAlloc.Jev.Criteria(\"x\")] A,\n"
            + "    {|JEV006:B|},\n    {|JEV006:NeedsAttention|},\n}\n\n" + Set;
        const string Fixed = "using ZeroAlloc.Jev;\n\npublic enum E\n{\n    [ZeroAlloc.Jev.Criteria(\"x\")] A,\n"
            + "    [Criteria(\"B\")] B,\n    [Criteria(\"Needs attention\")] NeedsAttention,\n}\n\n" + Set;

        return VerifyCompilesAsync([Source], [Fixed], batchFixedSources: [Fixed]);
    }

    [Fact]
    public Task CrlfFile_AttributeOnItsOwnLine_KeepsTheLayout()
    {
        // A member whose attribute list has a line of its own gets the new list on a line of its own too.
        const string Set = "[JevQuestions]\r\npublic partial class Q\r\n{\r\n"
            + "    [Choice(\"q\")] public partial Choice<E> {|CS9248:Answer|} { get; }\r\n}\r\n";
        return VerifyCompilesAsync(
            ["using ZeroAlloc.Jev;\r\n\r\npublic enum E\r\n{\r\n    [Criteria(\"x\")] A,\r\n    [System.Obsolete]\r\n    {|JEV006:B|},\r\n}\r\n\r\n" + Set],
            ["using ZeroAlloc.Jev;\r\n\r\npublic enum E\r\n{\r\n    [Criteria(\"x\")] A,\r\n    [System.Obsolete]\r\n    [Criteria(\"B\")]\r\n    B,\r\n}\r\n\r\n" + Set]);
    }

    [Fact]
    public Task EditorConfigEndOfLine_SetsTheNewLine()
    {
        // A single line has no line break of its own to follow, so only .editorconfig can ask for "\n".
        var test = new CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier>
        {
            TestCode = "public enum E { [ZeroAlloc.Jev.Criteria(\"x\")] A, {|JEV006:B|} } "
                + "[ZeroAlloc.Jev.JevQuestions] public partial class Q { "
                + "[ZeroAlloc.Jev.Choice(\"q\")] public partial ZeroAlloc.Jev.Choice<E> Answer { get; } }",
            FixedCode = "using ZeroAlloc.Jev;\n\npublic enum E { [ZeroAlloc.Jev.Criteria(\"x\")] A, [Criteria(\"B\")] B } "
                + "[ZeroAlloc.Jev.JevQuestions] public partial class Q { "
                + "[ZeroAlloc.Jev.Choice(\"q\")] public partial ZeroAlloc.Jev.Choice<E> Answer { get; } }",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\nend_of_line = lf\n"));
        AddJevReference(test);
        return test.RunAsync();
    }

    /// <summary>
    /// Verifies a fix with the compiler's errors checked, so the fixed code must compile: each source marks the one
    /// expected error, CS9248 for the partial property the generator would implement. That the fixed code reports no
    /// JEV006 is the testing framework's own check.
    /// </summary>
    private static Task VerifyCompilesAsync(string[] sources, string[] fixedSources, string[]? batchFixedSources = null)
    {
        var test = new CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            CompilerDiagnostics = CompilerDiagnostics.Errors,
        };

        for (var index = 0; index < sources.Length; index++)
        {
            var path = $"/0/Test{index}.cs";
            test.TestState.Sources.Add((path, sources[index]));
            test.FixedState.Sources.Add((path, fixedSources[index]));
            if (batchFixedSources is not null)
            {
                test.BatchFixedState.Sources.Add((path, batchFixedSources[index]));
            }
        }

        AddJevReference(test);
        return test.RunAsync();
    }

    private static Task VerifyAsync(string source, string fixedSource, bool batch = false)
    {
        var fixedCode = "using ZeroAlloc.Jev;\n" + fixedSource;
        var test = new CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier>
        {
            TestCode = "using ZeroAlloc.Jev;\n" + source,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        if (batch)
        {
            test.BatchFixedCode = fixedCode;
        }

        AddJevReference(test);
        return test.RunAsync();
    }

    private static void AddJevReference(CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier> test)
        => test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(JevQuestionsAttribute).Assembly.Location));

    private static void AddReferences(
        CSharpCodeFixTest<QuestionSetAnalyzer, AddDescriptionCodeFixProvider, DefaultVerifier> test,
        params MetadataReference[] additionalReferences)
    {
        AddJevReference(test);
        foreach (var reference in additionalReferences)
        {
            test.TestState.AdditionalReferences.Add(reference);
        }
    }
}
