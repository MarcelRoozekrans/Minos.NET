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
        // The chat client first: AddChatDecisionClient resolves IChatClient from the container.
        services.AddChatClient(chat);

        // The recorder, as on the recording page. Its records keep each answer's probability source.
        services.AddSingleton(_ => new JsonlDecisionRecordSink(recordPath));
        services.AddSingleton(provider => new DecisionRecorder(provider.GetRequiredService<JsonlDecisionRecordSink>()));
        services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // A reasoning model returns no logprobs, so Verbalized skips the logprob call that Auto would waste on it.
        services.AddChatDecisionClient(options => options.ProbabilityMode = ProbabilityMode.Verbalized)
            .UseRecording();
        return services;
    }
}
#endregion
