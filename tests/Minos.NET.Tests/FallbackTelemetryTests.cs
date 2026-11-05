using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>
/// The fallback stage's span tags, read through <c>UseOpenTelemetry</c>'s stage, the <c>minos.fallbacks</c> counter the
/// stage emits itself, who reports them when escalation wraps fallback or a cache copies a response, and that no state
/// reaches telemetry or logs.
/// </summary>
[Collection(TelemetryListeners.Name)]
public sealed class FallbackTelemetryTests
{
    private const string Steps = "minos.fallback.steps";
    private const string AnsweredBy = "minos.fallback.answered_by";
    private const string Counter = "minos.fallbacks";

    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    // is_urgent is confident by escalation's default threshold of 0.6; department is unsure.
    private static readonly Dictionary<string, QuestionAnswer> Unsure = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> Sure = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.08, 0.02]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["department"] = QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
    };

    private static FallbackDecisionClient Stage(IDecisionClient primary, params FallbackStep[] steps)
    {
        var options = new FallbackOptions();
        foreach (var step in steps)
        {
            options.Steps.Add(step);
        }

        return new FallbackDecisionClient(primary, options);
    }

    private static StubDecisionClient Failing() => StubDecisionClient.Failing(DecisionErrorKind.Server);

    [Fact]
    public async Task A_fallback_answer_tags_the_span_with_steps_and_answered_by()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(Stage(
            Failing(),
            new FallbackStep("clef", Failing()),
            new FallbackStep("hosted", StubDecisionClient.Answering(Sure))));

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsSuccess);
        var span = capture.Span();
        Assert.Equal(2, span.GetTagItem(Steps));
        Assert.Equal("hosted", span.GetTagItem(AnsweredBy));
        Assert.Equal(2, result.Value.FallbackStepsTag);
        Assert.Equal("hosted", result.Value.FallbackAnsweredByTag);
    }

    [Fact]
    public async Task Step_zero_answering_adds_no_fallback_tags()
    {
        using var capture = new TelemetryCapture();
        var clef = StubDecisionClient.Answering(Sure);
        using var stage = new OpenTelemetryDecisionClient(Stage(StubDecisionClient.Answering(Sure), new FallbackStep("clef", clef)));

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Path);
        var span = capture.Span();
        Assert.Null(span.GetTagItem(Steps));
        Assert.Null(span.GetTagItem(AnsweredBy));
        Assert.Empty(capture.Points(Counter));
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task A_step_zero_failure_the_chain_rejects_counts_nothing()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(Stage(
            StubDecisionClient.Failing(DecisionErrorKind.Validation), new FallbackStep("clef", StubDecisionClient.Answering(Sure))));

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsFailure);
        Assert.Null(capture.Span().GetTagItem(Steps));
        Assert.Empty(capture.Points(Counter));
    }

    [Fact]
    public async Task An_exhausted_chain_counts_exhausted_and_adds_no_fallback_span_tags()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(Stage(
            Failing(), new FallbackStep("clef", Failing()), new FallbackStep("hosted", StubDecisionClient.Failing(DecisionErrorKind.RateLimited))));

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsFailure);
        var span = capture.Span();
        Assert.Null(span.GetTagItem(Steps));
        Assert.Null(span.GetTagItem(AnsweredBy));
        Assert.Equal("RateLimited", span.GetTagItem("error.type"));
        var point = capture.OnlyPoint(Counter);
        Assert.Equal(1, point.Value);
        Assert.Equal(["minos.fallback.outcome"], point.TagNames);
        Assert.Equal("exhausted", point.Tag("minos.fallback.outcome"));
    }

    [Fact]
    public async Task A_later_failure_that_stops_the_chain_counts_exhausted()
    {
        using var capture = new TelemetryCapture();
        var hosted = StubDecisionClient.Answering(Sure);
        using var stage = Stage(Failing(), new FallbackStep("clef", StubDecisionClient.Failing(DecisionErrorKind.Refused)), new FallbackStep("hosted", hosted));

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal(DecisionErrorKind.Refused, result.Error.Kind);
        Assert.Equal(0, hosted.Calls);
        Assert.Equal("exhausted", capture.OnlyPoint(Counter).Tag("minos.fallback.outcome"));
    }

    [Fact]
    public async Task Every_later_step_skipped_for_time_counts_exhausted()
    {
        using var capture = new TelemetryCapture();
        var time = new FakeTimeProvider(Start);
        var clef = StubDecisionClient.Answering(Sure);
        var primary = new StubDecisionClient((_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));
        });
        var options = new FallbackOptions { Budget = TimeSpan.FromSeconds(5) };
        options.Steps.Add(new FallbackStep("clef", clef) { ExpectedLatency = TimeSpan.FromSeconds(6) });
        using var stage = new FallbackDecisionClient(primary, options, timeProvider: time);

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Equal(DecisionErrorKind.Server, result.Error.Kind);
        Assert.Equal(0, clef.Calls);
        Assert.Equal("exhausted", capture.OnlyPoint(Counter).Tag("minos.fallback.outcome"));
    }

    [Fact]
    public async Task The_counter_counts_answered_with_the_client_tag()
    {
        using var capture = new TelemetryCapture();
        using var stage = Stage(Failing(), new FallbackStep("clef", StubDecisionClient.Answering(Sure)));

        await stage.EvaluateAsync(new DecisionRequest(Triage, State));
        await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        var points = capture.Points(Counter);
        Assert.Equal(2, points.Length);
        Assert.All(points, point =>
        {
            Assert.Equal(1, point.Value);
            Assert.Equal("{fallback}", point.Unit);
            Assert.Equal(["minos.fallback.client", "minos.fallback.outcome"], point.TagNames);
            Assert.Equal("clef", point.Tag("minos.fallback.client"));
            Assert.Equal("answered", point.Tag("minos.fallback.outcome"));
        });
        var instrument = Assert.IsType<Counter<long>>(capture.Instrument(Counter));
        Assert.Equal("Minos", instrument.Meter.Name);
        Assert.Equal(
            typeof(DecisionResponse).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            instrument.Meter.Version);
    }

    [Fact]
    public async Task Escalation_over_fallback_reports_both_parts_once()
    {
        using var capture = new TelemetryCapture();
        var fallback = Stage(Failing(), new FallbackStep("clef", StubDecisionClient.Answering(Unsure)));
        using var stage = new OpenTelemetryDecisionClient(
            new EscalatingDecisionClient(fallback, StubDecisionClient.Answering(TargetAnswers), new EscalationOptions { TargetName = "hosted" }));

        var response = (await stage.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        var span = capture.Span();
        Assert.Equal(1, span.GetTagItem("minos.escalation.escalated"));
        Assert.Equal(1, span.GetTagItem(Steps));
        Assert.Equal("clef", span.GetTagItem(AnsweredBy));
        Assert.Equal("clef", capture.OnlyPoint(Counter).Tag("minos.fallback.client"));
        Assert.Equal("hosted", capture.OnlyPoint("minos.escalations").Tag("minos.escalation.target"));
        Assert.Equal(["primary", "clef", "hosted"], response.Path!.Calls.Select(call => call.ClientName));
    }

    [Fact]
    public async Task Escalation_over_fallback_without_an_unsure_answer_keeps_the_fallback_tags()
    {
        using var capture = new TelemetryCapture();
        var fallback = Stage(Failing(), new FallbackStep("clef", StubDecisionClient.Answering(Sure)));
        using var stage = new OpenTelemetryDecisionClient(
            new EscalatingDecisionClient(fallback, StubDecisionClient.Answering(TargetAnswers), new EscalationOptions { TargetName = "hosted" }));

        await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        var span = capture.Span();
        Assert.Null(span.GetTagItem("minos.escalation.escalated"));
        Assert.Null(span.GetTagItem("minos.escalation.skipped"));
        Assert.Equal(1, span.GetTagItem(Steps));
        Assert.Equal("clef", span.GetTagItem(AnsweredBy));
        Assert.Empty(capture.Points("minos.escalations"));
        Assert.Single(capture.Points(Counter));
    }

    [Fact]
    public async Task A_step_whose_own_escalation_answered_reports_both_parts()
    {
        using var capture = new TelemetryCapture();
        var clef = new EscalatingDecisionClient(
            StubDecisionClient.Answering(Unsure), StubDecisionClient.Answering(TargetAnswers), new EscalationOptions { TargetName = "hosted" });
        using var stage = new OpenTelemetryDecisionClient(Stage(Failing(), new FallbackStep("clef", clef)));

        await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        var span = capture.Span();
        Assert.Equal(1, span.GetTagItem("minos.escalation.escalated"));
        Assert.Equal(1, span.GetTagItem(Steps));
        Assert.Equal("clef", span.GetTagItem(AnsweredBy));
        Assert.Single(capture.Points("minos.escalations"));
    }

    [Fact]
    public async Task A_cached_copy_reports_no_routing_telemetry()
    {
        // The response an earlier fallback built, before anything listens, and the copy a cache inside step 0 returns.
        var earlier = Stage(Failing(), new FallbackStep("clef", StubDecisionClient.Answering(Sure)));
        var built = (await earlier.EvaluateAsync(new DecisionRequest(Triage, State))).Value;
        var cached = built.AsCached();
        using var capture = new TelemetryCapture();
        var clef = StubDecisionClient.Answering(Sure);
        using var stage = new OpenTelemetryDecisionClient(Stage(StubDecisionClient.Responding(_ => cached), new FallbackStep("clef", clef)));

        var result = await stage.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.Same(cached, result.Value);
        Assert.Same(built.Path, cached.Path);
        Assert.Equal(0, clef.Calls);
        Assert.Equal(1, built.FallbackStepsTag);
        Assert.Null(cached.FallbackStepsTag);
        Assert.Null(cached.FallbackAnsweredByTag);
        var span = capture.Span();
        Assert.Equal(true, span.GetTagItem("minos.cache.hit"));
        Assert.Null(span.GetTagItem(Steps));
        Assert.Null(span.GetTagItem(AnsweredBy));
        Assert.Empty(capture.Points(Counter));
    }

    [Fact]
    public async Task Escalation_over_a_cached_fallback_copy_reports_no_fallback_telemetry()
    {
        var earlier = Stage(Failing(), new FallbackStep("clef", StubDecisionClient.Answering(Sure)));
        var cached = (await earlier.EvaluateAsync(new DecisionRequest(Triage, State))).Value.AsCached();
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new EscalatingDecisionClient(
            StubDecisionClient.Responding(_ => cached), StubDecisionClient.Answering(TargetAnswers), new EscalationOptions { TargetName = "hosted" }));

        var response = (await stage.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.NotNull(response.Path);
        Assert.Null(response.Path.Routing);
        var span = capture.Span();
        Assert.Null(span.GetTagItem(Steps));
        Assert.Null(span.GetTagItem(AnsweredBy));
        Assert.Empty(capture.Points(Counter));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task No_state_reaches_logs_tags_or_messages(int scenario)
    {
        const string Sentinel = "SENTINEL-";
        using var capture = new TelemetryCapture();
        using var logs = new LogCapture();
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Choice(
                Sentinel + "key",
                DecisionContent.FromString(Sentinel + "instructions"),
                new OptionDefinition(Sentinel + "option-a", Criterion.Text(Sentinel + "criterion-a")),
                new OptionDefinition(Sentinel + "option-b", Criterion.Text(Sentinel + "criterion-b"))));
        var answers = new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal) { [Sentinel + "key"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.1]) };
        var time = new FakeTimeProvider(Start);

        // 0: step 0 hangs and is cut, clef answers. 1: step 0 is cut, clef is skipped for time, hosted answers another
        // question set. 2: step 0 is cut, clef fails with a message that quotes the state, hosted is skipped.
        var primary = new FallbackTimeTests.WaitingClient();
        var clef = scenario == 2
            ? StubDecisionClient.Failing(new DecisionError(DecisionErrorKind.Server, Sentinel + "error-message"))
            : StubDecisionClient.Answering(answers);
        var hosted = scenario == 1
            ? StubDecisionClient.Responding(request => StubDecisionClient.Respond(new QuestionSetDefinition([.. request.Definition.Questions]), answers))
            : StubDecisionClient.Answering(answers);
        var options = new FallbackOptions { Budget = TimeSpan.FromSeconds(5) };
        options.Steps.Add(new FallbackStep("clef", clef) { ExpectedLatency = TimeSpan.FromSeconds(scenario == 1 ? 1 : 0.3) });
        options.Steps.Add(new FallbackStep("hosted", hosted) { ExpectedLatency = TimeSpan.FromSeconds(scenario switch { 0 => 0.5, 1 => 0.2, _ => 10 }) });
        using var fallback = new FallbackDecisionClient(primary, options, logs.Factory.CreateLogger("test"), time);
        using var stage = new OpenTelemetryDecisionClient(fallback);

        var call = stage.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(Sentinel + "state"))).AsTask();
        await primary.Started.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(scenario == 1 ? 4.8 : 4.7));
        await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(1017, logs.EventIds);
        Assert.Equal("DeadlineExceeded", logs.Records.First(record => record.Id.Id == 1017).StructuredState!.Single(pair => string.Equals(pair.Key, "ErrorKind", StringComparison.Ordinal)).Value);
        foreach (var record in logs.Records)
        {
            Assert.DoesNotContain(Sentinel, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(record.StructuredState ?? [], pair => $"{pair.Value}".Contains(Sentinel, StringComparison.Ordinal));
        }

        Assert.NotEmpty(capture.Points(Counter));
        var tags = capture.Span().TagObjects.Concat(capture.StartTags()).Concat(capture.AllPoints.SelectMany(point => point.Tags));
        Assert.DoesNotContain(tags, tag => $"{tag.Key}={tag.Value}".Contains(Sentinel, StringComparison.Ordinal));
    }
}
