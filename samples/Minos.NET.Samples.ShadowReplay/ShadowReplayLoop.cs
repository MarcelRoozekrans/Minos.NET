using System.Globalization;
using Minos.Evaluation;
using Minos.Recording;
using ZeroAlloc.Results;

namespace Minos.Samples.ShadowReplay;

/// <summary>
/// Replays a labelled dataset through the recorder in shadow mode: the primary decides each item, the item's labels are
/// added as its confirmed outcome, and the candidate shadows it. Each item waits for its shadow write before the next
/// starts, so with one candidate call at a time no decision is skipped as busy.
/// </summary>
public static class ShadowReplayLoop
{
    /// <summary>The longest a candidate call may take; a longer one is recorded as a timeout.</summary>
    public static readonly TimeSpan CandidateTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How much longer than <see cref="CandidateTimeout"/> an item waits for its shadow write.</summary>
    public static readonly TimeSpan WaitMargin = TimeSpan.FromSeconds(5);

    private const string OutcomeSource = "banking77-label";
    private const int ProgressEvery = 25;

    /// <summary>Maps an item's expected answers to the confirmed answers of its outcome.</summary>
    /// <param name="item">The dataset item.</param>
    /// <returns>The confirmed answer per question key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, ConfirmedAnswer> ConfirmedAnswers(DatasetItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var answers = new Dictionary<string, ConfirmedAnswer>(item.Expected.Count, StringComparer.Ordinal);
        foreach (var (key, expected) in item.Expected)
        {
            answers[key] = expected.Kind switch
            {
                QuestionKind.Noul => ConfirmedAnswer.ForNoul(expected.Noul),
                QuestionKind.Choice => ConfirmedAnswer.ForChoice(expected.ChoiceKey!),
                QuestionKind.Score => ConfirmedAnswer.ForScore(expected.ScoreLevel),
                _ => throw new ArgumentException($"Question '{key}' has an expected answer of an unknown kind.", nameof(item)),
            };
        }

        return answers;
    }

    /// <summary>
    /// Replays every item in dataset order: records the primary's decision with the item's id, adds its labels as a
    /// confirmed outcome, and waits for the candidate's shadow write, for at most <see cref="CandidateTimeout"/> plus
    /// <see cref="WaitMargin"/>. Logs a line for each failed item and every 25 items, never the state.
    /// </summary>
    /// <param name="primary">The client whose decisions are recorded. The replay owns it and disposes it.</param>
    /// <param name="candidate">The client that shadows the primary. The caller owns it.</param>
    /// <param name="dataset">The items to replay.</param>
    /// <param name="recordsPath">The JSONL file the records are appended to.</param>
    /// <param name="log">Where the progress lines go.</param>
    /// <param name="cancellationToken">Stops the replay; the records written so far are kept.</param>
    /// <returns>The counts of the replay.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled, after the records were flushed.</exception>
    public static async Task<ReplaySummary> RunAsync(
        IDecisionClient primary,
        IDecisionClient candidate,
        DecisionDataset dataset,
        string recordsPath,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentException.ThrowIfNullOrEmpty(recordsPath);
        ArgumentNullException.ThrowIfNull(log);

        // Until the pipeline is built, the primary itself is what the replay disposes.
        var client = primary;
        ShadowSignalSink? sink = null;
        DecisionRecorder? recorder = null;
        try
        {
            sink = new ShadowSignalSink(new JsonlDecisionRecordSink(recordsPath));
            recorder = new DecisionRecorder(sink);
            var shadow = new ShadowOptions(candidate, dailyCap: 400) { MaxConcurrent = 1, Timeout = CandidateTimeout };
            client = primary.AsBuilder().UseRecording(shadow, recorder).Build();

            int items = 0, decided = 0, primaryFailed = 0, shadowed = 0, waitsRunOut = 0;
            foreach (var item in dataset)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items++;

                // Registered before the decision, so a shadow write that lands before the wait below is not missed.
                var signal = sink.Expect(item.Id);
                Result<DecisionResponse, DecisionError> result;
                using (DecisionRecording.Begin(decisionId: item.Id))
                {
                    result = await client.EvaluateAsync(new DecisionRequest(item.Definition, item.State), cancellationToken).ConfigureAwait(false);
                }

                if (result.IsFailure)
                {
                    // A failed decision is not shadowed: there is nothing to wait for.
                    primaryFailed++;
                    sink.Forget(item.Id);
                    await log.WriteLineAsync($"{item.Id}: primary failed ({result.Error.Kind})").ConfigureAwait(false);
                }
                else
                {
                    decided++;
                    var outcome = DecisionOutcome.Confirmed(OutcomeSource, TimeProvider.System.GetUtcNow(), ConfirmedAnswers(item));
                    await recorder.AddOutcomeAsync(item.Id, outcome, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await signal.WaitAsync(CandidateTimeout + WaitMargin, cancellationToken).ConfigureAwait(false);
                        shadowed++;
                    }
                    catch (TimeoutException)
                    {
                        waitsRunOut++;
                        sink.Forget(item.Id);
                        await log.WriteLineAsync($"{item.Id}: no shadow write within {(CandidateTimeout + WaitMargin).TotalSeconds.ToString(CultureInfo.InvariantCulture)} s").ConfigureAwait(false);
                    }
                }

                if (items % ProgressEvery == 0)
                {
                    await log.WriteLineAsync(
                        $"{items}/{dataset.Count}: decided {decided}, primary failed {primaryFailed}, shadowed {shadowed}, waits run out {waitsRunOut}")
                        .ConfigureAwait(false);
                }
            }

            return new ReplaySummary(items, decided, primaryFailed, shadowed, waitsRunOut);
        }
        finally
        {
            // The pipeline first, so no decision is made once the recorder flushes, and the sink last, after the flush.
            try
            {
                client.Dispose();
            }
            finally
            {
                try
                {
                    if (recorder is not null)
                    {
                        await recorder.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (sink is not null)
                    {
                        await sink.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
