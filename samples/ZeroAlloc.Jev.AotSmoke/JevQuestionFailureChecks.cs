namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="JevQuestionFailure"/>'s constructor under Native AOT, compared with a failure a broken set reports.</summary>
internal static class JevQuestionFailureChecks
{
    [Covers("ZeroAlloc.Jev.JevQuestionFailure.JevQuestionFailure(string! Rule, string? QuestionKey, string! Message) -> void")]
    public static void HandBuiltFailureEqualsTheReportedOne()
    {
        var built = JevQuestionSet.CreateBuilder().Choice("empty", "Which one?", out _, options => { }).Build();
        if (!built.IsFailure || built.Error.Failures.Count == 0)
        {
            Program.Check(false, "a set whose Choice has no options reports a JevQuestionFailure");
            return;
        }

        var reported = built.Error.Failures[0];
        var expected = new JevQuestionFailure("JEV001", "empty", reported.Message);

        Program.Check(
            expected.Equals(reported)
                && string.Equals(expected.Rule, "JEV001", StringComparison.Ordinal)
                && string.Equals(expected.QuestionKey, "empty", StringComparison.Ordinal),
            "a hand-built JevQuestionFailure equals the failure Build reports under Native AOT");
    }
}
