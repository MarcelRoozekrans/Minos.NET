using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.Evaluation.Tests;

public sealed class DatasetRunnerTests
{
    private static string NoulLine(string id)
        => "{\"id\":\"" + id + "\",\"state\":\"s " + id + "\",\"version\":1,\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":\"?\"}},\"expected\":{\"q\":true}}";

    private const string AllKinds = """
        {"id":"a","state":"x","version":1,"questions":{
          "n":{"type":"noul","instructions":"?"},
          "c":{"type":"choice","instructions":"?","criteria":{"one":"1","two":"2","three":"3"}},
          "s":{"type":"score","instructions":"?","criteria":["a","b","c"]}},
          "expected":{"n":true,"c":"two","s":2}}
        """;

    private static DecisionDataset Parse(params string[] lines)
    {
        var result = DecisionDataset.Parse(string.Join('\n', lines.Select(l => l.ReplaceLineEndings(" "))));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static DecisionDataset Many(int count) => Parse([.. Enumerable.Range(0, count).Select(i => NoulLine("i" + i))]);

    private static Result<DecisionResponse, DecisionError> Ok(DecisionResponse r) => Result<DecisionResponse, DecisionError>.Success(r);

    private static Result<DecisionResponse, DecisionError> Fail(DecisionError e) => Result<DecisionResponse, DecisionError>.Failure(e);

    private static DecisionResponse NoulResponse(DecisionRequest r, double p, string? model = null, DecisionUsage? usage = null)
        => new(r.Definition, [QuestionAnswer.Noul(p)], model, usage);

    private static FakeDecisionClient Simple(double p = 0.9)
        => new((r, _) => ValueTask.FromResult(Ok(NoulResponse(r, p))));

    [Fact]
    public async Task SendsOneRequestPerItem_WithTheItemsDefinitionAndState()
    {
        var dataset = Many(5);
        var client = Simple();

        await DatasetRunner.RunAsync(client, dataset);

        Assert.Equal(dataset.Count, client.Requests.Count);
        foreach (var item in dataset)
        {
            Assert.Contains(client.Requests, r => ReferenceEquals(r.Definition, item.Definition) && r.State.Equals(item.State));
        }
    }

    [Fact]
    public async Task ResultsAreInDatasetOrder_WithParallelism4()
    {
        var dataset = Many(8);
        var completion = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            var id = r.State.ToString().Split(' ')[^1];
            if (string.Equals(id, "i0", StringComparison.Ordinal))
            {
                // The first item finishes only after another one has, so completion order differs from dataset order.
                await firstDone.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }

            completion.Enqueue(id);
            firstDone.TrySetResult();
            return Ok(NoulResponse(r, 0.9));
        });

        var results = await DatasetRunner.RunAsync(client, dataset, new DatasetRunOptions { MaxParallelism = 4 });

        Assert.False(dataset.Select(i => i.Id).SequenceEqual(completion, StringComparer.Ordinal));
        Assert.Equal(dataset.Select(i => i.Id), results.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task NeverExceedsMaxParallelism()
    {
        // Every call waits until three are in flight, so the peak must reach the bound; it can never pass it.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            if (Interlocked.Increment(ref arrived) >= 3)
            {
                gate.TrySetResult();
            }

            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await Task.Yield();
            return Ok(NoulResponse(r, 0.9));
        });

        await DatasetRunner.RunAsync(client, Many(20), new DatasetRunOptions { MaxParallelism = 3 });

