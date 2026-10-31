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
    // Returns whether the sink confirmed the erasure. When it did not, because the sink failed or the recorder is
    // disposed, as it is while the app shuts down, keep the request and try again later.
    public static async Task<bool> ForgetAsync(IDecisionRecorder recorder, string customerId, CancellationToken cancellationToken)
    {
        try
        {
            await recorder.EraseAsync(customerId, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is RecordingException or ObjectDisposedException)
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

#region Recording_CustomSink
// A sink over a store of your own, here a list that stands in for a table. Each row holds the line the file format
// has for it, so the store can be exported as a file that DecisionRecordFile reads.
public sealed class LineStoreSink : IDecisionRecordSink
{
    private readonly List<(string DecisionId, string? ErasureKey, byte[] Line)> _rows = [];

    public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
        => StoreAsync(record.DecisionId, record.ErasureKey, buffer => DecisionRecordFile.WriteRecordLine(buffer, record));

    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
        => StoreAsync(decisionId, null, buffer => DecisionRecordFile.WriteOutcomeLine(buffer, decisionId, outcome));

    // An erasure deletes for real: every row of each decision recorded with the key so far.
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
    {
        var erased = _rows
            .Where(row => string.Equals(row.ErasureKey, erasureKey, StringComparison.Ordinal))
            .Select(row => row.DecisionId)
            .ToHashSet(StringComparer.Ordinal);
        _rows.RemoveAll(row => erased.Contains(row.DecisionId));
        return ValueTask.CompletedTask;
    }

    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        _rows.RemoveAll(row => string.Equals(row.DecisionId, decisionId, StringComparison.Ordinal));
        return ValueTask.CompletedTask;
    }

    // Writes the rows in the order they arrived, each line ended with a line feed. Export once no recorder writes to it.
    public async Task ExportAsync(string path, CancellationToken cancellationToken)
    {
        var file = File.Create(path);
        await using (file)
        {
            foreach (var row in _rows)
            {
                await file.WriteAsync(row.Line, cancellationToken);
                await file.WriteAsync("\n"u8.ToArray(), cancellationToken);
            }
        }
    }

    private ValueTask StoreAsync(string decisionId, string? erasureKey, Action<System.Buffers.ArrayBufferWriter<byte>> write)
    {
        // The writers write one line, without its line feed.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        write(buffer);
        _rows.Add((decisionId, erasureKey, buffer.WrittenSpan.ToArray()));
        return ValueTask.CompletedTask;
    }
}
#endregion

#region Recording_Shadow
public static class ShadowedApp
{
    public static async Task RunAsync(Func<IDecisionClient> createPrimary, IDecisionClient local, string path, CancellationToken cancellationToken)
    {
        await using var sink = new JsonlDecisionRecordSink(path);
        await using var recorder = new DecisionRecorder(sink);

        // One ShadowOptions for the candidate: every pipeline built with it shares the cap of 500 calls a day and the
        // limit of 2 candidate calls at a time. The candidate must not record its own decisions.
        var shadow = new ShadowOptions(local, dailyCap: 500) { Timeout = TimeSpan.FromSeconds(20), MaxConcurrent = 2 };

        // The pipeline owns the primary it is built over and disposes it with the pipeline. The candidate stays yours.
        using var client = createPrimary().AsBuilder()
            .UseRecording(shadow, recorder)
            .Build();

        // The result is the primary's. The candidate answers in the background and is never waited for.
        var result = await client.EvaluateAsync<SupportTriage>("The invoice is wrong.", cancellationToken);
        Console.WriteLine(result.IsSuccess);

        // A flush writes what is queued; it does not wait for a candidate that is still running, and dispose cancels
        // the candidates that are. A short program waits for as long as the candidate may take, which is its Timeout.
        await Task.Delay(shadow.Timeout, cancellationToken);
    }
}
#endregion

#region Recording_ShadowReport
public static class ShadowReporting
{
    public static async Task<Minos.Evaluation.ShadowReport> ReportAsync(string path, CancellationToken cancellationToken)
    {
        // The report reads the record file, not an export: agreement needs no outcome.
        var report = await Minos.Evaluation.ShadowReport.CreateAsync(DecisionRecordFile.ReadAsync(path, cancellationToken), cancellationToken: cancellationToken);

        Console.WriteLine($"{report.DecisionsShadowed} of {report.DecisionsRead} decisions were shadowed.");
        foreach (var question in report.Questions)
        {
            Console.WriteLine($"{question.Key}: {question.Agreement:P1} agreement over {question.Compared} comparisons{(question.IsSmall ? " (small)" : "")}");
        }

        await File.WriteAllTextAsync("shadow-report.md", report.ToMarkdown(), cancellationToken);
        return report;
    }
}
#endregion
