using System.Globalization;
using System.Text;

namespace ZeroAlloc.Jev.Analyzers.Tests;

/// <summary>JEV001–006: the Jev API's own rules, checked at compile time.</summary>
public sealed class ApiRuleTests
{
    // ---- JEV001 / JEV002: an empty enum ----

    [Fact]
    public Task EmptyChoiceEnum_ReportsOnTheEnum()
        => AnalyzerVerifier.VerifyAsync(
            "public enum {|JEV001:E|} { } [JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");

    [Fact]
    public Task EmptyChoiceEnum_SharedByTwoProperties_ReportsOnce()
        => AnalyzerVerifier.VerifyAsync(
            "public enum {|JEV001:E|} { } [JevQuestions] public partial class C { "
                + "[Choice(\"q1\")] public partial Choice<E> A1 { get; } [Choice(\"q2\")] public partial Choice<E> A2 { get; } }");

    [Fact]
    public Task EmptyScoreEnum_ReportsOnTheEnum()
        => AnalyzerVerifier.VerifyAsync(
            "public enum {|JEV002:L|} { } [JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }");

    [Fact]
    public async Task EmptyEnum_FromReferencedAssembly_ReportsOnTheProperty()
    {
        var reference = (await CompilationHelper.CompileAsync("namespace External; public enum Empty { }", "External"))
            .EmitToReference();

        await AnalyzerVerifier.VerifyAsync(
            "using External; [JevQuestions] public partial class C { "
                + "[Choice(\"q1\")] public partial Choice<Empty> {|JEV001:A1|} { get; } "
                + "[Score(\"q2\")] public partial Score<Empty> {|JEV002:A2|} { get; } }",
            reference);
    }

