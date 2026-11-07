namespace Minos.Docs.Tests;

using Microsoft.Extensions.DependencyInjection;
using Minos.Recording;
using Minos.SystemOne;

public static class EscalationCompose
{
    #region Escalation_Compose
    // The first Use is the outermost stage. Recording sees one decision per call, escalation moves doubtful questions to
    // "hosted", and fallback keeps the local model reliable by asking "backup" when it fails. Both "hosted" and "backup"
    // are sent the state, so register only providers that may see it. The recorder is registered as the recording page
    // shows.
    public static IServiceCollection AddComposedDecisions(IServiceCollection services, string localKey, string hostedKey, string backupKey)
    {
        services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
        services.AddSystemOneDecisionClient("backup", options => options.ApiKey = backupKey);
        services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
            .UseRecording()
            .UseEscalation("hosted", options => options.PrimaryName = "local")
            .UseFallback(new KeyedFallbackStep("backup", TimeSpan.FromMilliseconds(300)));
        return services;
    }
    #endregion
}
