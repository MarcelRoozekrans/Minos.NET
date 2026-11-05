using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Minos;

/// <summary>
/// The client's log events, in the <see cref="Category"/> category. They carry metadata only: never the state,
/// instructions, criteria, answers, API key, a header value or <see cref="DecisionError.Detail"/>.
/// </summary>
/// <remarks>
/// Each template keeps its placeholders in parameter order. An event with at most six fields becomes a
/// <c>LoggerMessage.Define</c> call; <see cref="EvaluationFailed"/>, with seven, becomes a generated state struct.
/// </remarks>
internal static partial class DecisionLog
{
    /// <summary>The logger category: <c>loggerFactory.CreateLogger(Category)</c>.</summary>
    public const string Category = "Minos.DecisionClient";

    /// <summary>The operation name of every neutral <see cref="IDecisionClient.EvaluateAsync"/> call, typed and built sets included.</summary>
    public const string EvaluateSet = "evaluate-set";

    /// <summary>
    /// What is logged instead of an <see cref="DecisionErrorKind.InvalidResponse"/> error's message, which can quote the
    /// response: an unknown option's value, or the characters a JSON reader rejected.
    /// </summary>
    public const string UnreadableResponse = "The response could not be read.";

    /// <summary>
    /// What is logged instead of a <see cref="DecisionErrorKind.Network"/> error's message, which is the transport's exception
    /// text and can echo the request: its URL or headers.
    /// </summary>
    public const string RequestNotSent = "The request could not be sent.";

    /// <summary>
    /// The <c>ErrorKind</c> a routing stage logs for a call it cut at the deadline or at a step's time limit, whatever
    /// failure the client made of the cancellation.
    /// </summary>
    public const string DeadlineExceeded = "DeadlineExceeded";

    /// <summary>The <c>ErrorKind</c> a routing stage logs for a client that answered a different question set than it was asked.</summary>
    public const string DefinitionMismatch = "DefinitionMismatch";

    /// <summary>An evaluation succeeded; <c>Cached</c> is whether a cache stage answered it.</summary>
    [LoggerMessage(
        EventId = 1001,
        EventName = nameof(EvaluationSucceeded),
        Level = LogLevel.Debug,
        Message = "Minos {Operation} on {Model} via {Provider} succeeded: {QuestionCount} questions in {DurationMs} ms, cached {Cached}.")]
    public static partial void EvaluationSucceeded(
        ILogger logger, string operation, string model, string provider, int questionCount, double durationMs, bool cached);

    /// <summary>An evaluation failed, after any retries; the message is the error's <see cref="SafeMessage"/>.</summary>
    [LoggerMessage(
        EventId = 1002,
        EventName = nameof(EvaluationFailed),
        Level = LogLevel.Warning,
        Message = "Minos {Operation} on {Model} failed with {ErrorKind}, status {StatusCode}, request id {RequestId}, in {DurationMs} ms: {ErrorMessage}")]
    public static partial void EvaluationFailed(
        ILogger logger, string operation, string model, DecisionErrorKind errorKind, int? statusCode, string? requestId, double durationMs, string errorMessage);

    /// <summary>An attempt failed with an error the retry policy will retry.</summary>
    [LoggerMessage(
        EventId = 1003,
        EventName = nameof(AttemptRetrying),
        Level = LogLevel.Warning,
        Message = "Minos attempt {Attempt} failed with {ErrorKind}, status {StatusCode}, retry-after {RetryAfter}; retrying.")]
    public static partial void AttemptRetrying(ILogger logger, int attempt, DecisionErrorKind errorKind, int? statusCode, TimeSpan? retryAfter);

    /// <summary>An operation threw: a programming error, since every call failure is returned as a <see cref="DecisionError"/>.</summary>
    [LoggerMessage(
        EventId = 1006,
        EventName = nameof(UnexpectedException),
        Level = LogLevel.Error,
        Message = "Minos {Operation} threw an unexpected exception.")]
    public static partial void UnexpectedException(ILogger logger, string operation, Exception exception);

    /// <summary>A cache stage's distributed cache threw on a read; the stage treats it as a miss.</summary>
    [LoggerMessage(
        EventId = 1007,
        EventName = nameof(CacheReadFailed),
        Level = LogLevel.Warning,
        Message = "Minos cache read via {Provider} failed; the call goes to the provider.")]
    public static partial void CacheReadFailed(ILogger logger, string provider, Exception exception);

