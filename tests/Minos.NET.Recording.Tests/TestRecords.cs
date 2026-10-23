using System.Text;
using System.Text.Json;
using Minos.Recording.RecordFormat;

namespace Minos.Recording.Tests;

/// <summary>Records, outcomes and line text for the recorder and sink tests.</summary>
internal static class TestRecords
{
    internal static readonly DateTimeOffset T0 = new(2026, 10, 22, 9, 30, 0, TimeSpan.FromHours(2));

    internal static QuestionSetDefinition Definition() => new(
        QuestionDefinition.Noul("urgent", "Does the customer need action today?"),
        QuestionDefinition.Choice("topic", "Which topic is this message about?",
            new OptionDefinition("card_arrival", "The card has not arrived yet."),
            new OptionDefinition("card_not_working", "The card arrived but does not work.")));

    internal static DecisionRecord Record(
        string id = "d-1", string? erasureKey = "customer-7", string state = "My card has not arrived.", DecisionUsage? usage = null)
        => new(
            id,
            erasureKey,
            T0,
            TimeSpan.FromMilliseconds(250),
            "openai",
            "https://api.example.test/v1/",
            "model-default",
            null,
            "model-served",
            "resp-1",
            null,
            usage ?? new DecisionUsage { InputTokens = 120, OutputTokens = 14 },
            isCached: false,
            Definition(),
            DecisionContent.FromString(state),
            imageCount: 0,
            [],
            [
                new RecordedAnswer("urgent", QuestionKind.Noul, -1, 0.25, 0, 0.5, []),
                new RecordedAnswer("topic", QuestionKind.Choice, 0, 0, 0, 0.75, [0.75, 0.25]),
            ],
            errorKind: null,
            []);

    internal static DecisionOutcome Signal(string name = "clicked") => DecisionOutcome.Signal("mailer", T0.AddHours(1), name, 1);

    internal static DecisionOutcome Confirmed() => DecisionOutcome.Confirmed(
        "reviewer-ui",
        T0.AddMinutes(5),
        new Dictionary<string, ConfirmedAnswer> { ["urgent"] = ConfirmedAnswer.ForNoul(true) });

    /// <summary>A record's line: two records are the same when they write the same line.</summary>
    internal static string Line(DecisionRecord record) => Json(w => RecordLine.Write(w, record));

    /// <summary>An outcome's line, for a decision id.</summary>
    internal static string Line(string decisionId, DecisionOutcome outcome) => Json(w => RecordLine.WriteOutcome(w, decisionId, outcome));

    /// <summary>The decisions as lines: each record's, then its outcomes'.</summary>
    internal static string[] Lines(IEnumerable<RecordedDecision> decisions)
        => [.. decisions.SelectMany(d => d.Outcomes.Select(o => Line(d.Record.DecisionId, o)).Prepend(Line(d.Record)))];

    private static string Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        RecordLine.WriteTo(buffer, write);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