        Assert.Equal(3, client.PeakConcurrency);
    }

    [Fact]
    public void MaxParallelism0_Throws()
    {
        var client = Simple();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = DatasetRunner.RunAsync(client, Many(2), new DatasetRunOptions { MaxParallelism = 0 }); });

        Assert.Empty(client.Requests);
    }

    [Fact]
    public void NullClientOrDataset_ThrowsSynchronously()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = DatasetRunner.RunAsync(null!, Many(1)); });
        Assert.Throws<ArgumentNullException>(() => { _ = DatasetRunner.RunAsync(Simple(), null!); });
    }

    [Fact]
    public async Task ParallelismAboveItemCount_Runs()
    {
        var results = await DatasetRunner.RunAsync(Simple(), Many(2), new DatasetRunOptions { MaxParallelism = 50 });

        Assert.Equal(2, results.Items.Count);
        Assert.All(results.Items, i => Assert.NotNull(i.Answers));
    }

    [Fact]
    public async Task RecordsAnswersOnTheGatedNumber()
    {
        var dataset = Parse(AllKinds);
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Ok(new DecisionResponse(
            r.Definition,
            [
                QuestionAnswer.Noul(0.3),
                QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
                QuestionAnswer.Score(2, 1.7, 0.6, [0.1, 0.3, 0.6]),
            ]))));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, dataset)).Items);

        var noul = item.Answers!["n"];
        Assert.False(noul.Noul);
        Assert.Equal(0.3, noul.Probability);
        Assert.Equal(0.7, noul.Confidence, 12);
        var choice = item.Answers["c"];
        Assert.Equal("two", choice.ChoiceKey);
        Assert.Equal(0.8, choice.Confidence);
        Assert.Null(choice.Probability);
        var score = item.Answers["s"];
        Assert.Equal(2, score.ScoreLevel);
        Assert.Equal(0.6, score.Confidence);
    }

    [Fact]
    public async Task NoulAtHalfIsTrue()
    {
        var item = Assert.Single((await DatasetRunner.RunAsync(Simple(0.5), Many(1))).Items);

        Assert.True(item.Answers!["q"].Noul);
        Assert.Equal(0.5, item.Answers["q"].Probability);
        Assert.Equal(0.5, item.Answers["q"].Confidence);
    }

    [Fact]
    public async Task DecisionErrorBecomesItemError()
    {
        var client = new FakeDecisionClient((_, _) => ValueTask.FromResult(
            Fail(new DecisionError(DecisionErrorKind.RateLimited, "slow down") { StatusCode = 429 })));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, Many(1))).Items);

        Assert.Null(item.Answers);
        Assert.Equal("RateLimited", item.Error!.Kind);
        Assert.Equal(429, item.Error.StatusCode);
        Assert.Equal("slow down", item.Error.Message);
    }

    [Fact]
    public async Task ForeignCancellationIsCancelledError()
    {
        var foreign = new FakeDecisionClient((_, _) => throw new OperationCanceledException("not yours"));

        var item = Assert.Single((await DatasetRunner.RunAsync(foreign, Many(1))).Items);

        Assert.Equal("Cancelled", item.Error!.Kind);
        Assert.Null(item.Error.StatusCode);
        Assert.Null(item.Answers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task CallerCancellationThrows_AndNothingMoreIsSent(int parallelism)
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeDecisionClient(async (_, _) =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            return default;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = parallelism }, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.InRange(client.Requests.Count, 1, parallelism);
    }

    [Fact]
    public async Task AFaultingClientStopsTheRun_AndItsExceptionIsRethrown()
    {
        var boom = new InvalidOperationException("boom");
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            if (r.State.ToString().EndsWith("i2", StringComparison.Ordinal))
            {
                throw boom;
            }

            await Task.Delay(20, ct);
            return Ok(NoulResponse(r, 0.9));
        });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 2 }).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Same(boom, thrown);
        Assert.InRange(client.Requests.Count, 3, 4);
    }

    [Fact]
    public async Task CachedResponseIsFlagged_TokensModelCostIdCopied()
    {
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Ok(
            new DecisionResponse(r.Definition, [QuestionAnswer.Noul(0.9)], "m-1", new DecisionUsage { InputTokens = 10, OutputTokens = 3, Cost = 0.002 })
            {
                Id = "resp-1",
                IsCached = true,
            })));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, Many(1))).Items);

        Assert.True(item.Cached);
        Assert.Equal("m-1", item.Model);
        Assert.Equal(10, item.InputTokens);
        Assert.Equal(3, item.OutputTokens);
        Assert.Equal(0.002, item.Cost);
        Assert.Equal("resp-1", item.ResponseId);
    }

    [Fact]
    public async Task ProgressReportsEachCompletion()
    {
        var reported = new List<int>();
        var progress = new SyncProgress(reported);

        await DatasetRunner.RunAsync(Simple(), Many(6), new DatasetRunOptions { Progress = progress, MaxParallelism = 3 });

        Assert.Equal(Enumerable.Range(1, 6), reported.Order());
    }

    [Fact]
    public async Task HeaderCarriesMetadata_DatasetName_AndTimes()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
        var client = new FakeDecisionClient(
            (r, _) =>
            {
                time.Advance(TimeSpan.FromMilliseconds(250));
                return ValueTask.FromResult(Ok(NoulResponse(r, 0.9)));
            },
            new DecisionClientMetadata("System One", new Uri("https://example.test/"), "sys-1"));

        var results = await DatasetRunner.RunAsync(client, Many(1), new DatasetRunOptions { DatasetName = "banking", TimeProvider = time });

        var header = results.Header;
        Assert.Equal("banking", header.DatasetName);
        Assert.Equal(1, header.ItemCount);
        Assert.Equal("System One", header.ProviderName);
        Assert.Equal(new Uri("https://example.test/"), header.Endpoint);
        Assert.Equal("sys-1", header.DefaultModel);
        Assert.Equal(1, header.Parallelism);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero), header.Started);
        Assert.Equal(header.Started.AddMilliseconds(250), header.Finished);
        Assert.Equal(250, results.Items[0].LatencyMs, 6);
    }

    [Fact]
    public async Task NoMetadata_LeavesProviderFieldsNull()
    {
        var results = await DatasetRunner.RunAsync(Simple(), Many(1));

        Assert.Null(results.Header.ProviderName);
        Assert.Null(results.Header.Endpoint);
        Assert.Null(results.Header.DefaultModel);
        Assert.Equal("dataset", results.Header.DatasetName);
    }

    [Fact]
    public async Task MissingAnswerKey_IsAbsent_ExtraKeyIgnored()
    {
        var dataset = Parse(NoulLine("a"));
        // The response answers a different set: no "q", and an extra "other".
        var foreign = new QuestionSetDefinition(QuestionDefinition.Noul("other", "?"));
        var client = new FakeDecisionClient((_, _) => ValueTask.FromResult(
            Ok(new DecisionResponse(foreign, [QuestionAnswer.Noul(0.9)]))));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, dataset)).Items);

        Assert.NotNull(item.Answers);
        Assert.DoesNotContain("q", item.Answers.Keys);
        Assert.DoesNotContain("other", item.Answers.Keys);
        Assert.Null(item.Error);
    }

    [Fact]
    public async Task ChoiceIndexOutOfRange_IsInvalidResponseError()
    {
        var dataset = Parse(AllKinds);
        var wide = new QuestionSetDefinition(
            QuestionDefinition.Noul("n", "?"),
            QuestionDefinition.Choice("c", "?", new OptionDefinition("one", null), new OptionDefinition("two", null), new OptionDefinition("three", null), new OptionDefinition("four", null)),
            QuestionDefinition.Score("s", "?", "a", "b", "c"));
        var client = new FakeDecisionClient((_, _) => ValueTask.FromResult(Ok(new DecisionResponse(
            wide,
            [
                QuestionAnswer.Noul(0.9),
                QuestionAnswer.Choice(3, 0.8, [0.1, 0.1, 0.0, 0.8]),
                QuestionAnswer.Score(0, 0, 0.6, [0.6, 0.2, 0.2]),
            ]))));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, dataset)).Items);

        Assert.Null(item.Answers);
        Assert.Equal("InvalidResponse", item.Error!.Kind);
        Assert.Contains("c", item.Error.Message, StringComparison.Ordinal);
    }

    private sealed class SyncProgress(List<int> sink) : IProgress<int>
    {
        public void Report(int value)
        {
            lock (sink)
            {
                sink.Add(value);
            }
        }
    }
}
