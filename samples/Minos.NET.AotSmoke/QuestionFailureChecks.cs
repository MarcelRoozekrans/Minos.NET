namespace Minos.AotSmoke;

/// <summary><see cref="QuestionFailure"/>'s constructor under Native AOT, compared with a failure a broken set reports.</summary>
internal static class QuestionFailureChecks
{
    [Covers("Minos.QuestionFailure.QuestionFailure(string! Rule, string? QuestionKey, string! Message) -> void")]
    public static void HandBuiltFailureEqualsTheReportedOne()
    {
        var built = QuestionSet.CreateBuilder().Choice("empty", "Which one?", out _, options => { }).Build();
        if (!built.IsFailure || built.Error.Failures.Count == 0)
        {
            Program.Check(false, "a set whose Choice has no options reports a QuestionFailure");
            return;
        }

        var reported = built.Error.Failures[0];
        var expected = new QuestionFailure("MIN001", "empty", reported.Message);

        Program.Check(
            expected.Equals(reported)
                && string.Equals(expected.Rule, "MIN001", StringComparison.Ordinal)
                && string.Equals(expected.QuestionKey, "empty", StringComparison.Ordinal),
            "a hand-built QuestionFailure equals the failure Build reports under Native AOT");
    }
}
