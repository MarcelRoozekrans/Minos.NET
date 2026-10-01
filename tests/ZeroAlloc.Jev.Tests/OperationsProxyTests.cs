using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Telemetry;
using ZeroAlloc.Telemetry;

namespace ZeroAlloc.Jev.Tests;

/// <summary>
/// The generated <c>JevOperationsInstrumented</c> proxy over a fake implementation: every attribute
/// <see cref="IJevOperations"/> uses, and what the proxy reads when nothing listens.
/// </summary>
[Collection(TelemetryListeners.Name)]
public sealed class OperationsProxyTests
{
    private static readonly Uri Endpoint = new("https://api.typesafe.ai/");

    [Fact]
    public async Task RawSuccess_RecordsTheSpanAndEveryMetric()
    {
        using var capture = new TelemetryCapture();
        var fake = new FakeOperations("{}") { Raw = Response("response-openrouter.json") };

        var result = await new JevOperationsInstrumented(fake).EvaluateAsync(Request(), "openrouter", Endpoint, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var span = capture.Span();
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("evaluate", span.OperationName);
        Assert.Equal("evaluate jev-latest", span.DisplayName);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);

        var start = capture.StartTags();
        Assert.Equal("evaluate", start.Tag("gen_ai.operation.name"));
        Assert.Equal("evaluate", start.Tag("jev.operation"));
        Assert.Equal("jev-latest", start.Tag("gen_ai.request.model"));
        Assert.Equal("openrouter", start.Tag("gen_ai.provider.name"));
        Assert.Equal("api.typesafe.ai", start.Tag("server.address"));
        Assert.Equal(443, start.Tag("server.port"));
        Assert.Equal(1, start.Tag("jev.request.question_count"));

        Assert.Equal("~typesafe/jev-latest", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(296, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(20, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal("gen-1727400000-abc123", span.GetTagItem("gen_ai.response.id"));
        Assert.Equal(0.000296, span.GetTagItem("jev.usage.cost"));
        Assert.Null(span.GetTagItem("error.type"));

        AssertSuccessMetrics(capture, "jev-latest", "openrouter", "~typesafe/jev-latest", "evaluate", inputTokens: 296, outputTokens: 20, confidences: []);
    }

    [Fact]
    public async Task RawSuccess_WithoutIdOrCost_SetsNeither()
    {
        using var capture = new TelemetryCapture();
        var fake = new FakeOperations("{}") { Raw = Response("response-noul.json") };

        await new JevOperationsInstrumented(fake).EvaluateAsync(Request(), "typesafe", Endpoint, CancellationToken.None);

        var span = capture.Span();
        Assert.Null(span.GetTagItem("gen_ai.response.id"));
        Assert.Null(span.GetTagItem("jev.usage.cost"));
    }

    [Fact]
    public async Task RawSuccess_RecordsOneConfidencePerChoiceAndScore_AndNoneForNoul()
    {
        using var capture = new TelemetryCapture();
        var mixed = JsonSerializer.Deserialize(
            $$"""{"model":"jev-1.13.0","answers":{"a":{{TelemetryBodies.ChoiceJson}},"b":{{TelemetryBodies.NoulJson}},"c":{{TelemetryBodies.ScoreJson}}},"usage":{"input_tokens":5,"output_tokens":2} }""",
            JevJsonContext.Default.SystemOneResponse)!;
        var fake = new FakeOperations("{}") { Raw = mixed };

        await new JevOperationsInstrumented(fake).EvaluateAsync(Request(), "typesafe", Endpoint, CancellationToken.None);

        Assert.Equal([0.81, 0.92], capture.Points("jev.answer.confidence").Select(point => point.Value));
    }

    [Fact]
    public async Task TypedSuccess_ReadsTheResponseLazily_AndRecordsEachConfidence()
    {
        using var capture = new TelemetryCapture();
        var fake = new FakeOperations(Fixture.Text("response-choice.json"));

        var result = await Evaluated.Unwrap(new JevOperationsInstrumented(fake).EvaluateTypedAsync<DepartmentRouting>(
            TelemetryBodies.EmptyBody(), "jev-test-model", "typesafe", Endpoint, 1, CancellationToken.None));

        Assert.Equal(Department.Billing, result.Value.Department.Value);
        var span = capture.Span();
        Assert.Equal("evaluate jev-test-model", span.DisplayName);
        Assert.Equal("evaluate-typed", capture.StartTags().Tag("jev.operation"));
        Assert.Equal(1, capture.StartTags().Tag("jev.request.question_count"));
        Assert.Equal("jev-1.13.0", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(318, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(34, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Null(span.GetTagItem("gen_ai.response.id"));

        AssertSuccessMetrics(capture, "jev-test-model", "typesafe", "jev-1.13.0", "evaluate-typed", inputTokens: 318, outputTokens: 34, confidences: [0.81]);
    }

    [Fact]
    public async Task TypedFailure_IsAnErrorWithItsKindAndNoDescription_AndRecordsOnlyTheDuration()
    {
        using var capture = new TelemetryCapture();
        var fake = new FakeOperations("{}") { Error = new JevError(JevErrorKind.RateLimited, "a message that must not leak", 429) };

        var result = await Evaluated.Unwrap(new JevOperationsInstrumented(fake).EvaluateTypedAsync<UrgencyCheck>(
            TelemetryBodies.EmptyBody(), "jev-test-model", "typesafe", Endpoint, 1, CancellationToken.None));

        Assert.True(result.IsFailure);
        var span = capture.Span();
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);
        Assert.Equal("RateLimited", span.GetTagItem("error.type"));
        Assert.Null(span.GetTagItem("gen_ai.response.model"));

        var point = capture.OnlyPoint();
        Assert.Equal("gen_ai.client.operation.duration", point.Metric);
        Assert.Equal("RateLimited", point.Tag("error.type"));
        Assert.Null(point.Tag("gen_ai.response.model"));
    }

    [Fact]
    public async Task BuiltSet_TagsItsOperationAndQuestionCount()
    {
        using var capture = new TelemetryCapture();
        var set = JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _)
            .Choice<Department>("department", "Which team?", out _)
            .Build().Value;
        var fake = new FakeOperations("""{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.4},"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81}},"usage":{"input_tokens":5,"output_tokens":2}}""");

        await Evaluated.Unwrap(new JevOperationsInstrumented(fake).EvaluateBuiltSetAsync(
            TelemetryBodies.EmptyBody(), set, "jev-test-model", "typesafe", Endpoint, CancellationToken.None));

        Assert.Equal("evaluate-built-set", capture.StartTags().Tag("jev.operation"));
        Assert.Equal(2, capture.StartTags().Tag("jev.request.question_count"));
        Assert.Equal([0.81], capture.Points("jev.answer.confidence").Select(point => point.Value));
        Assert.Equal("evaluate-built-set", capture.Points("jev.answer.confidence")[0].Tag("jev.operation"));
    }

    [Fact]
    public async Task ListModels_IsAListModelsSpan_WithOnlyTheDurationMetric()
    {
        using var capture = new TelemetryCapture();
        var proxy = new JevOperationsInstrumented(new FakeOperations("{}"));

        await proxy.ListModelsAsync("typesafe", Endpoint, CancellationToken.None);

        var span = capture.Span();
        Assert.Equal("list_models", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        var start = capture.StartTags();
        Assert.Equal("list_models", start.Tag("gen_ai.operation.name"));
        Assert.Equal("list-models", start.Tag("jev.operation"));
        Assert.Null(start.Tag("gen_ai.request.model"));
        Assert.Null(start.Tag("jev.request.question_count"));

        var point = capture.OnlyPoint();
        Assert.Equal(
            ["gen_ai.operation.name", "gen_ai.provider.name", "server.address", "server.port"],
            point.TagNames);
    }

    [Fact]
    public async Task ListModelsFailure_CarriesItsErrorType()
    {
        using var capture = new TelemetryCapture();
        var proxy = new JevOperationsInstrumented(new FakeOperations("{}") { Error = new JevError(JevErrorKind.Unauthorized, "no", 401) });

        await proxy.ListModelsAsync("typesafe", Endpoint, CancellationToken.None);

        Assert.Equal(ActivityStatusCode.Error, capture.Span().Status);
        Assert.Equal("Unauthorized", capture.Span().GetTagItem("error.type"));
        Assert.Equal("Unauthorized", capture.OnlyPoint().Tag("error.type"));
    }

    [Fact]
    public async Task Instruments_OnAllFourMethods_HaveTheSpecsUnitsAndBuckets_AndTheAssemblyVersion()
    {
        using var capture = new TelemetryCapture();
        var fake = new FakeOperations(Fixture.Text("response-choice.json")) { Raw = Response("response-choice.json") };
        var proxy = new JevOperationsInstrumented(fake);
        var set = JevQuestionSet.CreateBuilder().Choice<Department>("department", "Which team?", out _).Build().Value;

        // Every method records, so every instrument any of the four declares is published before it is checked.
        await proxy.EvaluateAsync(Request(), "typesafe", Endpoint, CancellationToken.None);
        await Evaluated.Unwrap(proxy.EvaluateTypedAsync<DepartmentRouting>(
            TelemetryBodies.EmptyBody(), "m", "typesafe", Endpoint, 1, CancellationToken.None));
        await Evaluated.Unwrap(proxy.EvaluateBuiltSetAsync(
            TelemetryBodies.EmptyBody(), set, "m", "typesafe", Endpoint, CancellationToken.None));
        await proxy.ListModelsAsync("typesafe", Endpoint, CancellationToken.None);

        Assert.Equal(4, capture.Points("gen_ai.client.operation.duration").Length);
        Assert.Equal(3, capture.Points("jev.answer.confidence").Length);
        AssertInstrument(capture, "gen_ai.client.operation.duration", "s", JevTelemetry.DurationBuckets.ToArray());
        AssertInstrument(capture, "gen_ai.client.inference.operation.input_tokens", "{token}", JevTelemetry.TokenBuckets.ToArray());
        AssertInstrument(capture, "gen_ai.client.inference.operation.output_tokens", "{token}", JevTelemetry.TokenBuckets.ToArray());
        AssertInstrument(capture, "jev.answer.confidence", "1", JevTelemetry.ConfidenceBuckets.ToArray());
        Assert.IsType<Counter<long>>(capture.Instrument("gen_ai.client.inference.usage.input_tokens"));
        Assert.Equal("{token}", capture.Instrument("gen_ai.client.inference.usage.output_tokens").Unit);

        var version = typeof(JevClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.All(capture.Spans("ZeroAlloc.Jev"), span => Assert.Equal(version, span.Source.Version));
        Assert.Equal(version, capture.Instrument("jev.answer.confidence").Meter.Version);
    }

    [Fact]
    public void EveryHistogramAttribute_RepeatsJevTelemetrysUnitAndBuckets()
    {
        var expected = new Dictionary<string, (string Unit, double[] Buckets)>(StringComparer.Ordinal)
        {
            ["gen_ai.client.operation.duration"] = ("s", JevTelemetry.DurationBuckets.ToArray()),
            ["gen_ai.client.inference.operation.input_tokens"] = ("{token}", JevTelemetry.TokenBuckets.ToArray()),
            ["gen_ai.client.inference.operation.output_tokens"] = ("{token}", JevTelemetry.TokenBuckets.ToArray()),
            ["jev.answer.confidence"] = ("1", JevTelemetry.ConfidenceBuckets.ToArray()),
        };
        var declared = 0;

        // Reads the literals themselves, so a drifted copy fails here even if the generator keeps one instrument per name.
        foreach (var method in typeof(IJevOperations).GetMethods())
        {
            foreach (var histogram in method.GetCustomAttributes<HistogramAttribute>())
            {
                AssertDeclared(expected, method.Name, histogram.Metric, histogram.Unit, histogram.Buckets);
                declared++;
            }

            foreach (var histogram in method.GetCustomAttributes<HistogramFromResultAttribute>())
            {
                AssertDeclared(expected, method.Name, histogram.Metric, histogram.Unit, histogram.Buckets);
                declared++;
            }
        }

        // The duration on all four methods, and both token histograms and the confidence histogram on the three evaluations.
        Assert.Equal(13, declared);
    }

    [Fact]
    public async Task Duration_IsRecordedInSeconds()
    {
        using var capture = new TelemetryCapture(traces: false);
        var proxy = new JevOperationsInstrumented(new FakeOperations("{}") { Delay = TimeSpan.FromMilliseconds(50) });

        await proxy.ListModelsAsync("typesafe", Endpoint, CancellationToken.None);

        // The call takes at least the fake's 50 ms. In seconds that is 0.05; in milliseconds it would be 40 or more, which
        // the upper bound rejects while leaving a loaded machine room.
        var duration = capture.OnlyPoint("gen_ai.client.operation.duration");
        Assert.Equal("s", duration.Unit);
        Assert.InRange(duration.Value, 0.04, 30);
    }

    [Fact]
    public async Task NothingListening_ReadsNoDeferredMember()
    {
        var fake = new FakeOperations(Fixture.Text("response-choice.json")) { DisposedResponse = true };
        var proxy = new JevOperationsInstrumented(fake);

        // The response is already returned, so reading ResponseModel, a token count or Confidences would throw.
        var result = await proxy.EvaluateTypedAsync<DepartmentRouting>(
            TelemetryBodies.EmptyBody(), "m", "typesafe", Endpoint, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Listening_ReadsTheDeferredMembers()
    {
        using var capture = new TelemetryCapture(traces: false);
        var fake = new FakeOperations(Fixture.Text("response-choice.json")) { DisposedResponse = true };
        var proxy = new JevOperationsInstrumented(fake);

        // The positive control for the test above: with a meter listener the proxy reads them, and the read throws.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await proxy.EvaluateTypedAsync<DepartmentRouting>(
            TelemetryBodies.EmptyBody(), "m", "typesafe", Endpoint, 1, CancellationToken.None));
    }

    [Fact]
    public async Task NothingListening_EnumeratesNoRawAnswers()
    {
        var answers = new CountingAnswers(Response("response-choice.json").Answers);
        var fake = new FakeOperations("{}") { Raw = WithAnswers(Response("response-choice.json"), answers) };

        await new JevOperationsInstrumented(fake).EvaluateAsync(Request(), "typesafe", Endpoint, CancellationToken.None);
        Assert.Equal(0, answers.Enumerations);

        using var capture = new TelemetryCapture(traces: false);
        await new JevOperationsInstrumented(fake).EvaluateAsync(Request(), "typesafe", Endpoint, CancellationToken.None);
        Assert.Equal(1, answers.Enumerations);
    }

    private static void AssertSuccessMetrics(
        TelemetryCapture capture, string requestModel, string provider, string responseModel, string jevOperation, int inputTokens, int outputTokens, double[] confidences)
    {
        // The unit only: Duration_IsRecordedInSeconds checks the value's scale over a call with a known minimum duration.
        var duration = capture.OnlyPoint("gen_ai.client.operation.duration");
        Assert.Equal("s", duration.Unit);
        Assert.Equal(
            ["gen_ai.operation.name", "gen_ai.provider.name", "gen_ai.request.model", "gen_ai.response.model", "server.address", "server.port"],
            duration.TagNames);
        Assert.Equal(responseModel, duration.Tag("gen_ai.response.model"));
        Assert.Equal(443, duration.Tag("server.port"));

        foreach (var (metric, value) in new[] { ("gen_ai.client.inference.operation.input_tokens", inputTokens), ("gen_ai.client.inference.operation.output_tokens", outputTokens) })
        {
            var point = capture.OnlyPoint(metric);
            Assert.Equal(value, point.Value);
            Assert.Equal(["gen_ai.operation.name", "gen_ai.provider.name", "gen_ai.request.model", "gen_ai.response.model"], point.TagNames);
            Assert.Equal(requestModel, point.Tag("gen_ai.request.model"));
            Assert.Equal(provider, point.Tag("gen_ai.provider.name"));
        }

        foreach (var (metric, value) in new[] { ("gen_ai.client.inference.usage.input_tokens", inputTokens), ("gen_ai.client.inference.usage.output_tokens", outputTokens) })
        {
            var point = capture.OnlyPoint(metric);
            Assert.Equal(value, point.Value);
            Assert.Equal(["gen_ai.operation.name", "gen_ai.provider.name", "gen_ai.request.model", "gen_ai.token.modality"], point.TagNames);
            Assert.Equal("text", point.Tag("gen_ai.token.modality"));
        }

        var confidencePoints = capture.Points("jev.answer.confidence");
        Assert.Equal(confidences, confidencePoints.Select(point => point.Value));
        Assert.All(confidencePoints, point =>
        {
            Assert.Equal(["gen_ai.operation.name", "gen_ai.provider.name", "gen_ai.request.model", "jev.operation"], point.TagNames);
            Assert.Equal(jevOperation, point.Tag("jev.operation"));
        });
    }

    private static void AssertInstrument(TelemetryCapture capture, string metric, string unit, double[] buckets)
    {
        var histogram = Assert.IsType<Histogram<double>>(capture.Instrument(metric));
        Assert.Equal(unit, histogram.Unit);
        Assert.Equal(buckets, histogram.Advice!.HistogramBucketBoundaries!);
    }

    private static void AssertDeclared(
        Dictionary<string, (string Unit, double[] Buckets)> expected, string method, string? metric, string? unit, double[]? buckets)
    {
        Assert.NotNull(metric);
        Assert.True(expected.TryGetValue(metric, out var spec), $"{method} declares an unexpected histogram {metric}.");
        Assert.True(string.Equals(spec.Unit, unit, StringComparison.Ordinal), $"{method} declares {metric} in {unit}, not {spec.Unit}.");
        Assert.Equal(spec.Buckets, buckets);
    }

    private static SystemOneRequest Request()
        => JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), JevJsonContext.Default.SystemOneRequest)!;

    private static SystemOneResponse Response(string fixture)
        => JsonSerializer.Deserialize(Fixture.Text(fixture), JevJsonContext.Default.SystemOneResponse)!;

    private static SystemOneResponse WithAnswers(SystemOneResponse response, IReadOnlyDictionary<string, JevAnswer> answers)
        => new() { Model = response.Model, Usage = response.Usage, Answers = answers };

    /// <summary>Counts how often <c>Values</c> is enumerated, which only <see cref="SystemOneResponse.Confidences"/> does here.</summary>
    private sealed class CountingAnswers(IReadOnlyDictionary<string, JevAnswer> inner) : IReadOnlyDictionary<string, JevAnswer>
    {
        public int Enumerations { get; private set; }

        public int Count => inner.Count;

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<JevAnswer> Values
        {
            get
            {
                Enumerations++;
                return inner.Values;
            }
        }

        public JevAnswer this[string key] => inner[key];

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out JevAnswer value) => inner.TryGetValue(key, out value);

        IEnumerator<KeyValuePair<string, JevAnswer>> IEnumerable<KeyValuePair<string, JevAnswer>>.GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
    }
}
