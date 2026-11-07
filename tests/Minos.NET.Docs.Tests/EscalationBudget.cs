namespace Minos.Docs.Tests;

using Microsoft.Extensions.DependencyInjection;
using Minos.SystemOne;

public static class EscalationBudget
{
    #region Escalation_Budget
    // Each call gets 15 seconds from the moment the stage starts, and the hosted call needs about 300 ms of it.
    public static IServiceCollection AddTriage(IServiceCollection services, string localKey, string hostedKey)
    {
        services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
        services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
            .UseEscalation("hosted", options =>
            {
                options.PrimaryName = "local";
                options.Budget = TimeSpan.FromSeconds(15);
                options.ExpectedLatency = TimeSpan.FromMilliseconds(300);
            });
        return services;
    }

    // A host that took 2 seconds to prepare the message passes an absolute Deadline, so the budget counts from when the
    // work began. The questions that found no time for the hosted call are the ones routed NoTimeLeft.
    public static async Task<IReadOnlyList<string>> KeysWithoutTimeAsync(
        IDecisionClient client, QuestionSetDefinition definition, string message, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var request = new DecisionRequest(definition, DecisionContent.FromString(message)) { Deadline = startedAt + TimeSpan.FromSeconds(15) };
        var result = await client.EvaluateAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return [];
        }

        return result.Value.Path!.Routes.Where(route => route.Reason == RouteReason.NoTimeLeft).Select(route => route.Key).ToList();
    }
    #endregion
}
