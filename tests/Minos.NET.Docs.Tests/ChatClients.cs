namespace Minos.Docs.Tests;

#region ChatClients_AsDecisionClient
using Microsoft.Extensions.AI;
using Minos;
using Minos.Extensions.AI;

public enum ChatTicketTeam
{
    [Criterion("Invoices, payments, refunds and charges")]
    Billing,

    [Criterion("Errors, crashes and how a feature works")]
    Technical,
}

[Questions]
public partial record ChatTicketCheck
{
    [Noul("Does the customer ask for money back?")]
    public partial Noul WantsRefund { get; }

    [Choice("Which team should handle this ticket?")]
    public partial Choice<ChatTicketTeam> Team { get; }
}

public static class ChatDecisions
{
    // chat is any IChatClient: OpenAI, OpenRouter, Azure, Ollama or another. The decision client does not dispose it,
    // so the app can keep using it for chat.
    public static async Task<string> TriageAsync(IChatClient chat, string ticket, CancellationToken cancellationToken)
    {
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Auto });
        var result = await client.EvaluateAsync<ChatTicketCheck>(ticket, cancellationToken);
        return result.IsFailure
            ? $"{result.Error.Kind}: {result.Error.Message}"
            : $"team: {result.Value.Team.Value}, from {result.Value.Team.ProbabilitySource}";
    }
}
#endregion

#region ChatClients_ReadSource
public static class TicketRouting
{
    // A verbalized probability is the model's own estimate, not a measured one, so such an answer never acts alone.
    public static bool CanRouteAlone(Choice<ChatTicketTeam> team, double threshold)
        => team.ProbabilitySource != ProbabilitySource.Verbalized && team.Confidence >= threshold;
}
#endregion
