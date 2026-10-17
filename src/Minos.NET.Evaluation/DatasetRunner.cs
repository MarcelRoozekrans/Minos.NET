using System.Globalization;
using ZeroAlloc.Results;

namespace Minos.Evaluation;

/// <summary>Runs a dataset through a decision client, one request per item.</summary>
public static class DatasetRunner
{
    /// <summary>
    /// Sends every item of <paramref name="dataset"/> to <paramref name="client"/> and records the outcomes. A failed item
    /// is recorded, never thrown, and the run never stops early for it. The exception is an exception thrown by the client
    /// or by <see cref="DatasetRunOptions.Progress"/>: the first one stops the other workers from sending more requests
    /// (requests already in flight may finish) and is rethrown as it was thrown.
    /// </summary>
    /// <param name="client">The client to evaluate with.</param>
    /// <param name="dataset">The dataset to run.</param>
    /// <param name="options">Run settings, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that stops the run by throwing.</param>
    /// <returns>The results, in dataset order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="dataset"/> is <see langword="null"/>; thrown before the task is returned.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DatasetRunOptions.MaxParallelism"/> is below 1; thrown before the task is returned.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<EvaluationResults> RunAsync(
        IDecisionClient client,
        DecisionDataset dataset,
        DatasetRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(dataset);
        options ??= new DatasetRunOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxParallelism, 1, nameof(options));
        return RunCoreAsync(client, dataset, options, cancellationToken);
    }

    private static async Task<EvaluationResults> RunCoreAsync(
        IDecisionClient client,
        DecisionDataset dataset,
        DatasetRunOptions options,
        CancellationToken cancellationToken)
    {
        var time = options.TimeProvider ?? TimeProvider.System;
        var metadata = client.GetService<DecisionClientMetadata>();
        var started = time.GetUtcNow();
        var results = new ItemResult[dataset.Count];
        var next = -1;
        var completed = 0;

        // Cancelled by the caller's token, or by the first worker to fault.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = stop.Token;

        async Task Worker()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= results.Length)
                    {
                        return;
                    }

                    results[index] = await RunItemAsync(client, dataset[index], time, token, cancellationToken).ConfigureAwait(false);
                    options.Progress?.Report(Interlocked.Increment(ref completed));
                }
            }
            catch
            {
                await stop.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }

        var workers = new Task[Math.Min(options.MaxParallelism, Math.Max(results.Length, 1))];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = Task.Run(Worker, CancellationToken.None);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var header = new RunHeader
        {
            DatasetName = options.DatasetName,
            ItemCount = results.Length,
            ProviderName = ResultsRules.Clean(metadata?.ProviderName),
            Endpoint = metadata?.Endpoint,
            DefaultModel = ResultsRules.Clean(metadata?.DefaultModel),
            Started = started,
            Finished = time.GetUtcNow(),
            Parallelism = options.MaxParallelism,
        };
        return new EvaluationResults(header, results);
    }

    private static async Task<ItemResult> RunItemAsync(IDecisionClient client, DatasetItem item, TimeProvider time, CancellationToken token, CancellationToken cancellationToken)
    {
        var begin = time.GetTimestamp();
        Result<DecisionResponse, DecisionError> outcome;
        try
        {
            outcome = await client.EvaluateAsync(new DecisionRequest(item.Definition, item.State), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(item, time.GetElapsedTime(begin), new ItemError { Kind = "Cancelled", Message = ResultsRules.Clean(ex.Message)! });
        }

        var latency = time.GetElapsedTime(begin);
        if (!outcome.IsSuccess)
        {
            var error = outcome.Error;
            return Failed(item, latency, new ItemError { Kind = error.Kind.ToString(), StatusCode = error.StatusCode, Message = ResultsRules.Clean(error.Message)! });
        }

        var response = outcome.Value;
        var usage = response.Usage;
        var answers = Record(item, response, out var invalid);
        return new ItemResult
        {
            Id = item.Id,
            Line = item.Line,
            LatencyMs = Milliseconds(latency),
            Cached = response.IsCached,
            Model = ResultsRules.Clean(response.Model),
            InputTokens = NonNegative(usage?.InputTokens),
            OutputTokens = NonNegative(usage?.OutputTokens),
            Cost = usage?.Cost is { } cost && double.IsFinite(cost) && cost >= 0 ? cost : null,
            ResponseId = ResultsRules.Clean(response.Id),
            Answers = invalid is null ? answers : null,
            Error = invalid is null ? null : new ItemError { Kind = nameof(DecisionErrorKind.InvalidResponse), Message = invalid },
        };
    }

    // A clock that runs backwards must not give a negative latency, which the results file refuses.
    private static double Milliseconds(TimeSpan latency) => Math.Max(0, latency.TotalMilliseconds);

    // A negative or missing count, and a negative, infinite or missing cost, is unknown.
    private static int? NonNegative(int? count) => count is >= 0 ? count : null;

    private static ItemResult Failed(DatasetItem item, TimeSpan latency, ItemError error)
        => new() { Id = item.Id, Line = item.Line, LatencyMs = Milliseconds(latency), Error = error };

    // Walks the item's own questions and builds only what the results file can hold. An answer the response lacks is
    // absent, and an answer to a question the item does not ask is ignored. The response is invalid, and the item an
    // InvalidResponse error, when an answer has another kind than its question, a chosen index the question does not
    // have, a probability or confidence outside 0 to 1, or when none of the item's questions was answered.
    private static Dictionary<string, AnswerRecord> Record(DatasetItem item, DecisionResponse response, out string? invalid)
    {
        invalid = null;
        var byKey = new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal);
        foreach (var answer in response.Answers)
        {
            byKey[answer.Key] = answer;
        }

        var records = new Dictionary<string, AnswerRecord>(StringComparer.Ordinal);
        foreach (var question in item.Definition.Questions)
        {
            if (!byKey.TryGetValue(question.Key, out var answer))
            {
                continue;
            }

            if (answer.Kind != question.Kind)
            {
                invalid = $"The response answered '{question.Key}' as {answer.Kind}; the question is {question.Kind}.";
                return records;
            }

            switch (question.Kind)
            {
                case QuestionKind.Noul:
                    var p = answer.Probability;
                    if (!ResultsRules.IsUnit(p))
                    {
                        invalid = $"Question '{question.Key}' has probability {p.ToString(CultureInfo.InvariantCulture)}, outside 0 to 1.";
                        return records;
                    }

                    records[question.Key] = AnswerRecord.ForNoul(p);
                    break;
                default:
                    if (answer.ChosenIndex < 0 || answer.ChosenIndex >= question.Options.Count)
                    {
                        invalid = $"Question '{question.Key}' chose option {answer.ChosenIndex.ToString(CultureInfo.InvariantCulture)}, but it has {question.Options.Count.ToString(CultureInfo.InvariantCulture)} options.";
                        return records;
                    }

                    if (!ResultsRules.IsUnit(answer.Confidence))
                    {
                        invalid = $"Question '{question.Key}' has confidence {answer.Confidence.ToString(CultureInfo.InvariantCulture)}, outside 0 to 1.";
                        return records;
                    }

                    records[question.Key] = question.Kind == QuestionKind.Choice
                        ? AnswerRecord.ForChoice(question.Options[answer.ChosenIndex].Key, answer.Confidence)
                        : AnswerRecord.ForScore(answer.ChosenIndex, answer.Confidence);
                    break;
            }
        }

        if (records.Count == 0)
        {
            invalid = "The response answered none of the item's questions.";
        }

        return records;
    }
}
