using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Jev.CodeFixes;

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
/// <para>Every test also sets <see cref="CodeFixTestBehaviors.SkipLocalDiagnosticCheck"/>: <see
/// cref="QuestionSetAnalyzer"/> reports every diagnostic from a <c>RegisterSymbolAction</c> callback, which the
/// testing framework always buckets as a "non-local", compilation-wide diagnostic regardless of the diagnostic
/// having a perfectly good in-source <see cref="Location"/> — the bucketing follows which analyzer callback
/// reported it, not where it points. Without this flag, <c>CodeFixTest</c> refuses to apply any fix to it at
/// all.</para>
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
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipLocalDiagnosticCheck,
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
                [System.Obsolete]
                [Criteria("B")]
                B,
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
            // The Formatter's default new line ("\r\n") separates the inserted using directive it added, even
            // though the rest of this single-line source has no line breaks of its own to match.
            FixedCode = "using ZeroAlloc.Jev;\r\n\r\npublic enum E { [ZeroAlloc.Jev.Criteria(\"x\")] A, [Criteria(\"B\")] B } "
                + "[ZeroAlloc.Jev.JevQuestions] public partial class Q { "
                + "[ZeroAlloc.Jev.Choice(\"q\")] public partial ZeroAlloc.Jev.Choice<E> Answer { get; } }",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipLocalDiagnosticCheck,
        };
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
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipLocalDiagnosticCheck,
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
