namespace Minos.Docs.Tests;

#region Recording_Questions
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minos;
using Minos.Recording;

public enum SupportLane
{
    [Criterion("Payments, invoices and refunds")]
    Billing,

    [Criterion("Bugs, outages and integrations")]
    Technical,
}

[Questions]
public partial record SupportTriage
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which queue should handle this?")]
    public partial Choice<SupportLane> Queue { get; }
}
#endregion

#region Recording_Register
public static class RecordedApp
{
    public static async Task RunAsync(IDecisionClient client, string path, IServiceProvider services, Func<IDecisionClient, Task> work)
    {
        // The app owns the sink and the recorder. Declared in this order, the recorder is disposed first, and its
        // dispose writes what is still queued to the sink before the sink closes the file.
        await using var sink = new JsonlDecisionRecordSink(path);
        await using var recorder = new DecisionRecorder(sink, new RecordingOptions { Capacity = 1024 }, services.GetService<ILoggerFactory>());

        // The recording stage goes first, so it is outermost: one decision is one record, a cache hit included.
        using var recorded = client.AsBuilder()
            .UseRecording(recorder)
            .UseCaching()
            .UseRetries()
            .Build(services);

        await work(recorded);
    }
}
#endregion

#region Recording_DependencyInjection
public static class RecordedServices
{
    public static IServiceCollection AddRecordedDecisions(this IServiceCollection services, string apiKey, string path)
    {
        // The container creates the sink before the recorder and disposes them in the reverse order, recorder first.
        services.AddSingleton(_ => new JsonlDecisionRecordSink(path));
        services.AddSingleton(provider => new DecisionRecorder(
            provider.GetRequiredService<JsonlDecisionRecordSink>(),
            new RecordingOptions(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // UseRecording with no recorder takes the DecisionRecorder from the container. A stage added here wraps the
        // registered client and its standard pipeline, so it is outermost already.
        services.AddSystemOneDecisionClient(options => options.ApiKey = apiKey).UseRecording();
        return services;
    }
}
#endregion

#region Recording_Scope
public static class TicketDecisions
{
    // Begin the scope in the method that awaits the call. The id is the one you give, or one the stage generates.
    public static async Task<(SupportLane? Queue, string? DecisionId)> RouteAsync(
        IDecisionClient client, string customerId, string message, CancellationToken cancellationToken)
    {
        using var scope = DecisionRecording.Begin(erasureKey: customerId);
        var result = await client.EvaluateAsync<SupportTriage>(message, cancellationToken);

        // Keep the id with the ticket, so outcomes and erasures can name this decision later.
        return (result.IsSuccess ? result.Value.Queue.Value : null, scope.DecisionId);
    }
}
#endregion

#region Recording_Outcomes
public static class TicketOutcomes
{
    // A reviewer moved the ticket: the right answers, which a dataset can use as expected answers.
    public static ValueTask ConfirmAsync(IDecisionRecorder recorder, string decisionId, CancellationToken cancellationToken)
        => recorder.AddOutcomeAsync(
            decisionId,
            DecisionOutcome.Confirmed("reviewer-ui", DateTimeOffset.UtcNow, new Dictionary<string, ConfirmedAnswer>
            {
                ["queue"] = ConfirmedAnswer.ForChoice("technical"),
                ["is_urgent"] = ConfirmedAnswer.ForNoul(true),
            }),
            cancellationToken);

    // Something that happened afterwards, which is never an expected answer.
    public static ValueTask ComplainedAsync(IDecisionRecorder recorder, string decisionId, CancellationToken cancellationToken)
        => recorder.AddOutcomeAsync(
            decisionId,
            DecisionOutcome.Signal("mailer", DateTimeOffset.UtcNow, "complained", value: 1, note: "Wrote back within the hour."),
            cancellationToken);
}
#endregion

#region Recording_Erasure
public static class CustomerErasure
{
    // Returns whether the sink confirmed the erasure. When it did not, keep the request and try again later.
    public static async Task<bool> ForgetAsync(IDecisionRecorder recorder, string customerId, CancellationToken cancellationToken)
    {
        try
        {
            await recorder.EraseAsync(customerId, cancellationToken);
            return true;
        }
        catch (RecordingException)
        {
            return false;
        }
    }
}
#endregion

#region Recording_Redaction
public static partial class StateRedaction
{
    // Text is kept with e-mail addresses masked. Structured state is not kept at all.
    public static RecordingOptions Options() => new()
    {
        Redact = state => state.TryGetString(out var text)
            ? RedactionResult.Keep(Email().Replace(text!, "[email]"))
            : RedactionResult.Drop,
    };

    [GeneratedRegex(@"[^\s@]+@[^\s@]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Email();
}
#endregion

#region Recording_ReadFile
public static class RecordFileReading
{
    // Reads every decision that survives its erasures, each with its outcomes. Bad lines are reported, not thrown.
    public static async Task<IReadOnlyList<RecordedDecision>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var problems = new List<DecisionRecordFileProblem>();
        var decisions = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, cancellationToken))
        {
            decisions.Add(decision);
        }

        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"line {problem.Line}: {problem.Message}");
        }

        return decisions;
    }

    // Rewrites the file without erased records. Run it when no recorder writes to the file.
    public static async Task<int> CompactAsync(string path, CancellationToken cancellationToken)
    {
        var problems = await DecisionRecordFile.CompactAsync(path, cancellationToken);
        return problems.Count;
    }
}
#endregion

#region Recording_InMemory
public static class RecordingInTests
{
    // In a test, keep the records in memory and flush before reading them.
    public static async Task<RecordedDecision> RecordOneAsync(IDecisionClient fake, CancellationToken cancellationToken)
    {
        var sink = new InMemoryDecisionRecordSink();
        await using var recorder = new DecisionRecorder(sink);
        using var client = fake.AsBuilder().UseRecording(recorder).Build();

        await client.EvaluateAsync<SupportTriage>("The invoice is wrong.", cancellationToken);
        await recorder.FlushAsync(cancellationToken);

        return sink.Records.Single();
    }
}
#endregion