    [Fact]
    public Task NonEmptyEnums_ReportNothing()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "public enum E { [Criteria(\"x\")] A } public enum L { [Level(\"a\")] A, [Level(\"b\")] B } "
                + "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> A1 { get; } "
                + "[Score(\"q\")] public partial Score<L> A2 { get; } }");

    // ---- JEV003: empty text ----

    public static TheoryData<string> EmptyTextCases => new()
    {
        "[JevQuestions] public partial class C { [Noul({|JEV003:\"\"|})] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul({|JEV003:\"   \"|})] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul({|JEV003:instructions: \" \"|})] public partial Noul Answer { get; } }",
        "public enum E { [Criteria({|JEV003:\"\"|})] A } "
            + "[JevQuestions] public partial class C { [Choice({|JEV003:\" \"|})] public partial Choice<E> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, [Level({|JEV003:\"  \"|})] B } "
            + "[JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }",
        "public enum L { [Level(\"a\")] A, [Level({|JEV003:\"\"|})] B } [JevQuestions] public partial class C { "
            + "[Score(\"q1\")] public partial Score<L> A1 { get; } [Score(\"q2\")] public partial Score<L> A2 { get; } }",
    };

    [Theory]
    [MemberData(nameof(EmptyTextCases))]
    public Task EmptyText_ReportsWarning(string source) => AnalyzerVerifier.VerifyAsync(source);

    public static TheoryData<string> NonEmptyTextCases => new()
    {
        "[JevQuestions] public partial class C { [Noul(\"Is it urgent?\")] public partial Noul Answer { get; } }",
        "[JevQuestions] public partial class C { [Noul(null)] public partial Noul Answer { get; } }",
        "public enum E { [Criteria(null)] A, [Criteria(\"b\")] B } public enum L { [Level(null)] A, [Level(\"b\")] B } "
            + "[JevQuestions] public partial class C { [Choice(null)] public partial Choice<E> A1 { get; } "
            + "[Score(null)] public partial Score<L> A2 { get; } }",
    };

    [Theory]
    [MemberData(nameof(NonEmptyTextCases))]
    public Task NonEmptyOrNullText_ReportsNothing(string source) => AnalyzerVerifier.VerifyNoDiagnosticsAsync(source);

    // ---- JEV004: a backticked name the State type does not have ----

    private const string State = """
        public sealed class TicketState
        {
            public string? CustomerTier { get; set; }
            public int OpenTickets;
            [System.Text.Json.Serialization.JsonPropertyName("sla")] public string? ServiceLevel { get; set; }
            public static int StaticCount { get; set; }
            internal string? Hidden { get; set; }
        }

        """;

    public static TheoryData<string> MatchingTokens => new()
    {
        "CustomerTier",
        "customerTier",
        "customer_tier",
        "customer-tier",
        "CUSTOMER_TIER",
        "OpenTickets",
        "open_tickets",
        "sla",
        "ServiceLevel",
        "service-level",
    };

    [Theory]
    [MemberData(nameof(MatchingTokens))]
    public Task StateReference_MatchingToken_ReportsNothing(string token)
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            State + "[JevQuestions(State = typeof(TicketState))] public partial class C { "
                + $"[Noul(\"Is `{token}` high?\")] public partial Noul Answer {{ get; }} }}");

    public static TheoryData<string> SkippedInstructions => new()
    {
        "Is `ticket.subject` set?",
        "Is `a b` set?",
        "Is `1st` set?",
        "Is `` empty?",
        "Is `unclosed set?",
        "No backticks at all",
    };

    [Theory]
    [MemberData(nameof(SkippedInstructions))]
    public Task StateReference_NonIdentifierToken_IsSkipped(string instructions)
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            State + "[JevQuestions(State = typeof(TicketState))] public partial class C { "
                + $"[Noul(\"{instructions}\")] public partial Noul Answer {{ get; }} }}");

    [Fact]
    public Task StateReference_Typo_ReportsOnTheInstructions()
        => AnalyzerVerifier.VerifyAsync(
            State + "[JevQuestions(State = typeof(TicketState))] public partial class C { "
                + "[Noul({|JEV004:\"Is `CustomerTeir` gold and `open_tickets` high?\"|})] public partial Noul Answer { get; } }");

    [Fact]
    public Task StateReference_StaticOrNonPublicMember_Reports()
        => AnalyzerVerifier.VerifyAsync(
            State + "[JevQuestions(State = typeof(TicketState))] public partial class C { "
                + "[Noul({|JEV004:\"Is `StaticCount` set?\"|})] public partial Noul A1 { get; } "
                + "[Noul({|JEV004:\"Is `hidden` set?\"|})] public partial Noul A2 { get; } }");

    [Fact]
    public Task StateReference_InheritedMember_Matches()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "public class BaseState { public int Priority { get; set; } } public sealed class DerivedState : BaseState { } "
                + "[JevQuestions(State = typeof(DerivedState))] public partial class C { "
                + "[Noul(\"Is `priority` high?\")] public partial Noul Answer { get; } }");

    [Fact]
    public Task StateReference_ChoiceAndScoreInstructions_AreChecked()
        => AnalyzerVerifier.VerifyAsync(
            State + "public enum E { [Criteria(\"x\")] A } public enum L { [Level(\"a\")] A, [Level(\"b\")] B } "
                + "[JevQuestions(State = typeof(TicketState))] public partial class C { "
                + "[Choice({|JEV004:\"`nope`\"|})] public partial Choice<E> A1 { get; } "
                + "[Score({|JEV004:\"`nada`\"|})] public partial Score<L> A2 { get; } }");

    [Fact]
    public Task StateReference_WithoutState_ReportsNothing()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "[JevQuestions] public partial class C { [Noul(\"Is `anything` set?\")] public partial Noul Answer { get; } }");

    [Fact]
    public Task StateReference_ArrayState_ReportsNothing()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            State + "[JevQuestions(State = typeof(TicketState[]))] public partial class C { "
                + "[Noul(\"Is `anything` set?\")] public partial Noul Answer { get; } }");

    // ---- JEV005: outside the API sketch's option and level guidance ----

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public Task ScoreLevelCount_OutsideTwoToTen_Warns(int levels, bool reports)
    {
        var members = Members(levels, index => $"[Level(\"level {index}\")] M{index}");
        var enumName = reports ? "{|JEV005:L|}" : "L";
        var source = $"public enum {enumName} {{ {members} }} "
            + "[JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }";

        return reports ? AnalyzerVerifier.VerifyAsync(source) : AnalyzerVerifier.VerifyNoDiagnosticsAsync(source);
    }

    [Theory]
    [InlineData(255, false)]
    [InlineData(256, true)]
    public Task ChoiceOptionCount_OverTwoHundredFiftyFive_Warns(int options, bool reports)
    {
        var members = Members(options, index => $"[Criteria(\"option {index}\")] M{index}");
        var enumName = reports ? "{|JEV005:E|}" : "E";
        var source = $"public enum {enumName} {{ {members} }} "
            + "[JevQuestions] public partial class C { [Choice(\"q\")] public partial Choice<E> Answer { get; } }";

        return reports ? AnalyzerVerifier.VerifyAsync(source) : AnalyzerVerifier.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public Task ScoreLevelCount_IgnoresAliases()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "public enum L { [Level(\"a\")] A, [Level(\"b\")] B, Alias = A } "
                + "[JevQuestions] public partial class C { [Score(\"q\")] public partial Score<L> Answer { get; } }");

    // ---- JEV006: a Choice option without [Criteria] ----

    [Fact]
    public Task ChoiceMemberWithoutCriteria_ReportsPerBareMember()
        => AnalyzerVerifier.VerifyAsync(
            "public enum E { [Criteria(\"x\")] A, {|JEV006:B|}, {|JEV006:C|} } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");

    [Fact]
    public Task ChoiceMemberWithoutCriteria_SharedEnum_ReportsOnce()
        => AnalyzerVerifier.VerifyAsync(
            "public enum E { [Criteria(\"x\")] A, {|JEV006:B|} } [JevQuestions] public partial class Q { "
                + "[Choice(\"q1\")] public partial Choice<E> A1 { get; } [Choice(\"q2\")] public partial Choice<E> A2 { get; } } "
                + "[JevQuestions] public partial class R { [Choice(\"q3\")] public partial Choice<E> A3 { get; } }");

    [Fact]
    public async Task ChoiceMemberWithoutCriteria_FromReferencedAssembly_ReportsEachMemberOnTheProperty()
    {
        var reference = (await CompilationHelper.CompileAsync("namespace External; public enum Bare { A, B }", "External"))
            .EmitToReference();

        await AnalyzerVerifier.VerifyAsync(
            "using External; [JevQuestions] public partial class C { "
                + "[Choice(\"q\")] public partial Choice<Bare> {|JEV006:{|JEV006:Answer|}|} { get; } }",
            reference);
    }

    [Fact]
    public Task ChoiceMembersWithCriteria_ReportNothing()
        => AnalyzerVerifier.VerifyNoDiagnosticsAsync(
            "public enum E { [Criteria(\"x\")] A, [Criteria(\"y\", Key = \"why\")] B } "
                + "[JevQuestions] public partial class Q { [Choice(\"q\")] public partial Choice<E> Answer { get; } }");

    private static string Members(int count, Func<int, string> member)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < count; index++)
        {
            builder.Append(member(index)).Append(", ");
        }

        return builder.ToString();
    }
}
