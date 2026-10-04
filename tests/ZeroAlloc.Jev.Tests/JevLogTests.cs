using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ZeroAlloc.Jev.Tests;

/// <summary>The client's log events: their ids, names, levels, fields and guards.</summary>
public sealed class JevLogTests
{
    [Fact]
    public void EveryEvent_HasItsIdNameAndLevel()
    {
        var logger = new FakeLogger();

        JevLog.EvaluationSucceeded(logger, JevLog.Evaluate, "m", JevProvider.TypeSafe, 1, 2.5);
        JevLog.EvaluationFailed(logger, JevLog.Evaluate, "m", JevErrorKind.Server, 500, 2.5, "The API returned HTTP 500.");
        JevLog.AttemptRetrying(logger, 1, JevErrorKind.Server, 500, null);
        JevLog.ModelsListed(logger, JevProvider.TypeSafe, 2, 2.5);
        JevLog.ModelsListFailed(logger, JevProvider.TypeSafe, JevErrorKind.Unauthorized, 401, 2.5, "The API returned HTTP 401.");
        JevLog.UnexpectedException(logger, JevLog.ListModels, new InvalidOperationException("bug"));

        (int, string, LogLevel)[] expected =
        [
            (1001, "EvaluationSucceeded", LogLevel.Debug),
            (1002, "EvaluationFailed", LogLevel.Warning),
            (1003, "AttemptRetrying", LogLevel.Warning),
            (1004, "ModelsListed", LogLevel.Debug),
            (1005, "ModelsListFailed", LogLevel.Warning),
            (1006, "UnexpectedException", LogLevel.Error),
        ];
        var actual = logger.Collector.GetSnapshot().Select(record => (record.Id.Id, record.Id.Name ?? string.Empty, record.Level)).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GeneratedEvents_CarryTheirFields()
    {
        var logger = new FakeLogger();

        JevLog.EvaluationSucceeded(logger, JevLog.EvaluateBuiltSet, "jev-latest", JevProvider.OpenRouter, 4, 2.5);
        var succeeded = logger.LatestRecord;
        Assert.Equal("evaluate-built-set", LogAssert.Field(succeeded, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(succeeded, "Model"));
        Assert.Equal("OpenRouter", LogAssert.Field(succeeded, "Provider"));
        Assert.Equal("4", LogAssert.Field(succeeded, "QuestionCount"));
        Assert.NotNull(LogAssert.Field(succeeded, "DurationMs"));

        JevLog.AttemptRetrying(logger, 2, JevErrorKind.RateLimited, 429, TimeSpan.FromSeconds(3));
        var retrying = logger.LatestRecord;
        Assert.Equal("2", LogAssert.Field(retrying, "Attempt"));
        Assert.Equal("RateLimited", LogAssert.Field(retrying, "ErrorKind"));
        Assert.Equal("429", LogAssert.Field(retrying, "StatusCode"));
        Assert.Equal("00:00:03", LogAssert.Field(retrying, "RetryAfter"));

        JevLog.ModelsListed(logger, JevProvider.TypeSafe, 7, 2.5);
        Assert.Equal("7", LogAssert.Field(logger.LatestRecord, "ModelCount"));

        JevLog.ModelsListFailed(logger, JevProvider.TypeSafe, JevErrorKind.Network, null, 2.5, "Connection refused");
        var failed = logger.LatestRecord;
        Assert.Equal("Network", LogAssert.Field(failed, "ErrorKind"));
        Assert.Null(LogAssert.Field(failed, "StatusCode"));
        Assert.Equal("Connection refused", LogAssert.Field(failed, "ErrorMessage"));
    }

    [Fact]
    public void EvaluationSucceeded_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        JevLog.EvaluationSucceeded(logger, JevLog.Evaluate, "jev-latest", JevProvider.TypeSafe, 3, 2.5);

        var record = logger.LatestRecord;
        Assert.Equal("evaluate", LogAssert.Field(record, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(record, "Model"));
        Assert.Equal("TypeSafe", LogAssert.Field(record, "Provider"));
        Assert.Equal("3", LogAssert.Field(record, "QuestionCount"));
        Assert.Equal(2.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal(
            "Jev {Operation} on {Model} via {Provider} succeeded: {QuestionCount} questions in {DurationMs} ms.",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Jev evaluate on jev-latest via TypeSafe succeeded: 3 questions in 2.5 ms.", record.Message);
    }

    [Fact]
    public void AttemptRetrying_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        JevLog.AttemptRetrying(logger, 2, JevErrorKind.RateLimited, 429, TimeSpan.FromSeconds(3));

        var record = logger.LatestRecord;
        Assert.Equal("2", LogAssert.Field(record, "Attempt"));
        Assert.Equal("RateLimited", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("429", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("00:00:03", LogAssert.Field(record, "RetryAfter"));
        Assert.Equal(
            "Jev attempt {Attempt} failed with {ErrorKind}, status {StatusCode}, retry-after {RetryAfter}; retrying.",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Jev attempt 2 failed with RateLimited, status 429, retry-after 00:00:03; retrying.", record.Message);
    }

    [Fact]
    public void AttemptRetrying_LogsANullRetryAfter_WhenTheServerSentNone()
    {
        var logger = new FakeLogger();

        JevLog.AttemptRetrying(logger, 1, JevErrorKind.Server, 500, null);

        var record = logger.LatestRecord;
        Assert.Null(LogAssert.Field(record, "RetryAfter"));
        Assert.Equal("500", LogAssert.Field(record, "StatusCode"));
        Assert.Equal("Jev attempt 1 failed with Server, status 500, retry-after (null); retrying.", record.Message);
    }

    [Fact]
    public void ModelsListed_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        JevLog.ModelsListed(logger, JevProvider.OpenRouter, 7, 2.5);

        var record = logger.LatestRecord;
        Assert.Equal("OpenRouter", LogAssert.Field(record, "Provider"));
        Assert.Equal("7", LogAssert.Field(record, "ModelCount"));
        Assert.Equal(2.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal(
            "Jev list-models via {Provider} succeeded: {ModelCount} models in {DurationMs} ms.",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Jev list-models via OpenRouter succeeded: 7 models in 2.5 ms.", record.Message);
    }

    [Fact]
    public void ModelsListFailed_CarriesItsValuesAndTemplate()
    {
        var logger = new FakeLogger();

        JevLog.ModelsListFailed(logger, JevProvider.OpenRouter, JevErrorKind.Unauthorized, 401, 2.5, "The API returned HTTP 401.");

        var record = logger.LatestRecord;
        Assert.Equal("OpenRouter", LogAssert.Field(record, "Provider"));
        Assert.Equal("Unauthorized", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("401", LogAssert.Field(record, "StatusCode"));
        Assert.Equal(2.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal("The API returned HTTP 401.", LogAssert.Field(record, "ErrorMessage"));
        Assert.Equal(
            "Jev list-models via {Provider} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}",
            LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Jev list-models via OpenRouter failed with Unauthorized, status 401, in 2.5 ms: The API returned HTTP 401.", record.Message);
    }

    [Fact]
    public void UnexpectedException_CarriesItsOperationAndTemplate()
    {
        var logger = new FakeLogger();

        JevLog.UnexpectedException(logger, JevLog.ListModels, new InvalidOperationException("bug"));

        var record = logger.LatestRecord;
        Assert.Equal("list-models", LogAssert.Field(record, "Operation"));
        Assert.Equal("Jev {Operation} threw an unexpected exception.", LogAssert.Field(record, "{OriginalFormat}"));
        Assert.Equal("Jev list-models threw an unexpected exception.", record.Message);
    }

    [Fact]
    public void EvaluationFailed_CarriesItsSixFields()
    {
        var logger = new FakeLogger();

        JevLog.EvaluationFailed(logger, JevLog.EvaluateTyped, "jev-latest", JevErrorKind.RateLimited, 429, 12.5, "The API returned HTTP 429.");

        var record = logger.LatestRecord;
        Assert.Equal("evaluate-typed", LogAssert.Field(record, "Operation"));
        Assert.Equal("jev-latest", LogAssert.Field(record, "Model"));
        Assert.Equal("RateLimited", LogAssert.Field(record, "ErrorKind"));
        Assert.Equal("429", LogAssert.Field(record, "StatusCode"));
        Assert.Equal(12.5.ToString(CultureInfo.InvariantCulture), LogAssert.Field(record, "DurationMs"));
        Assert.Equal(
            "Jev evaluate-typed on jev-latest failed with RateLimited, status 429, in 12.5 ms: The API returned HTTP 429.",
            record.Message);
        Assert.Equal("The API returned HTTP 429.", LogAssert.Field(record, "ErrorMessage"));
        Assert.Equal(
            "Jev {Operation} on {Model} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}",
            LogAssert.Field(record, "{OriginalFormat}"));

        // Provider is fixed per client and retry-after is on AttemptRetrying: neither is a field here.
        var keys = record.StructuredState!.Select(pair => pair.Key).ToArray();
        Assert.DoesNotContain("Provider", keys);
        Assert.DoesNotContain("RetryAfter", keys);
    }

    [Fact]
    public void EvaluationFailed_LogsANullStatus_WhenNoResponseArrived()
    {
        var logger = new FakeLogger();

        JevLog.EvaluationFailed(logger, JevLog.Evaluate, "m", JevErrorKind.Timeout, null, 1, "The request timed out.");

        Assert.Null(LogAssert.Field(logger.LatestRecord, "StatusCode"));
    }

    [Fact]
    public void EvaluationFailed_LogsNothing_WhenWarningIsDisabled()
    {
        var logger = new FakeLogger();
        logger.ControlLevel(LogLevel.Warning, enabled: false);

        JevLog.EvaluationFailed(logger, JevLog.Evaluate, "m", JevErrorKind.Server, 500, 1, "x");

        Assert.Equal(0, logger.Collector.Count);
    }

    [Theory]
    [InlineData(JevErrorKind.Validation, "The API returned HTTP 422.")]
    [InlineData(JevErrorKind.Timeout, "The request timed out.")]
    [InlineData(JevErrorKind.Unsupported, "Model listing is only available on TypeSafe's API.")]
    public void SafeMessage_KeepsALibraryMessage(JevErrorKind kind, string message)
        => Assert.Equal(message, JevLog.SafeMessage(new JevError(kind, message)));

    [Fact]
    public void SafeMessage_ReplacesANetworkMessage_WhichCanEchoTheRequest()
        => Assert.Equal(
            JevLog.RequestNotSent,
            JevLog.SafeMessage(new JevError(JevErrorKind.Network, "Connection refused to https://host/?q=secret")));

    [Fact]
    public void SafeMessage_ReplacesAnInvalidResponseMessage_WhichCanQuoteTheResponse()
        => Assert.Equal(
            JevLog.UnreadableResponse,
            JevLog.SafeMessage(new JevError(JevErrorKind.InvalidResponse, "'secret' is not one of the options.") { StatusCode = 200 }));

    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void IsAnyEnabled_IsTrue_WhenOnlyOneLevelAnOperationEmitsIsEnabled(LogLevel enabled)
    {
        var logger = new FakeLogger();
        foreach (var level in new[] { LogLevel.Debug, LogLevel.Warning, LogLevel.Error })
        {
            logger.ControlLevel(level, enabled: level == enabled);
        }

        Assert.True(JevLog.IsAnyEnabled(logger));
    }

    [Fact]
    public void IsAnyEnabled_IsFalse_WhenNoLevelAnOperationEmitsIsEnabled()
    {
        var none = new FakeLogger();
        none.ControlLevel(LogLevel.Debug, enabled: false);
        none.ControlLevel(LogLevel.Warning, enabled: false);
        none.ControlLevel(LogLevel.Error, enabled: false);

        Assert.True(JevLog.IsAnyEnabled(new FakeLogger()));
        Assert.False(JevLog.IsAnyEnabled(none));
        Assert.False(JevLog.IsAnyEnabled(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    [Fact]
    public void StartTiming_IsZero_WithoutALogger_OrWithDebugAndWarningDisabled()
    {
        var quiet = new FakeLogger();
        quiet.ControlLevel(LogLevel.Debug, enabled: false);
        quiet.ControlLevel(LogLevel.Warning, enabled: false);

        Assert.Equal(0L, JevLog.StartTiming(null));
        Assert.Equal(0L, JevLog.StartTiming(quiet));
        Assert.NotEqual(0L, JevLog.StartTiming(new FakeLogger()));
        Assert.Equal(0d, JevLog.ElapsedMilliseconds(0));
        Assert.True(JevLog.ElapsedMilliseconds(JevLog.StartTiming(new FakeLogger())) >= 0);
    }

    [Fact]
    public void LogUnexpected_LogsAnError_AndReturnsFalse_SoTheExceptionPropagates()
    {
        var logger = new FakeLogger();
        var bug = new InvalidOperationException("bug");

        Assert.False(JevLog.LogUnexpected(logger, JevLog.Evaluate, bug, CancellationToken.None));

        var record = logger.LatestRecord;
        Assert.Equal(1006, record.Id.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("evaluate", LogAssert.Field(record, "Operation"));
        Assert.Same(bug, record.Exception);
    }

    [Fact]
    public void LogUnexpected_SkipsTheCallersOwnCancellation()
    {
        var logger = new FakeLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False(JevLog.LogUnexpected(logger, JevLog.Evaluate, new OperationCanceledException(cancellation.Token), cancellation.Token));

        Assert.Equal(0, logger.Collector.Count);
    }
}
