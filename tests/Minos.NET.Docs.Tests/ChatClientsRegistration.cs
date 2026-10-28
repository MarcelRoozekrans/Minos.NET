namespace Minos.Docs.Tests;

#region ChatClients_Register
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Minos;
using Minos.Extensions.AI;
using Minos.Recording;

public static class ChatDecisionServices
{
    public static IServiceCollection AddChatDecisions(this IServiceCollection services, IChatClient chat, string recordPath)
    {
        // A chat client too: the decision client resolves IChatClient from the container when it is first resolved.
        services.AddChatClient(chat);

        // The recorder, as on the recording page. Its records keep each answer's probability source.
        services.AddSingleton(_ => new JsonlDecisionRecordSink(recordPath));
        services.AddSingleton(provider => new DecisionRecorder(provider.GetRequiredService<JsonlDecisionRecordSink>()));
        services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // A reasoning model gives no logprobs, or rejects the request for them, so Verbalized skips the call Auto spends on it.
        services.AddChatDecisionClient(options => options.ProbabilityMode = ProbabilityMode.Verbalized)
            .UseRecording();
        return services;
    }
}
#endregion
