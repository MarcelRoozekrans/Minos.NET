using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Fake = Minos.Tests.EscalationTests.Fake;

namespace Minos.Tests;

/// <summary>The escalation stage's three log events: their ids, levels and the values they carry.</summary>
public sealed class EscalationLoggingTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static readonly Dictionary<string, QuestionAnswer> Unsure = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["department"] = QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
    };

    private static EscalatingDecisionClient Stage(LogCapture logs, IDecisionClient target, TimeProvider? time = null, TimeSpan expected = default)
        => new(
            new Fake(request => Fake.Respond(request.Definition, Unsure)),
            target,
            new EscalationOptions { TargetName = "hosted", ExpectedLatency = expected },
            logs.Factory.CreateLogger("test"),
            time);

    private static string Value(Microsoft.Extensions.Logging.Testing.FakeLogRecord record, string key)
        => record.StructuredState!.Single(pair => string.Equals(pair.Key, key, StringComparison.Ordinal)).Value!;

    [Fact]
    public async Task An_escalation_logs_the_count_and_the_target()
    {
        using var logs = new LogCapture();
        using var client = Stage(logs, Fake.Answering(TargetAnswers));

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        var record = logs.Only(1014);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("1", Value(record, "QuestionCount"));
        Assert.Equal("hosted", Value(record, "Target"));
        Assert.Equal([1014], logs.EventIds);
    }

    [Fact]
    public async Task A_skip_for_time_logs_the_count_and_the_target()
    {
        using var logs = new LogCapture();
        var start = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        using var client = Stage(logs, new Fake(_ => null), new FakeTimeProvider(start), TimeSpan.FromSeconds(2));

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State) { Deadline = start.AddSeconds(1) });

        var record = logs.Only(1015);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("1", Value(record, "QuestionCount"));
        Assert.Equal("hosted", Value(record, "Target"));
        Assert.Equal([1015], logs.EventIds);
    }

    [Fact]
    public async Task A_target_failure_logs_the_target_and_the_error_kind()
    {
        using var logs = new LogCapture();
        using var client = Stage(logs, new Fake(_ => null));

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        var record = logs.Only(1016);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("hosted", Value(record, "Target"));
        Assert.Equal("Server", Value(record, "ErrorKind"));
        Assert.Equal([1016], logs.EventIds);
    }

    [Fact]
    public async Task A_target_that_answers_another_set_logs_a_target_failure()
    {
        using var logs = new LogCapture();
        using var client = Stage(logs, new Fake(_ => Fake.Respond(QuestionSets.TriageDefinition(), Unsure)));

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        Assert.Equal("DefinitionMismatch", Value(logs.Only(1016), "ErrorKind"));
    }

    [Fact]
    public async Task All_confident_logs_nothing()
    {
        using var logs = new LogCapture();
        using var client = new EscalatingDecisionClient(
            new Fake(request => Fake.Respond(request.Definition, new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal)
            {
                ["is_urgent"] = QuestionAnswer.Noul(0.97),
                ["department"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.08, 0.02]),
                ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
            })),
            new Fake(_ => null),
            new EscalationOptions { TargetName = "hosted" },
            logs.Factory.CreateLogger("test"));

        await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        Assert.Empty(logs.Records);
    }
}
