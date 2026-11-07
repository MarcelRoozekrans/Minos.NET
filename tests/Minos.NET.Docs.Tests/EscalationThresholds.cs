namespace Minos.Docs.Tests;

using Microsoft.Extensions.DependencyInjection;
using Minos.SystemOne;

public static class EscalationThresholds
{
    #region Escalation_Thresholds
    // A question the local model is less than 0.6 sure of is asked again of "hosted", unless it has a threshold of its
    // own. The keys are the question keys of the Banking77 set, and a key the set does not have is ignored.
    public static IServiceCollection AddTriage(IServiceCollection services, string localKey, string hostedKey)
    {
        services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
        services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
            .UseEscalation("hosted", options =>
            {
                options.PrimaryName = "local";
                options.DefaultThreshold = 0.6;
                options.Thresholds["intent"] = 0.85;
                options.Thresholds["card_not_arrived"] = 0.80;
                options.Thresholds["about_a_payment"] = 0.80;
            });
        return services;
    }
    #endregion
}
