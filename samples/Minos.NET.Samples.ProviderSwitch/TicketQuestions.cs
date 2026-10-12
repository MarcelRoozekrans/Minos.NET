namespace Minos.Samples.ProviderSwitch;

/// <summary>
/// The one question every ticket gets: which team handles it. It is a definition rather than a typed set, so the sample
/// can send it as a <see cref="DecisionRequest"/> and read the model that answered from the response.
/// </summary>
public static class TicketQuestions
{
    /// <summary>The key of the Choice question.</summary>
    public const string Team = "team";

    public static QuestionSetDefinition Definition { get; } = new(
        QuestionDefinition.Choice(
            Team,
            "Which team should handle this support ticket?",
            new OptionDefinition("billing", Criterion.Text("Invoices, payments, refunds and charges")),
            new OptionDefinition("technical", Criterion.Text("Errors, outages, crashes and how a feature works")),
            new OptionDefinition("account", Criterion.Text("Signing in, passwords, users and permissions")),
            new OptionDefinition("sales", Criterion.Text("Plans, quotes, discounts and buying more"))));
}
