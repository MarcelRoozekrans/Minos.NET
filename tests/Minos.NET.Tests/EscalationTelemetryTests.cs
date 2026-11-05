using System.Diagnostics;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;
using Fake = Minos.Tests.EscalationTests.Fake;

namespace Minos.Tests;

/// <summary>The escalation stage's span tags and counter, read through <c>UseOpenTelemetry</c>'s stage, and that no state reaches telemetry.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class EscalationTelemetryTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static readonly Dictionary<string, QuestionAnswer> Unsure = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> Confident = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.08, 0.02]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["department"] = QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
    };

    private static EscalatingDecisionClient Stage(IDecisionClient primary, IDecisionClient target, TimeProvider? time = null, Action<EscalationOptions>? configure = null)
    {
        var options = new EscalationOptions { TargetName = "hosted" };
        configure?.Invoke(options);
        return new EscalatingDecisionClient(primary, target, options, timeProvider: time);
    }

    [Fact]
    public async Task An_escalation_tags_the_span_with_the_count_and_the_counter_with_target_and_reason()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(Stage(
            new Fake(request => Fake.Respond(request.Definition, Unsure)), Fake.Answering(TargetAnswers)));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        var span = capture.Span();
        Assert.Equal(1, span.GetTagItem("minos.escalation.escalated"));
        Assert.Null(span.GetTagItem("minos.escalation.skipped"));
        var point = capture.OnlyPoint("minos.escalations");
        Assert.Equal(1, point.Value);
        Assert.Equal("hosted", point.Tag("minos.escalation.target"));
        Assert.Equal("escalated", point.Tag("minos.escalation.reason"));
    }

    [Fact]
    public async Task A_cached_copy_of_an_escalated_response_carries_no_escalation_telemetry()
    {
        using var capture = new TelemetryCapture();
        var escalating = Stage(new Fake(request => Fake.Respond(request.Definition, Unsure)), Fake.Answering(TargetAnswers));
        var escalated = (await escalating.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;
        var response = escalated.AsCached();
        using var stage = new OpenTelemetryDecisionClient(new Fake(_ => response));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        Assert.NotNull(escalated.EscalatedTag);
        Assert.Same(escalated.Path, response.Path);
        Assert.Null(response.EscalatedTag);
        Assert.Null(response.SkippedTag);
        Assert.Null(response.EscalationEvents);
        Assert.Null(response.EscalationTargetTag);
        Assert.Null(response.EscalationReasonTag);
        Assert.Null(capture.Span().GetTagItem("minos.escalation.escalated"));
        Assert.Empty(capture.Points("minos.escalations"));
    }

    [Fact]
    public async Task A_copy_that_shares_an_escalated_path_reports_no_escalation_telemetry()
    {
        var escalating = Stage(new Fake(request => Fake.Respond(request.Definition, Unsure)), Fake.Answering(TargetAnswers));
        var escalated = (await escalating.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        // A custom stage that rebuilds the response and keeps the path, as a custom cache stage can.
        var copy = new DecisionResponse(escalated.Definition, [.. escalated.Answers], escalated.Model, escalated.Usage) { Path = escalated.Path };

        Assert.Equal(1, escalated.EscalationEvents);
        Assert.Same(escalated.Path, copy.Path);
        Assert.Null(copy.EscalatedTag);
        Assert.Null(copy.SkippedTag);
        Assert.Null(copy.EscalationEvents);
        Assert.Null(copy.EscalationTargetTag);
        Assert.Null(copy.EscalationReasonTag);
    }

    [Fact]
    public async Task Without_an_unsure_question_the_path_holds_no_escalation_telemetry()
    {
        var escalating = Stage(new Fake(request => Fake.Respond(request.Definition, Confident)), Fake.Answering(TargetAnswers));

        var response = (await escalating.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.NotNull(response.Path);
        Assert.Null(response.Path.Escalation);
        Assert.Null(response.EscalationEvents);
    }

    [Fact]
    public async Task A_target_failure_tags_the_span_skipped_and_the_counter_target_failed()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(Stage(
            new Fake(request => Fake.Respond(request.Definition, Unsure)),
            new Fake(_ => null)));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        var span = capture.Span();
        Assert.Null(span.GetTagItem("minos.escalation.escalated"));
        Assert.Equal("target_failed", span.GetTagItem("minos.escalation.skipped"));
        var point = capture.OnlyPoint("minos.escalations");
        Assert.Equal("hosted", point.Tag("minos.escalation.target"));
        Assert.Equal("target_failed", point.Tag("minos.escalation.reason"));
    }

    [Fact]
    public async Task No_time_left_tags_the_span_skipped_and_the_counter_no_time_left()
    {
        using var capture = new TelemetryCapture();
        var start = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(start);
        using var stage = new OpenTelemetryDecisionClient(Stage(
            new Fake(request => Fake.Respond(request.Definition, Unsure)),
            new Fake(_ => null),
            time,
            o => o.ExpectedLatency = TimeSpan.FromSeconds(2)));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State) { Deadline = start.AddSeconds(1) });

        Assert.Equal("no_time_left", capture.Span().GetTagItem("minos.escalation.skipped"));
        var point = capture.OnlyPoint("minos.escalations");
        Assert.Equal("hosted", point.Tag("minos.escalation.target"));
        Assert.Equal("no_time_left", point.Tag("minos.escalation.reason"));
    }

    [Fact]
    public async Task Without_an_unsure_question_the_span_has_neither_tag_and_the_counter_no_measurement()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(Stage(
            new Fake(request => Fake.Respond(request.Definition, Confident)), new Fake(_ => null)));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        var span = capture.Span();
        Assert.Null(span.GetTagItem("minos.escalation.escalated"));
        Assert.Null(span.GetTagItem("minos.escalation.skipped"));
        Assert.Empty(capture.Points("minos.escalations"));
    }

    [Fact]
    public async Task A_plain_response_has_neither_tag_and_no_counter_measurement()
    {
        using var capture = new TelemetryCapture();
        using var stage = new OpenTelemetryDecisionClient(new Fake(request => Fake.Respond(request.Definition, Confident)));

        await stage.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        Assert.Null(capture.Span().GetTagItem("minos.escalation.escalated"));
        Assert.Empty(capture.Points("minos.escalations"));
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
        var answers = new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal) { [Sentinel + "key"] = QuestionAnswer.Choice(0, 0.5, [0.5, 0.5]) };
        var start = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        IDecisionClient target = scenario switch
        {
            0 => Fake.Answering(new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal) { [Sentinel + "key"] = QuestionAnswer.Choice(1, 0.9, [0.1, 0.9]) }),
            _ => new Fake(_ => null),
        };
        var options = new EscalationOptions { TargetName = "hosted", ExpectedLatency = TimeSpan.FromSeconds(2) };
        using var escalating = new EscalatingDecisionClient(
            new Fake(request => Fake.Respond(request.Definition, answers)), target, options, logs.Factory.CreateLogger("test"), new FakeTimeProvider(start));
        using var stage = new OpenTelemetryDecisionClient(escalating);

        await stage.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(Sentinel + "state"))
        {
            Deadline = scenario == 2 ? start.AddSeconds(1) : null,
        });

        Assert.NotEmpty(logs.Records);
        foreach (var record in logs.Records)
        {
            Assert.DoesNotContain(Sentinel, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(record.StructuredState ?? [], pair => $"{pair.Value}".Contains(Sentinel, StringComparison.Ordinal));
        }

        var tags = capture.Span().TagObjects.Concat(capture.StartTags()).Concat(capture.AllPoints.SelectMany(point => point.Tags));
        Assert.DoesNotContain(tags, tag => $"{tag.Key}={tag.Value}".Contains(Sentinel, StringComparison.Ordinal));
    }
}
