using System.Security.Cryptography;
using ZeroAlloc.Results;

namespace Minos.Recording;

/// <summary>
/// A stage that hands a record of each decision to a <see cref="DecisionRecorder"/>: the question set, the state after
/// <see cref="RecordingOptions.Redact"/>, the model and endpoint, the answers and usage, or the kind of error. It returns
/// the inner client's result unchanged and never waits for the recorder.
/// </summary>
/// <remarks>
/// <para>
/// The record takes its decision id and erasure key from the <see cref="DecisionRecording"/> scope of the call, or gets a
/// generated UUIDv7 id and no key when there is none. A scope holds one decision: a second request in it with
/// <see cref="DecisionRequest.RetryAttempt"/> 0 throws <see cref="InvalidOperationException"/> before the inner call, the one
/// way the stage fails a call. A request with a higher attempt, from a retry stage outside this one, is recorded under the
/// scope's id with its attempt, and a reader keeps the highest attempt; without a scope, each attempt gets its own id. Add
/// this stage outermost, so a decision is one record and a cache hit is recorded as cached.
/// </para>
/// <para>
/// A record that cannot be queued, because the queue is full or the recorder is disposed, is dropped and counted. A
/// redactor that throws keeps no record and is logged as event 1011 with the exception's type name only. Images are
/// recorded as their count and the SHA-256 of each, never their bytes. Disposing the stage disposes the inner client,
/// never the recorder.
/// </para>
/// </remarks>
public sealed class RecordingDecisionClient : DelegatingDecisionClient
{
    private const string UnknownProvider = "unknown";

    private readonly DecisionRecorder _recorder;
    private readonly TimeProvider _time;
    private readonly string _provider;
    private readonly string? _endpoint;
    private readonly string? _defaultModel;

    /// <summary>Initializes a new instance of the <see cref="RecordingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client whose decisions are recorded.</param>
    /// <param name="recorder">The recorder the records go to. The stage does not dispose it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="recorder"/> is <see langword="null"/>.</exception>
    public RecordingDecisionClient(IDecisionClient innerClient, DecisionRecorder recorder)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
        _time = recorder.Time;
        var metadata = innerClient.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? UnknownProvider;
        _endpoint = metadata?.Endpoint?.AbsoluteUri;
        _defaultModel = metadata?.DefaultModel;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// A decision was already made in the call's <see cref="DecisionRecording"/> scope, and the request's
    /// <see cref="DecisionRequest.RetryAttempt"/> is 0.
    /// </exception>
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (decisionId, erasureKey) = Identify(request);
        var started = _time.GetUtcNow();
        var timestamp = _time.GetTimestamp();
        var pending = InnerClient.EvaluateAsync(request, cancellationToken);
        if (pending.IsCompletedSuccessfully)
        {
            var result = pending.Result;
            Record(request, result, decisionId, erasureKey, started, timestamp);
            return new(result);
        }

        return AwaitAsync(pending, request, decisionId, erasureKey, started, timestamp);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && (serviceType == typeof(IDecisionRecorder) || serviceType == typeof(DecisionRecorder))
            ? _recorder
            : base.GetService(serviceType, serviceKey);
    }

    // The scope's id and key, enforcing one fresh decision per scope before the inner call.
    private static (string DecisionId, string? ErasureKey) Identify(DecisionRequest request)
    {
        if (DecisionRecording.Current is not { } scope)
        {
            return (Guid.CreateVersion7().ToString(), null);
        }

        if (request.RetryAttempt == 0)
        {
            if (!scope.TryClaimFresh())
            {
                throw new InvalidOperationException(
                    "A decision was already made in this recording scope: a scope holds one decision. Begin a new scope for each decision.");
            }
        }
        else
        {
            scope.MarkDecided();
        }

        return (scope.EnsureDecisionId(), scope.ErasureKey);
    }

    private async ValueTask<Result<DecisionResponse, DecisionError>> AwaitAsync(
        ValueTask<Result<DecisionResponse, DecisionError>> pending,
        DecisionRequest request,
        string decisionId,
        string? erasureKey,
        DateTimeOffset started,
        long timestamp)
    {
        var result = await pending.ConfigureAwait(false);
        Record(request, result, decisionId, erasureKey, started, timestamp);
        return result;
    }

    // Builds the record and queues it without waiting. Nothing here fails the call: a redactor's failure is caught, and
    // a full queue drops the record.
    private void Record(
        DecisionRequest request, Result<DecisionResponse, DecisionError> result, string decisionId, string? erasureKey, DateTimeOffset started, long timestamp)
    {
        var latency = _time.GetElapsedTime(timestamp);
        if (!TryRedact(request.State, out var state))
        {
            return;
        }

        var images = request.Images;
        var imageHashes = new string[images.Count];
        for (var i = 0; i < imageHashes.Length; i++)
        {
            imageHashes[i] = Convert.ToHexStringLower(SHA256.HashData(images[i].Data.Span));
        }

        DecisionResponse? response = null;
        DecisionErrorKind? errorKind = null;
        List<RecordedAnswer>? answers = null;
        List<string>? refusedKeys = null;
        if (result.IsSuccess)
        {
            response = result.Value;
            answers = new List<RecordedAnswer>(response.Answers.Count);
            foreach (var answer in response.Answers)
            {
                answers.Add(RecordedAnswer.From(answer));
            }
        }
        else
        {
            var error = result.Error;
            errorKind = error.Kind;
            if (error.Kind == DecisionErrorKind.Refused)
            {
                refusedKeys = [];
                foreach (var failure in error.Failures)
                {
                    if (failure.QuestionKey is { } key && !refusedKeys.Contains(key, StringComparer.Ordinal))
                    {
                        refusedKeys.Add(key);
                    }
                }
            }
        }

        var record = new DecisionRecord(
            decisionId,
            erasureKey,
            request.RetryAttempt,
            started,
            latency,
            _provider,
            _endpoint,
            _defaultModel,
            request.Model,
            response?.Model,
            response?.Id,
            response?.UpstreamProvider,
            response?.Usage,
            response?.IsCached ?? false,
            request.Definition,
            state,
            images.Count,
            DecisionRecord.Freeze(imageHashes),
            DecisionRecord.Freeze(answers),
            errorKind,
            DecisionRecord.Freeze(refusedKeys));
        _recorder.TryEnqueue(record);
    }

    // Runs the redactor. A redactor that throws or returns null keeps no record; its exception's message is never logged,
    // since it can quote the state.
    private bool TryRedact(DecisionContent original, out DecisionContent state)
    {
        state = original;
        if (_recorder.Redact is not { } redact)
        {
            return true;
        }

        RedactionResult redaction;
        try
        {
            redaction = redact(original) ?? throw new InvalidOperationException("The redactor returned null.");
        }
        catch (Exception exception)
        {
            RecordingTelemetry.Record(RecordingTelemetry.RedactionFailed);
            _recorder.LogRedactionFailed(exception.GetType().Name);
            return false;
        }

        if (redaction.IsDrop)
        {
            RecordingTelemetry.Record(RecordingTelemetry.DroppedRedaction);
            return false;
        }

        state = redaction.State;
        return true;
    }
}
