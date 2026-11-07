namespace Minos.Docs.Tests;

public static class EscalationFallback
{
    #region Escalation_FallbackPath
    // A response the first client answered has no Path, so there is nothing to list. After a fallback, Calls holds one
    // entry per client tried or skipped, in order, and the last one is the client that answered.
    public static async Task<IReadOnlyList<string>> CallsAsync(
        IDecisionClient client, QuestionSetDefinition definition, string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(message)), cancellationToken);
        if (result.IsFailure)
        {
            return [];
        }

        var calls = result.Value.Path?.Calls ?? [];
        return calls.Select(call => $"{call.ClientName}: {call.Status}").ToList();
    }
    #endregion
}
