using System.Text.Json;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="JevCriterion"/>'s not-for texts under Native AOT; its other members run in the built-set checks.</summary>
internal static class JevCriterionChecks
{
    [Covers("ZeroAlloc.Jev.JevCriterion.WithNotFor(params System.ReadOnlySpan<string?> notFor) -> ZeroAlloc.Jev.JevCriterion!")]
    public static void NotForTextsAreSentWithTheDescription()
    {
        var built = JevQuestionSet.CreateBuilder()
            .Choice<Team>("team", "Which team should handle `message`?", out _, options => options
                .Describe(Team.Billing, JevCriterion.Text("Charges, invoices, refunds").WithNotFor("How much is Pro?", null))
                .Describe(Team.Account, "Login, profile, permissions"))
            .Build();

        if (!built.IsSuccess)
        {
            Program.Check(false, "a set with a not-for criterion builds: " + built.Error.Message);
            return;
        }

        using var questions = JsonDocument.Parse(built.Value.QuestionsUtf8.ToArray());
        var billing = questions.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("billing");
        Program.Check(
            string.Equals(billing.GetProperty("description").GetString(), "Charges, invoices, refunds", StringComparison.Ordinal)
                && billing.GetProperty("not_for").GetArrayLength() == 1
                && string.Equals(billing.GetProperty("not_for")[0].GetString(), "How much is Pro?", StringComparison.Ordinal),
            "JevCriterion.WithNotFor sends its texts, nulls skipped, beside the description under Native AOT");

        using var json = JsonDocument.Parse("""{"description":"Login"}""");
        var jsonCriterion = JevCriterion.Json(JevContent.FromJson(json.RootElement));
        Program.Check(
            SmokeAssert.Throws<InvalidOperationException>(() => jsonCriterion.WithNotFor("How much is Pro?")),
            "JevCriterion.WithNotFor on a JSON criterion throws under Native AOT");
    }
}
