namespace Minos.Docs.Tests;

using Microsoft.Extensions.AI;
using Minos;
using Minos.Extensions.AI;

public static class EscalationChatTarget
{
    #region Escalation_ChatTarget
    // chat is any IChatClient. The unsure questions of primary are asked again of it, under the name "chat". The merged
    // response reports the weakest source among all its answers, so Verbalized means at least one answer is the model's
    // own estimate. Disposing client disposes primary, and target is disposed here too. It leaves chat open.
    public static async Task<ProbabilitySource?> SourceAsync(
        IChatClient chat, IDecisionClient primary, QuestionSetDefinition definition, string message, CancellationToken cancellationToken)
    {
        using var target = new ChatDecisionClient(chat);
        using var client = primary.AsBuilder().UseEscalation(target, options => options.TargetName = "chat").Build();
        var result = await client.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(message)), cancellationToken);
        return result.IsFailure ? null : result.Value.ProbabilitySource;
    }
    #endregion
}
