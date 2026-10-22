using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using ZeroAlloc.Results;

namespace Minos.Recording.Tests;

/// <summary>Requests, responses and recorders for the recording stage's tests.</summary>
internal static class Stage
{
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    internal static readonly DecisionClientMetadata Metadata = new("openai", new Uri("https://api.example.test/v1/"), "model-default");

    internal static DecisionRequest Request(string state = "My card has not arrived.") => new(TestRecords.Definition(), state);

    /// <summary>A success with every member set: model, usage with cost, response id and upstream provider.</summary>
    internal static DecisionResponse Answer(bool isCached = false) => new(
        TestRecords.Definition(),
        [QuestionAnswer.Noul(0.9), QuestionAnswer.Choice(1, 0.7, [0.3, 0.7])],
        model: "model-served",
        usage: new DecisionUsage { InputTokens = 120, OutputTokens = 14, Cost = 0.0012 })
    {
        Id = "resp-1",
        UpstreamProvider = "Together",
        IsCached = isCached,
    };

    internal static Result<DecisionResponse, DecisionError> Success() => Result<DecisionResponse, DecisionError>.Success(Answer());

    internal static Result<DecisionResponse, DecisionError> Error(string message = "The server failed.")
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, message));

    internal static Result<DecisionResponse, DecisionError> Refusal() => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(
        DecisionErrorKind.Refused,
        "The model refused.",
        [new QuestionFailure("refused", "urgent", "No."), new QuestionFailure("refused", "topic", "No.")]));

    internal static (DecisionRecorder Recorder, FakeLogCollector Logs, ILoggerFactory Factory) Recorder(
        IDecisionRecordSink sink, RecordingOptions? options = null, TimeProvider? time = null)
    {
        var logs = new FakeLogCollector();
        var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(logs)));
        return (new DecisionRecorder(sink, options, factory, time), logs, factory);
    }

    internal static string? Field(FakeLogRecord record, string key)
        => record.StructuredState!.Single(pair => string.Equals(pair.Key, key, StringComparison.Ordinal)).Value;
}
