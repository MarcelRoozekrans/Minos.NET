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
    public async Task RecordsEachAnswersProbabilitySource()
    {
        var dataset = Parse(AllKinds);
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Ok(new DecisionResponse(
            r.Definition,
            [
                QuestionAnswer.Noul(0.3, ProbabilitySource.Verbalized),
                QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1], ProbabilitySource.Logprobs),
                QuestionAnswer.Score(2, 1.7, 0.6, [0.1, 0.3, 0.6], ProbabilitySource.Verbalized),
            ]))));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, dataset)).Items);

        Assert.Equal(ProbabilitySource.Verbalized, item.Answers!["n"].ProbabilitySource);
        Assert.Equal(0.3, item.Answers["n"].Probability);
        Assert.Equal(ProbabilitySource.Logprobs, item.Answers["c"].ProbabilitySource);
        Assert.Equal(ProbabilitySource.Verbalized, item.Answers["s"].ProbabilitySource);
    }

    [Fact]
    public async Task ModelAnswers_RecordModel()
    {
        var item = Assert.Single((await DatasetRunner.RunAsync(Simple(0.9), Many(1))).Items);

        Assert.Equal(ProbabilitySource.Model, item.Answers!["q"].ProbabilitySource);
    }

    [Fact]
    public async Task AnUndefinedSource_IsAnInvalidResponse()
    {
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Ok(new DecisionResponse(r.Definition, [QuestionAnswer.Noul(0.9, (ProbabilitySource)7)]))));

        var item = Assert.Single((await DatasetRunner.RunAsync(client, Many(1))).Items);

        Assert.Null(item.Answers);
        Assert.Equal(nameof(DecisionErrorKind.InvalidResponse), item.Error!.Kind);
        Assert.Contains("source", item.Error.Message, StringComparison.Ordinal);
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
    public async Task AFaultingClientStopsTheRun_AndItsExceptionIsTheInnerException()
    {
        // i0 and i1 answer at once and i2 throws. Every later item waits on a task that only the run's cancellation ends,
        // so no item after i2 can finish and free its worker: at most one more request, i3, is sent before the stop.
        var boom = new InvalidOperationException("boom");
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            var id = r.State.ToString().Split(' ')[^1];
            if (id is "i0" or "i1")
            {
                return Ok(NoulResponse(r, 0.9));
            }

            if (string.Equals(id, "i2", StringComparison.Ordinal))
            {
                throw boom;
            }

            await never.Task.WaitAsync(ct);
            return Ok(NoulResponse(r, 0.9));
        });

        var thrown = await Assert.ThrowsAsync<DatasetRunException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 2 }).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Same(boom, thrown.InnerException);
        Assert.False(never.Task.IsCompleted);
        Assert.InRange(client.Requests.Count, 3, 4);
        Assert.DoesNotContain(client.Requests, r => r.State.ToString().EndsWith("i4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NegativeReportedCost_IsUnknown_AndTheResultsSaveAndLoad()
    {
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Ok(
            NoulResponse(r, 0.9, "m-1", new DecisionUsage { InputTokens = 1, OutputTokens = 1, Cost = -1 }))));

        var results = await DatasetRunner.RunAsync(client, Many(1));

        Assert.Null(Assert.Single(results.Items).Cost);
        using var stream = new MemoryStream();
        results.Save(stream);
        stream.Position = 0;
        var loaded = EvaluationResults.Parse(stream);
        Assert.True(loaded.IsSuccess);
        Assert.Null(loaded.Value.Items[0].Cost);
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

    private sealed class BackwardsTime : TimeProvider
    {
        private long _ticks = 1_000_000;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => _ticks -= 500;
    }

    private static void AssertSavesAndLoads(EvaluationResults results)
    {
        using var stream = new MemoryStream();
        results.Save(stream);
        stream.Position = 0;
        var loaded = EvaluationResults.Parse(stream);
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? null : string.Join("; ", loaded.Error.Select(p => $"{p.Line}: {p.Message}")));
        Assert.Equal(results.Items.Count, loaded.Value.Items.Count);
    }

    [Fact]
    public void DatasetName_RefusesNullAndEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => new DatasetRunOptions { DatasetName = null! });
        Assert.Throws<ArgumentException>(() => new DatasetRunOptions { DatasetName = string.Empty });
        var lone = new string((char)0xD800, 1);
        Assert.Throws<ArgumentException>(() => new DatasetRunOptions { DatasetName = "a" + lone });
    }

    [Fact]
    public void TimeProvider_RefusesNull()
        => Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => new DatasetRunOptions { TimeProvider = null! }).ParamName);

    [Fact]
    public async Task ProviderTextWithALoneSurrogate_IsCleaned_AndSaves()
    {
        var lone = new string((char)0xD800, 1);
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Fail(new DecisionError(DecisionErrorKind.RateLimited, "bad " + lone))));

        var results = await DatasetRunner.RunAsync(client, Many(1));

        Assert.Equal("bad \uFFFD", Assert.Single(results.Items).Error!.Message);
        AssertSavesAndLoads(results);
    }

    [Fact]
    public async Task MetadataWithALoneSurrogate_IsCleaned_AndSaves()
    {
        var lone = new string((char)0xD800, 1);
        var client = new FakeDecisionClient(
            (r, _) => ValueTask.FromResult(Ok(NoulResponse(r, 0.9))),
            new DecisionClientMetadata("prov" + lone, new Uri("https://example.test/"), "model" + lone));

        var results = await DatasetRunner.RunAsync(client, Many(1));

        Assert.Equal("prov\uFFFD", results.Header.ProviderName);
        Assert.Equal("model\uFFFD", results.Header.DefaultModel);
        AssertSavesAndLoads(results);
    }

    [Fact]
    public async Task MissingSomeKeys_AreAbsent_ExtraKeyIgnored()
    {
        var dataset = Parse(AllKinds);
        var partial = new QuestionSetDefinition(QuestionDefinition.Noul("n", "?"), QuestionDefinition.Noul("other", "?"));
        var client = new FakeDecisionClient((_, _) => ValueTask.FromResult(
            Ok(new DecisionResponse(partial, [QuestionAnswer.Noul(0.9), QuestionAnswer.Noul(0.1)]))));

        var results = await DatasetRunner.RunAsync(client, dataset);

        var item = Assert.Single(results.Items);
        Assert.Equal(["n"], item.Answers!.Keys);
        Assert.Null(item.Error);
        AssertSavesAndLoads(results);
    }

    [Fact]
    public async Task ResponseForAnotherDefinition_IsInvalidResponse_AndSaves()
    {
        var foreign = new QuestionSetDefinition(QuestionDefinition.Noul("other", "?"));
        var client = new FakeDecisionClient((_, _) => ValueTask.FromResult(
            Ok(new DecisionResponse(foreign, [QuestionAnswer.Noul(0.9)]))));

        var results = await DatasetRunner.RunAsync(client, Parse(NoulLine("a")));

        var item = Assert.Single(results.Items);
        Assert.Null(item.Answers);
        Assert.Equal("InvalidResponse", item.Error!.Kind);
        Assert.Contains("none of the item's questions", item.Error.Message, StringComparison.Ordinal);
        AssertSavesAndLoads(results);
    }

    [Fact]
    public async Task KindMismatchOnOneQuestion_IsInvalidResponse()
    {
        var wrong = new QuestionSetDefinition(
            QuestionDefinition.Score("n", "?", "a", "b"),
            QuestionDefinition.Choice("c", "?", new OptionDefinition("one", null), new OptionDefinition("two", null), new OptionDefinition("three", null)));
        var client = new FakeDecisionClient((_, _) => ValueTask.FromResult(Ok(new DecisionResponse(
            wrong, [QuestionAnswer.Score(1, 1, 0.5, [0.5, 0.5]), QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1])]))));

        var results = await DatasetRunner.RunAsync(client, Parse(AllKinds));

        var item = Assert.Single(results.Items);
        Assert.Null(item.Answers);
        Assert.Equal("InvalidResponse", item.Error!.Kind);
        Assert.Equal("The response answered 'n' as Score; the question is Noul.", item.Error.Message);
        AssertSavesAndLoads(results);
    }

    [Fact]
    public async Task NegativeTokens_AreUnknown_AndInfiniteCostIsUnknown()
    {
        var client = new FakeDecisionClient((r, _) => ValueTask.FromResult(Ok(
            NoulResponse(r, 0.9, "m", new DecisionUsage { InputTokens = -1, OutputTokens = -5, Cost = double.PositiveInfinity }))));

        var results = await DatasetRunner.RunAsync(client, Many(1));

        var item = Assert.Single(results.Items);
        Assert.Null(item.InputTokens);
        Assert.Null(item.OutputTokens);
        Assert.Null(item.Cost);
        AssertSavesAndLoads(results);
    }

    [Fact]
    public async Task BackwardsClock_GivesZeroLatency()
    {
        var results = await DatasetRunner.RunAsync(Simple(), Many(1), new DatasetRunOptions { TimeProvider = new BackwardsTime() });

        Assert.Equal(0, Assert.Single(results.Items).LatencyMs);
        AssertSavesAndLoads(results);
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

    [Fact]
    public async Task ClientFault_ThrowsDatasetRunException_WithFinishedItemsInOrder()
    {
        var boom = new InvalidOperationException("boom");
        var client = new FakeDecisionClient((r, _) =>
            r.State.ToString().EndsWith("i5", StringComparison.Ordinal) ? throw boom : ValueTask.FromResult(Ok(NoulResponse(r, 0.9))));

        var ex = await Assert.ThrowsAsync<DatasetRunException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 1 }).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Same(boom, ex.InnerException);
        Assert.Equal(["i0", "i1", "i2", "i3", "i4"], ex.PartialResults.Items.Select(i => i.Id));
        Assert.True(ex.PartialResults.Header.Partial);
        Assert.Equal(5, ex.PartialResults.Header.ItemCount);
        Assert.All(ex.PartialResults.Items, i => Assert.Null(i.Error));
    }

    [Fact]
    public async Task ParallelFault_PartialResultsAreASubsetInDatasetOrder()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            var id = r.State.ToString().Split(' ')[^1];
            if (id is "i0" or "i1" or "i3")
            {
                return Ok(NoulResponse(r, 0.9));
            }

            if (string.Equals(id, "i4", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("boom");
            }

            await never.Task.WaitAsync(ct);
            return Ok(NoulResponse(r, 0.9));
        });

        var ex = await Assert.ThrowsAsync<DatasetRunException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 3 }).WaitAsync(TimeSpan.FromSeconds(10)));

        var ids = ex.PartialResults.Items.Select(i => int.Parse(i.Id[1..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal([.. ids.Order()], ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains(0, ids);
        Assert.DoesNotContain(4, ids);
        Assert.Equal(ids.Count, ex.PartialResults.Header.ItemCount);
        Assert.All(ex.PartialResults.Items, i => Assert.Null(i.Error));
    }

    [Fact]
    public async Task ProgressFault_ThrowsDatasetRunException_KeepingTheItemThatReported()
    {
        var boom = new InvalidOperationException("progress");
        var progress = new ThrowingProgress(2, boom);

        var ex = await Assert.ThrowsAsync<DatasetRunException>(
            () => DatasetRunner.RunAsync(Simple(), Many(5), new DatasetRunOptions { Progress = progress }));

        Assert.Same(boom, ex.InnerException);
        Assert.Equal(["i0", "i1"], ex.PartialResults.Items.Select(i => i.Id));
    }

    private sealed class ThrowingProgress(int at, Exception ex) : IProgress<int>
    {
        public void Report(int value)
        {
            if (value == at)
            {
                throw ex;
            }
        }
    }

    [Fact]
    public async Task Cancellation_IsAnOperationCanceledException_ForTheCallersToken_NotADatasetRunException()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeDecisionClient(async (_, _) =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            return default;
        });
        OperationCanceledException? caught = null;

        try
        {
            await DatasetRunner.RunAsync(client, Many(5), cancellationToken: cts.Token);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            caught = ex;
        }

        Assert.IsType<DatasetRunCanceledException>(caught);
    }

    [Fact]
    public async Task CallerCancellation_MidRun_KeepsTheFinishedItemsInOrder()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            if (r.State.ToString().EndsWith("i3", StringComparison.Ordinal))
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
            }

            return Ok(NoulResponse(r, 0.9));
        });

        var ex = await Assert.ThrowsAsync<DatasetRunCanceledException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 1, DatasetName = "set" }, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.Null(ex.InnerException);
        Assert.Equal(["i0", "i1", "i2"], ex.PartialResults!.Items.Select(i => i.Id));
        Assert.True(ex.PartialResults!.Header.Partial);
        Assert.Equal(3, ex.PartialResults!.Header.ItemCount);
        Assert.Equal("set", ex.PartialResults!.Header.DatasetName);
        Assert.All(ex.PartialResults!.Items, i => Assert.Null(i.Error));
        Assert.Equal(4, client.Requests.Count);
        Assert.Contains("after 3 finished items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellation_InParallel_LeavesOutTheItemsItCut()
    {
        // i0, i1 and i3 answer at once, i4 cancels the caller's token, and every other item waits until the stop cancels it.
        using var cts = new CancellationTokenSource();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            var id = r.State.ToString().Split(' ')[^1];
            if (id is "i0" or "i1" or "i3")
            {
                return Ok(NoulResponse(r, 0.9));
            }

            if (string.Equals(id, "i4", StringComparison.Ordinal))
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
            }

            await never.Task.WaitAsync(ct);
            return Ok(NoulResponse(r, 0.9));
        });

        var ex = await Assert.ThrowsAsync<DatasetRunCanceledException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 3 }, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));

        var ids = ex.PartialResults!.Items.Select(i => int.Parse(i.Id[1..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal([.. ids.Order()], ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains(0, ids);
        Assert.DoesNotContain(4, ids);
        Assert.Equal(ids.Count, ex.PartialResults!.Header.ItemCount);
        Assert.True(ex.PartialResults!.Header.Partial);
        Assert.All(ex.PartialResults!.Items, i => Assert.Null(i.Error));
        Assert.False(never.Task.IsCompleted);
    }

    [Fact]
    public async Task CallerCancellation_BeforeTheStart_SendsNothing_AndKeepsNoItems()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = Simple();

        var ex = await Assert.ThrowsAsync<DatasetRunCanceledException>(
            () => DatasetRunner.RunAsync(client, Many(3), cancellationToken: cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Empty(client.Requests);
        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.Empty(ex.PartialResults!.Items);
        Assert.Equal(0, ex.PartialResults!.Header.ItemCount);
        Assert.True(ex.PartialResults!.Header.Partial);
    }

    [Fact]
    public async Task ClientFault_WithALiveToken_IsStillADatasetRunException()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeDecisionClient((r, _) =>
            r.State.ToString().EndsWith("i2", StringComparison.Ordinal) ? throw new InvalidOperationException("boom") : ValueTask.FromResult(Ok(NoulResponse(r, 0.9))));

        var ex = await Record.ExceptionAsync(
            () => DatasetRunner.RunAsync(client, Many(5), cancellationToken: cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));

        var stopped = Assert.IsType<DatasetRunException>(ex);
        Assert.Equal(["i0", "i1"], stopped.PartialResults.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task CallerCancellation_DuringAFault_WinsAndKeepsTheFaultAsTheInnerException()
    {
        // i2 throws once i3 is in flight. The fault's stop cancels i3, which then cancels the caller's token before it
        // returns, so the caller cancels while the fault is being handled.
        using var cts = new CancellationTokenSource();
        var boom = new InvalidOperationException("boom");
        var i3Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            var id = r.State.ToString().Split(' ')[^1];
            switch (id)
            {
                case "i0" or "i1":
                    return Ok(NoulResponse(r, 0.9));
                case "i2":
                    await i3Started.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                    throw boom;
                default:
                    i3Started.TrySetResult();
                    try
                    {
                        await never.Task.WaitAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        await cts.CancelAsync();
                        throw;
                    }

                    return Ok(NoulResponse(r, 0.9));
            }
        });

        var ex = await Assert.ThrowsAsync<DatasetRunCanceledException>(
            () => DatasetRunner.RunAsync(client, Many(10), new DatasetRunOptions { MaxParallelism = 2 }, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Same(boom, ex.InnerException);
        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.Equal(["i0", "i1"], ex.PartialResults!.Items.Select(i => i.Id));
        Assert.Equal(2, ex.PartialResults!.Header.ItemCount);
        Assert.True(ex.PartialResults!.Header.Partial);
    }

    [Fact]
    public async Task CancelledPartialResults_SaveAndLoad_AndMakeAPartialReport()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeDecisionClient(async (r, ct) =>
        {
            if (r.State.ToString().EndsWith("i2", StringComparison.Ordinal))
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
            }

            return Ok(NoulResponse(r, 0.9));
        });
        var dataset = Many(4);

        var ex = await Assert.ThrowsAsync<DatasetRunCanceledException>(
            () => DatasetRunner.RunAsync(client, dataset, cancellationToken: cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        using var stream = new MemoryStream();
        ex.PartialResults!.Save(stream);
        stream.Position = 0;
        var loaded = EvaluationResults.Parse(stream);
        var report = CalibrationReport.Create(ex.PartialResults, dataset);

        Assert.True(loaded.IsSuccess);
        Assert.True(loaded.Value.Header.Partial);
        Assert.True(report.IsSuccess);
        Assert.Contains("Partial run: 2 of 4 items", report.Value.ToMarkdown(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "after 0 finished items.")]
    [InlineData(1, "after 1 finished item.")]
    [InlineData(3, "after 3 finished items.")]
    public void CanceledMessage_CountsFinishedItemsInTheRightNumber(int count, string expected)
    {
        var items = Enumerable.Range(1, count).Select(i => ReportFixture.Answered(i, new Dictionary<string, AnswerRecord> { ["q"] = AnswerRecord.ForNoul(0.9) })).ToArray();
        using var cts = new CancellationTokenSource();
        var inner = new InvalidOperationException("boom");

        var ex = new DatasetRunCanceledException(ReportFixture.PartialResults(items), inner, cts.Token);

        Assert.EndsWith(expected, ex.Message, StringComparison.Ordinal);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.Equal(count, ex.PartialResults!.Items.Count);
    }

    [Fact]
    public void CanceledException_NeedsItsPartialResults()
        => Assert.Throws<ArgumentNullException>(() => new DatasetRunCanceledException((EvaluationResults)null!, null, CancellationToken.None));

    [Fact]
    public void CanceledException_StandardConstructors_CarryNoPartialResults()
    {
        var inner = new InvalidOperationException("boom");

        DatasetRunCanceledException[] made = [new(), new("stopped"), new("stopped", inner)];

        Assert.All(made, ex =>
        {
            Assert.Null(ex.PartialResults);
            Assert.Equal(CancellationToken.None, ex.CancellationToken);
        });
        Assert.Equal("The dataset run was cancelled.", made[0].Message);
        Assert.Equal("stopped", made[1].Message);
        Assert.Same(inner, made[2].InnerException);
    }

    [Fact]
    public void CanceledException_TokenConstructors_KeepTheToken_AndCarryNoPartialResults()
    {
        using var cts = new CancellationTokenSource();
        var inner = new InvalidOperationException("boom");

        DatasetRunCanceledException[] made = [new(cts.Token), new("stopped", cts.Token), new("stopped", inner, cts.Token)];

        Assert.All(made, ex =>
        {
            Assert.Equal(cts.Token, ex.CancellationToken);
            Assert.Null(ex.PartialResults);
        });
        Assert.Equal("The dataset run was cancelled.", made[0].Message);
        Assert.Equal("stopped", made[1].Message);
        Assert.Same(inner, made[2].InnerException);
    }

    [Theory]
    [InlineData(1, "after 1 finished item:")]
    [InlineData(3, "after 3 finished items:")]
    public void Message_CountsFinishedItemsInTheRightNumber(int count, string expected)
    {
        var items = Enumerable.Range(1, count).Select(i => ReportFixture.Answered(i, new Dictionary<string, AnswerRecord> { ["q"] = AnswerRecord.ForNoul(0.9) })).ToArray();

        var ex = new DatasetRunException(ReportFixture.PartialResults(items), new InvalidOperationException("boom"));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }
}