    /// <summary>A cache stage's distributed cache threw on a write; the answer is returned without being stored there.</summary>
    [LoggerMessage(
        EventId = 1008,
        EventName = nameof(CacheWriteFailed),
        Level = LogLevel.Warning,
        Message = "Minos cache write via {Provider} failed; the answer is returned uncached.")]
    public static partial void CacheWriteFailed(ILogger logger, string provider, Exception exception);

    /// <summary>
    /// A decision recorder dropped records because its queue was full. The count is of the drops since the last such event;
    /// the recorder logs it from its writer, so a caller is never slowed by it.
    /// </summary>
    [LoggerMessage(
        EventId = 1009,
        EventName = nameof(RecordsDropped),
        Level = LogLevel.Warning,
        Message = "Minos recorder dropped {Count} records: its queue was full.")]
    public static partial void RecordsDropped(ILogger logger, long count);

    /// <summary>
    /// A decision recorder's sink threw; the item was counted and skipped. Only the exception's type name is logged:
    /// its message could quote the state, a decision id or an erasure key.
    /// </summary>
    [LoggerMessage(
        EventId = 1010,
        EventName = nameof(SinkCallFailed),
        Level = LogLevel.Warning,
        Message = "Minos recorder sink call {Operation} failed with {ExceptionType}; the item was skipped.")]
    public static partial void SinkCallFailed(ILogger logger, string operation, string exceptionType);

    /// <summary>A recording stage's redactor threw; no record was kept, and the decision is unaffected. Only the exception's type name is logged.</summary>
    [LoggerMessage(
        EventId = 1011,
        EventName = nameof(RedactionFailed),
        Level = LogLevel.Warning,
        Message = "Minos recording redaction failed with {ExceptionType}; no record was kept.")]
    public static partial void RedactionFailed(ILogger logger, string exceptionType);

    /// <summary>
    /// A decision recorder's dispose did not drain its queue within its flush timeout. The items the sink never
    /// completed are counted by kind; a lost erasure or flush made its caller's task fail.
    /// </summary>
    [LoggerMessage(
        EventId = 1012,
        EventName = nameof(FlushTimedOut),
        Level = LogLevel.Error,
        Message = "Minos recorder flush timed out after {TimeoutMs} ms; lost {Records} records, {Outcomes} outcomes, {Erasures} erasures and {Flushes} flushes.")]
    public static partial void FlushTimedOut(ILogger logger, double timeoutMs, int records, int outcomes, int erasures, int flushes);

