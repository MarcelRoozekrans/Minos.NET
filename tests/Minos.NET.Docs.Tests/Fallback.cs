namespace Minos.Docs.Tests;

using Microsoft.Extensions.DependencyInjection;
using Minos.SystemOne;

public static class FallbackPipeline
{
    #region Pipeline_Fallback
    // The default client answers first. When it fails with a provider error, "clef" is asked, then "hosted". A step is
    // skipped when less than its expected latency is left before the deadline.
    public static IServiceCollection AddFallbackDecisions(IServiceCollection services, string localKey, string clefKey, string hostedKey)
    {
        services.AddSystemOneDecisionClient("clef", options => options.ApiKey = clefKey);
        services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
        services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
            .UseFallback(
                new KeyedFallbackStep("clef", TimeSpan.FromMilliseconds(300)),
                new KeyedFallbackStep("hosted", TimeSpan.FromSeconds(2)));
        return services;
    }

    // Path says which clients were tried and which one answered each question.
    public static async Task<(ShopIntent Intent, string AnsweredBy)> ReadAsync(IDecisionClient client, string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync(new DecisionRequest(ChatMessage.Definition, DecisionContent.FromString(message)), cancellationToken);
        if (result.IsFailure)
        {
            return (ShopIntent.Other, "none");
        }

        var response = result.Value;
        return (response.As<ChatMessage>().Intent.Value, response.Path?.Routes[0].AnsweredBy ?? "primary");
    }
    #endregion
}
