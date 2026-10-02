namespace ZeroAlloc.Jev.Docs.Tests;

#region GettingStarted_Questions
using ZeroAlloc.Jev;

public enum SupportTeam
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,

    [Criteria("Pricing, upgrades and new accounts")]
    Sales,
}

// Two questions about one message. The generator writes the question JSON at compile time, and the
// properties hold the typed answers once Jev has replied.
[JevQuestions]
public partial record TicketCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which team should handle this?")]
    public partial Choice<SupportTeam> Team { get; }
}
#endregion

public static class GettingStartedClients
{
    #region GettingStarted_Clients
    // TypeSafe is the default provider. With no ApiKey set, the client reads TYPESAFE_API_KEY.
    // JevClient is disposable and meant to be long-lived: create it once, share it, and dispose it at shutdown.
    public static async Task<string> ViaTypeSafeAsync(string message, CancellationToken ct)
    {
        using var jev = new JevClient(new JevClientOptions());
        return await GettingStartedEvaluation.TriageAsync(jev, message, ct);
    }

    // OpenRouter: name the provider. With no ApiKey set, the client reads OPENROUTER_API_KEY.
    public static async Task<string> ViaOpenRouterAsync(string message, CancellationToken ct)
    {
        using var jev = new JevClient(new JevClientOptions { Provider = JevProvider.OpenRouter });
        return await GettingStartedEvaluation.TriageAsync(jev, message, ct);
    }
    #endregion
}

public static class GettingStartedEvaluation
{
    #region GettingStarted_Evaluate
    public static async Task<string> TriageAsync(IJevClient jev, string message, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync<TicketCheck>(message, ct);

        // Every outcome comes back as a value, so check for failure before reading the answers: a network
        // error, a rejected key and an unreadable response all arrive here, with a Kind and a Message.
        if (result.IsFailure)
        {
            return $"Jev failed, {result.Error.Kind}: {result.Error.Message}";
        }

        var check = result.Value;

        // IsUrgent.Probability is the chance that the answer is yes, and Value is that chance at 0.5 or more.
        // Team.Value is the option Jev picked, and Team.Confidence says how far to trust the pick.
        return $"{(check.IsUrgent.Value ? "urgent" : "not urgent")}, for {check.Team.Value}";
    }
    #endregion
}