    /// <summary>
    /// A recording stage failed to record a decision after the call was made, for example because a metric listener threw;
    /// no record was kept, and the call's result is returned unchanged. Only the exception's type name is logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1013,
        EventName = nameof(RecordingFailed),
        Level = LogLevel.Warning,
        Message = "Minos recording failed with {ExceptionType}; no record was kept, and the decision is unaffected.")]
    public static partial void RecordingFailed(ILogger logger, string exceptionType);

    /// <summary>The message to log for <paramref name="error"/>: its own, except where it can carry request or response text.</summary>
    /// <param name="error">The error.</param>
    /// <returns><see cref="DecisionError.Message"/>, or a fixed text for <see cref="DecisionErrorKind.InvalidResponse"/> (<see cref="UnreadableResponse"/>) and <see cref="DecisionErrorKind.Network"/> (<see cref="RequestNotSent"/>).</returns>
    public static string SafeMessage(DecisionError error)
        => error.Kind switch
        {
            DecisionErrorKind.InvalidResponse => UnreadableResponse,
            DecisionErrorKind.Network => RequestNotSent,
            _ => error.Message,
        };

    /// <summary>
    /// Whether an operation could log anything: Debug for its success, Warning for its failure, Error for an unexpected
    /// exception. Checked per call, so a logger whose filter changes at run time is followed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <returns><see langword="true"/> when any of the three levels is enabled.</returns>
    public static bool IsAnyEnabled(ILogger logger)
        => logger.IsEnabled(LogLevel.Debug) || logger.IsEnabled(LogLevel.Warning) || logger.IsEnabled(LogLevel.Error);

    /// <summary>Takes a timestamp only when a success or failure event could be written.</summary>
    /// <param name="logger">The logger, or <see langword="null"/>.</param>
    /// <returns>A <see cref="Stopwatch"/> timestamp, or 0 when neither Debug nor Warning is enabled.</returns>
    public static long StartTiming(ILogger? logger)
        => logger is not null && (logger.IsEnabled(LogLevel.Debug) || logger.IsEnabled(LogLevel.Warning)) ? Stopwatch.GetTimestamp() : 0;

    /// <summary>The milliseconds since <paramref name="started"/>, or 0 when no timestamp was taken.</summary>
    /// <param name="started">A <see cref="StartTiming"/> result.</param>
    /// <returns>The elapsed milliseconds.</returns>
    public static double ElapsedMilliseconds(long started)
        => started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>
    /// An exception filter: logs <see cref="UnexpectedException"/> unless <paramref name="exception"/> is the caller's
    /// own cancellation, and returns <see langword="false"/>, so the exception propagates unchanged.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="operation">The operation name.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="ct">The caller's token.</param>
    /// <returns><see langword="false"/>.</returns>
    public static bool LogUnexpected(ILogger logger, string operation, Exception exception, CancellationToken ct)
    {
        if (!(exception is OperationCanceledException && ct.IsCancellationRequested))
        {
            UnexpectedException(logger, operation, exception);
        }

        return false;
    }

    /// <summary>
    /// An escalation stage re-asked its unsure questions on the target, and the target answered them. Only the client names
    /// and the number of questions are logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1014,
        EventName = nameof(Escalated),
        Level = LogLevel.Information,
        Message = "Minos escalated {QuestionCount} questions to {Target}.")]
    public static partial void Escalated(ILogger logger, int questionCount, string target);

    /// <summary>
    /// An escalation stage did not call the target because too little time was left before the deadline; the primary's
    /// answers stand. Only the target's name and the number of unsure questions are logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1015,
        EventName = nameof(EscalationSkippedForTime),
        Level = LogLevel.Information,
        Message = "Minos did not escalate {QuestionCount} questions to {Target}: too little time was left.")]
    public static partial void EscalationSkippedForTime(ILogger logger, int questionCount, string target);

    /// <summary>
    /// An escalation stage's target failed, ran past the deadline or answered another question set; the primary's answers
    /// stand. Only the target's name and the error kind are logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1016,
        EventName = nameof(EscalationTargetFailed),
        Level = LogLevel.Warning,
        Message = "Minos escalation target {Target} failed with {ErrorKind}; the primary's answers stand.")]
    public static partial void EscalationTargetFailed(ILogger logger, string target, string errorKind);

    /// <summary>
    /// A fallback stage's step failed with an error the chain moves on from, and a later step is left to move on to; the
    /// last step's failure logs only <see cref="FallbackExhausted"/>. A cut step logs <see cref="DeadlineExceeded"/>
    /// and one that answered another question set <see cref="DefinitionMismatch"/>. Only the step's name and the error kind
    /// are logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1017,
        EventName = nameof(FallbackStepFailed),
        Level = LogLevel.Warning,
        Message = "Minos fallback step {Step} failed with {ErrorKind}.")]
    public static partial void FallbackStepFailed(ILogger logger, string step, string errorKind);

    /// <summary>A fallback stage did not call a step because too little time was left before the deadline. Only the step's name is logged.</summary>
    [LoggerMessage(
        EventId = 1018,
        EventName = nameof(FallbackStepSkippedForTime),
        Level = LogLevel.Information,
        Message = "Minos skipped fallback step {Step}: too little time was left.")]
    public static partial void FallbackStepSkippedForTime(ILogger logger, string step);

    /// <summary>
    /// A fallback stage went past its first step and no step answered; the error of the last step called is returned.
    /// Only the number of steps after the first that were tried or skipped and the returned error's kind are logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1019,
        EventName = nameof(FallbackExhausted),
        Level = LogLevel.Warning,
        Message = "Minos fallback found no answer after {StepsTried} more steps; the last error was {ErrorKind}.")]
    public static partial void FallbackExhausted(ILogger logger, int stepsTried, string errorKind);

    /// <summary>
    /// A fallback stage's step after the first answered. Only the step's name and the number of steps after the first that
    /// were tried or skipped, the answering one included, are logged.
    /// </summary>
    [LoggerMessage(
        EventId = 1020,
        EventName = nameof(FellBack),
        Level = LogLevel.Information,
        Message = "Minos fell back to {Step} after {StepsTried} more steps.")]
    public static partial void FellBack(ILogger logger, string step, int stepsTried);
}
