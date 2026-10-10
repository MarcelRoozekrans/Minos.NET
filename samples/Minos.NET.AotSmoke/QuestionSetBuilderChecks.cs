using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// The <see cref="QuestionSetBuilder"/> overloads and option builders the other checks leave out: a Noul with
/// <see cref="NoulCriteriaBuilder"/> criteria, and a keyed Choice whose options <see cref="KeyedChoiceOptionsBuilder"/>
/// adds without descriptions.
/// </summary>
internal static class QuestionSetBuilderChecks
{
    [Covers("Minos.QuestionSetBuilder.Noul(string! key, Minos.DecisionContent instructions, out Minos.NoulHandle question, System.Action<Minos.NoulCriteriaBuilder!>! configure) -> Minos.QuestionSetBuilder!")]
    [Covers("Minos.NoulCriteriaBuilder.WhenTrue(Minos.DecisionContent description) -> Minos.NoulCriteriaBuilder!")]
    [Covers("Minos.NoulCriteriaBuilder.WhenFalse(Minos.DecisionContent description) -> Minos.NoulCriteriaBuilder!")]
    [Covers("Minos.KeyedChoiceOptionsBuilder.Option(string! key) -> Minos.KeyedChoiceOptionsBuilder!")]
    public static async Task NoulCriteriaAndUndescribedKeyedOptionsAreSent()
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("requests_credentials", "Does `message` ask for a credential?", out _, criteria => criteria
                .WhenTrue("It asks for a password, a code or a key")
                .WhenFalse("It asks for nothing secret"))
            .Choice("product", "Which product is `message` about?", out _, options => options
                .Option("pro-plan")
                .Option("team-plan"))
            .Build();

        if (!built.IsSuccess)
        {
            Program.Check(false, "a set with Noul criteria and undescribed keyed options builds: " + built.Error.Message);
            return;
        }

        using var questions = await CapturingHandler.QuestionsSentAsync(built.Value, Program.CredentialsResponse).ConfigureAwait(false);
        var noul = questions.RootElement.GetProperty("requests_credentials").GetProperty("criteria");
        var product = questions.RootElement.GetProperty("product").GetProperty("criteria");
        Program.Check(
            string.Equals(noul.GetProperty("true").GetString(), "It asks for a password, a code or a key", StringComparison.Ordinal)
                && string.Equals(noul.GetProperty("false").GetString(), "It asks for nothing secret", StringComparison.Ordinal),
            "NoulCriteriaBuilder's WhenTrue and WhenFalse are sent as the Noul's criteria under Native AOT");
        Program.Check(
            product.GetProperty("pro-plan").ValueKind == JsonValueKind.Null
                && product.GetProperty("team-plan").ValueKind == JsonValueKind.Null,
            "KeyedChoiceOptionsBuilder.Option(key) sends an option without a description under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => QuestionSet.CreateBuilder()
                .Noul("requests_credentials", "Does `message` ask for a credential?", out _, criteria => criteria.WhenTrue(default))),
            "NoulCriteriaBuilder.WhenTrue rejects uninitialized content under Native AOT");
    }
}
