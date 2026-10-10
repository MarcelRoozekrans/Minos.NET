using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary><see cref="Criterion"/>'s not-for texts under Native AOT; its other members run in the built-set checks.</summary>
internal static class CriterionChecks
{
    [Covers("Minos.Criterion.WithNotFor(params System.ReadOnlySpan<string?> notFor) -> Minos.Criterion!")]
    public static async Task NotForTextsAreSentWithTheDescription()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice<Team>("team", "Which team should handle `message`?", out _, options => options
                .Describe(Team.Billing, Criterion.Text("Charges, invoices, refunds").WithNotFor("How much is Pro?", null))
                .Describe(Team.Account, "Login, profile, permissions"))
            .Build();

        if (!built.IsSuccess)
        {
            Program.Check(false, "a set with a not-for criterion builds: " + built.Error.Message);
            return;
        }

        using var questions = await CapturingHandler.QuestionsSentAsync(built.Value, Program.CredentialsResponse).ConfigureAwait(false);
        var billing = questions.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("billing");
        Program.Check(
            string.Equals(billing.GetProperty("description").GetString(), "Charges, invoices, refunds", StringComparison.Ordinal)
                && billing.GetProperty("not_for").GetArrayLength() == 1
                && string.Equals(billing.GetProperty("not_for")[0].GetString(), "How much is Pro?", StringComparison.Ordinal),
            "Criterion.WithNotFor sends its texts, nulls skipped, beside the description under Native AOT");

        using var json = JsonDocument.Parse("""{"description":"Login"}""");
        var jsonCriterion = Criterion.Json(DecisionContent.FromJson(json.RootElement));
        Program.Check(
            SmokeAssert.Throws<InvalidOperationException>(() => jsonCriterion.WithNotFor("How much is Pro?")),
            "Criterion.WithNotFor on a JSON criterion throws under Native AOT");
    }
}
