using System.ClientModel;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Minos.Extensions.AI;
using Minos.Recording;
using OpenAI;
using OpenAI.Chat;

namespace Minos.Samples.ChatClient;

/// <summary>The sample's program, apart from the console, so a test can run it and read its exit code.</summary>
public static class ChatClientApp
{
    public const string ApiKeyVariable = "OPENROUTER_API_KEY";

    public const string DefaultModel = "openai/gpt-4.1-mini";

    public const string Usage = "Usage: dotnet run --project samples/Minos.NET.Samples.ChatClient [-- [--model <id>]]";

    private static readonly Uri OpenRouter = new("https://openrouter.ai/api/v1");

    /// <summary>
    /// Runs the sample with the key in <c>OPENROUTER_API_KEY</c>: online through OpenRouter when it is set, offline with a
    /// fake chat client when it is not. Returns 0, or 2 for a bad argument.
    /// </summary>
    public static Task<int> RunAsync(
        IReadOnlyList<string> args, string baseDirectory, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        => RunAsync(args, baseDirectory, Environment.GetEnvironmentVariable(ApiKeyVariable), output, error, cancellationToken);

    /// <summary>Runs the sample with the given key; a null or empty key runs it offline.</summary>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, string baseDirectory, string? apiKey, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string model;
        try
        {
            model = ParseModel(args);
        }
        catch (ArgumentException e)
        {
            await error.WriteLineAsync(e.Message).ConfigureAwait(false);
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        var online = !string.IsNullOrEmpty(apiKey);
        IChatClient chat = online
            ? new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey!), new OpenAIClientOptions { Endpoint = OpenRouter }).AsIChatClient()
            : new FakeChatClient();

        // The sink keeps the records in memory; a real app would use a JsonlDecisionRecordSink.
        var sink = new InMemoryDecisionRecordSink();
        var services = new ServiceCollection();
        services.AddSingleton(chat);
        services.AddSingleton(sink);
        services.AddSingleton(_ => new DecisionRecorder(sink));
        services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());
        services.AddChatDecisionClient(options => options.Model = online ? model : null).UseRecording();

        var provider = services.BuildServiceProvider();
        int code;
        try
        {
            await output.WriteLineAsync(online ? "Asking " + model + " through OpenRouter" : "Asking a fake chat client, offline").ConfigureAwait(false);
            code = await TriageAsync(provider.GetRequiredService<IDecisionClient>(), output, error, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        if (code != 0)
        {
            return code;
        }

        // Disposing the container disposed the recorder, which wrote what was still queued to the sink.
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("Recorded decisions").ConfigureAwait(false);
        foreach (var decision in sink.Records)
        {
            var sources = new List<string>();
            foreach (var answer in decision.Record.Answers)
            {
                sources.Add(answer.Key + " " + answer.ProbabilitySource);
            }

            await output.WriteLineAsync("  " + string.Join(", ", sources)).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> TriageAsync(IDecisionClient client, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        foreach (var ticket in Tickets.All)
        {
            var result = await client.EvaluateAsync<TicketTriage>(ticket, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await error.WriteLineAsync(result.Error.Kind + ": " + result.Error.Message).ConfigureAwait(false);
                return 1;
            }

            var triage = result.Value;
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync(ticket).ConfigureAwait(false);
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"  wants a refund  {triage.WantsRefund.Probability:0.00}  from {triage.WantsRefund.ProbabilitySource}")).ConfigureAwait(false);
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"  team            {triage.Team.Value} {triage.Team.Confidence:0.00}  from {triage.Team.ProbabilitySource}")).ConfigureAwait(false);
        }

        return 0;
    }

    private static string ParseModel(IReadOnlyList<string> args)
    {
        var model = DefaultModel;
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], "--model", StringComparison.Ordinal))
            {
                throw new ArgumentException("Unknown argument " + args[i] + ".", nameof(args));
            }

            if (i + 1 >= args.Count)
            {
                throw new ArgumentException("--model needs a model id.", nameof(args));
            }

            model = args[++i];
        }

        return model;
    }
}
