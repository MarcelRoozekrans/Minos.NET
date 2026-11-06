using System.Globalization;
using System.Text;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>Runs a labelled dataset through the triage pipeline and appends one record line per item.</summary>
public static class TriageRun
{
    /// <summary>A progress line is logged after this many items.</summary>
    public const int ProgressEvery = 25;

    /// <summary>
    /// Triages every item in dataset order, one at a time, and appends a line to <paramref name="recordsPath"/> for each,
    /// flushed as it is written. Logs a line for each failed item and every 25 items, never the message text.
    /// </summary>
    /// <param name="local">The local client. The run owns it and disposes it, because disposing the pipeline disposes the client under it.</param>
    /// <param name="target">The hosted client the unsure questions are re-asked on. The caller owns it.</param>
    /// <param name="dataset">The items to triage.</param>
    /// <param name="recordsPath">The JSONL file the records are appended to.</param>
    /// <param name="log">Where the progress lines go.</param>
    /// <param name="cancellationToken">Stops the run; the lines written so far are kept.</param>
    /// <returns>The counts of the run.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled, after the lines were flushed.</exception>
    public static Task<TriageSummary> RunAsync(
        IDecisionClient local,
        IDecisionClient target,
        DecisionDataset dataset,
        string recordsPath,
        TextWriter log,
        CancellationToken cancellationToken)
        => RunAsync(local, target, dataset, recordsPath, log, TimeProvider.System, cancellationToken);

    /// <summary>As the public overload, with the clock as a parameter so a test can move it by hand.</summary>
    internal static async Task<TriageSummary> RunAsync(
        IDecisionClient local,
        IDecisionClient target,
        DecisionDataset dataset,
        string recordsPath,
        TextWriter log,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentException.ThrowIfNullOrEmpty(recordsPath);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);

        var localSlot = new CallSlot();
        var targetSlot = new CallSlot();
        using var client = TriagePipeline.Build(local, target, localSlot, targetSlot, time);
        var records = new StreamWriter(recordsPath, append: true, new UTF8Encoding(false)) { NewLine = "\n" };
        await using var recordsScope = records.ConfigureAwait(false);

        int items = 0, decided = 0, failed = 0, escalated = 0, noTimeLeft = 0, targetFailed = 0;
        foreach (var item in dataset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items++;
            localSlot.Reset();
            targetSlot.Reset();

            var start = time.GetTimestamp();
            var result = await client.EvaluateAsync(new DecisionRequest(item.Definition, item.State), cancellationToken).ConfigureAwait(false);
            var totalMs = time.GetElapsedTime(start).TotalMilliseconds;

            TriageRecord record;
            if (result.IsFailure)
            {
                failed++;
                record = new TriageRecord(item.Id, totalMs, result.Error.Kind.ToString(), [], []);
                await log.WriteLineAsync($"{item.Id}: failed ({result.Error.Kind})").ConfigureAwait(false);
            }
            else
            {
                decided++;
                record = ToRecord(item, result.Value, totalMs, localSlot, targetSlot);
                foreach (var question in record.Questions)
                {
                    switch (question.Reason)
                    {
                        case nameof(RouteReason.Escalated):
                            escalated++;
                            break;
                        case nameof(RouteReason.NoTimeLeft):
                            noTimeLeft++;
                            break;
                        case nameof(RouteReason.TargetFailed):
                            targetFailed++;
                            break;
                    }
                }
            }

            await records.WriteLineAsync(TriageRecordWriter.Line(record)).ConfigureAwait(false);
            await records.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (items % ProgressEvery == 0)
            {
                await log.WriteLineAsync(
                    $"{items}/{dataset.Count}: decided {decided}, failed {failed}, escalated {escalated}, no time left {noTimeLeft}, target failed {targetFailed}")
                    .ConfigureAwait(false);
            }
        }

        return new TriageSummary(items, decided, failed, escalated, noTimeLeft, targetFailed);
    }

    private static TriageRecord ToRecord(DatasetItem item, DecisionResponse response, double totalMs, CallSlot localSlot, CallSlot targetSlot)
    {
        var definition = response.Definition;
        var path = response.Path;
        var questions = new List<TriageQuestion>(definition.Questions.Count);
        for (var i = 0; i < definition.Questions.Count; i++)
        {
            var question = definition.Questions[i];
            var final = Answer(question, response.Answers[i]);

            // The stage always attaches a path; a response without one reads as the local client's confident answer.
            var route = path is null ? (QuestionRoute?)null : path.Routes[i];
            var local = localSlot.Response is { } captured ? Answer(question, captured.Answers[i]) : final;
            questions.Add(new TriageQuestion(
                question.Key,
                local,
                route?.AnsweredBy ?? TriagePipeline.PrimaryName,
                (route?.Reason ?? RouteReason.Confident).ToString(),
                route?.PrimaryCertainty ?? Certainty(local),
                route?.TargetCertainty,
                final));
        }

        var calls = new List<TriageCall>();
        foreach (var call in path?.Calls ?? [new DecisionCall(TriagePipeline.PrimaryName, response.Model, response.Usage, response.IsCached)])
        {
            double? latency = call.ClientName switch
            {
                TriagePipeline.PrimaryName => localSlot.LatencyMs,
                TriagePipeline.TargetName when call.Status != CallStatus.NotCalledNoTimeLeft => targetSlot.LatencyMs,
                _ => null,
            };
            calls.Add(new TriageCall(
                call.ClientName,
                call.Model,
                call.Usage?.InputTokens,
                call.Usage?.OutputTokens,
                call.Usage?.Cost,
                call.Status.ToString(),
                latency));
        }

        return new TriageRecord(item.Id, totalMs, null, questions, calls);
    }

    private static TriageAnswer Answer(QuestionDefinition question, QuestionAnswer answer)
        => question.Kind switch
        {
            QuestionKind.Noul => new TriageAnswer("noul", answer.Probability, null, null),
            QuestionKind.Choice => new TriageAnswer("choice", null, question.Options[answer.ChosenIndex].Key, answer.Confidence),
            _ => throw new NotSupportedException("The triage sample asks Noul and Choice questions only."),
        };

    private static double Certainty(TriageAnswer answer)
        => answer.Probability is { } p ? Math.Max(p, 1 - p) : answer.Confidence ?? 0;
}
