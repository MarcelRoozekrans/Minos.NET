using Microsoft.Extensions.Logging;

namespace Minos.SystemOne;

/// <summary>
/// The log events of the <c>/v1/systemone</c> protocol's raw calls, in <see cref="DecisionLog.Category"/>. They carry metadata
/// only, as the events in <see cref="DecisionLog"/> do. Their event ids continue that class's block, so the ids stay unique.
/// </summary>
internal static partial class SystemOneLog
{
    /// <summary>The operation name of <c>SystemOneDecisionClient.EvaluateAsync</c> over a raw request.</summary>
    public const string Evaluate = "evaluate";

    /// <summary>The operation name of <c>SystemOneDecisionClient.ListModelsAsync</c>.</summary>
    public const string ListModels = "list-models";

    /// <summary>A model listing succeeded.</summary>
    [LoggerMessage(
        EventId = 1004,
        EventName = nameof(ModelsListed),
        Level = LogLevel.Debug,
        Message = "Minos " + ListModels + " via {Provider} succeeded: {ModelCount} models in {DurationMs} ms.")]
    public static partial void ModelsListed(ILogger logger, string provider, int modelCount, double durationMs);

    /// <summary>A model listing failed, after any retries.</summary>
    [LoggerMessage(
        EventId = 1005,
        EventName = nameof(ModelsListFailed),
        Level = LogLevel.Warning,
        Message = "Minos " + ListModels + " via {Provider} failed with {ErrorKind}, status {StatusCode}, in {DurationMs} ms: {ErrorMessage}")]
    public static partial void ModelsListFailed(
        ILogger logger, string provider, DecisionErrorKind errorKind, int? statusCode, double durationMs, string errorMessage);
}
