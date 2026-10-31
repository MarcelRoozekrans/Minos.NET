using System.Security.Cryptography;
using ZeroAlloc.Results;

namespace Minos.Recording;

/// <summary>
/// A stage that hands a record of each decision to a <see cref="DecisionRecorder"/>: the question set, the state after
/// <see cref="RecordingOptions.Redact"/>, the model and endpoint, the answers and usage, or the kind of error. The
/// redactor runs before the call returns to the caller, so no state is queued before it ran. The stage returns the
/// inner client's result unchanged and never waits for the recorder.
/// </summary>
/// <remarks>
/// <para>
/// The record takes its decision id and erasure key from the <see cref="DecisionRecording"/> scope of the call, or gets a
/// generated UUIDv7 id and no key when there is none. A scope holds one decision: a second request in it with
/// <see cref="DecisionRequest.RetryAttempt"/> 0 throws <see cref="InvalidOperationException"/> before the inner call, the one
/// way the stage fails a call. Add this stage outermost, so a decision is one record and a cache hit is recorded as
/// cached. A request with a <see cref="DecisionRequest.RetryAttempt"/> above 0, which reaches the stage only when a retry
/// stage sits outside it, is passed to the inner client and not recorded.
/// </para>
/// <para>
/// A record that cannot be queued, because the queue is full or the recorder is disposed, is dropped and counted. A
/// redactor that throws keeps no record and is logged as event 1011 with the exception's type name only. Images are
/// recorded as their count and the SHA-256 of each, never their bytes. Once the inner client has returned, nothing the
/// recording does fails the call: a failure to record loses the record, is counted as a <c>failed</c> record and is logged
/// as event 1013 with the exception's type name only. An exception
/// from the inner client, the caller's cancellation among them, reaches the caller unchanged and leaves the scope free for
/// another attempt. Disposing the stage disposes the inner client, never the recorder.
/// </para>
/// <para>
/// With <see cref="ShadowOptions"/>, the stage runs a candidate client beside the inner one for each decision it recorded
/// on its first attempt whose inner call succeeded and was not a cache hit, within the daily cap and
/// <see cref="ShadowOptions.MaxConcurrent"/>, and counts each skip. The candidate starts in the background once the record
/// is queued, without the caller's execution context, so the scope and the <see cref="System.Diagnostics.Activity"/> of
/// the call stay out of it; it gets the unredacted state and <see cref="ShadowOptions.Model"/>, never the caller's
/// cancellation token. It is cancelled after <see cref="ShadowOptions.Timeout"/> or when the recorder is disposed. Its
/// answers, or its kind of error, reach the sink as a second write of the decision's record with
/// <see cref="DecisionRecord.Shadow"/> set. The stage never waits for the candidate, never changes the inner client's
/// result, and never disposes the candidate. Its error message is never logged or recorded.
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
    private readonly ShadowRunner? _shadow;

    /// <summary>Initializes a new instance of the <see cref="RecordingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client whose decisions are recorded.</param>
    /// <param name="recorder">The recorder the records go to. The stage does not dispose it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="recorder"/> is <see langword="null"/>.</exception>
    public RecordingDecisionClient(IDecisionClient innerClient, DecisionRecorder recorder)
        : this(innerClient, recorder, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RecordingDecisionClient"/> class, in shadow mode when <paramref name="shadow"/> is set.</summary>
    /// <param name="innerClient">The client whose decisions are recorded.</param>
    /// <param name="recorder">The recorder the records go to. The stage does not dispose it.</param>
    /// <param name="shadow">
    /// The candidate to shadow the inner client with, and its limits, read once here; <see langword="null"/> for no shadow
    /// mode. The stage does not dispose the candidate.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="recorder"/> is <see langword="null"/>.</exception>
    public RecordingDecisionClient(IDecisionClient innerClient, DecisionRecorder recorder, ShadowOptions? shadow)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
        _time = recorder.Time;
        var metadata = innerClient.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? UnknownProvider;
        _endpoint = EndpointText(metadata);
        _defaultModel = metadata?.DefaultModel;
        _shadow = shadow is null ? null : new ShadowRunner(shadow, recorder);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// A decision was already made in the call's <see cref="DecisionRecording"/> scope, and the request's
    /// <see cref="DecisionRequest.RetryAttempt"/> is 0.
    /// </exception>
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RetryAttempt > 0)
        {
            // A retry from a stage outside this one: the decision is recorded by its first attempt only.
            return InnerClient.EvaluateAsync(request, cancellationToken);
        }

        var identity = Identify();
        var started = _time.GetUtcNow();
        var timestamp = _time.GetTimestamp();
        ValueTask<Result<DecisionResponse, DecisionError>> pending;
        try
        {
            pending = InnerClient.EvaluateAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            // No decision was made: the scope can be used again. The exception is the caller's, unchanged.
            identity.Release();
            throw;
        }

        if (pending.IsCompletedSuccessfully)
        {
            var result = pending.Result;
            Record(request, result, identity, started, timestamp);
            return new(result);
        }

        return AwaitAsync(pending, request, identity, started, timestamp);
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
    private static Identity Identify()
    {
        if (DecisionRecording.Current is not { } scope)
        {
            return new(Guid.CreateVersion7().ToString(), null, null, Generated: false);
        }

        if (!scope.TryClaimFresh())
        {
            throw new InvalidOperationException(
                "A decision was already made in this recording scope: a scope holds one decision. Begin a new scope for each decision.");
        }

        var decisionId = scope.EnsureDecisionId(out var generated);
        return new(decisionId, scope.ErasureKey, scope, generated);
    }

    private async ValueTask<Result<DecisionResponse, DecisionError>> AwaitAsync(
        ValueTask<Result<DecisionResponse, DecisionError>> pending,
        DecisionRequest request,
        Identity identity,
        DateTimeOffset started,
        long timestamp)
    {
        Result<DecisionResponse, DecisionError> result;
        try
        {
            result = await pending.ConfigureAwait(false);
        }
        catch (Exception)
        {
            identity.Release();
            throw;
        }

        Record(request, result, identity, started, timestamp);
        return result;
    }

    // Once the inner client has decided, recording can never fail the call: whatever throws here is counted and logged
    // by its type name, and the record is lost.
    private void Record(DecisionRequest request, Result<DecisionResponse, DecisionError> result, Identity identity, DateTimeOffset started, long timestamp)
    {
        try
        {
            RecordCore(request, result, identity.DecisionId, identity.ErasureKey, started, timestamp);
        }
        catch (Exception exception)
        {
            RecordingTelemetry.Records.Add(RecordingTelemetry.Failed);
            _recorder.LogRecordingFailed(exception.GetType().Name);
        }
    }

    /// <summary>A client's endpoint as a record keeps it: the absolute URI, or the text it was made from.</summary>
    /// <param name="metadata">The client's metadata, or <see langword="null"/>.</param>
    /// <returns>The endpoint's text, or <see langword="null"/>.</returns>
    internal static string? EndpointText(DecisionClientMetadata? metadata)
        => metadata?.Endpoint is { IsAbsoluteUri: true } endpoint ? endpoint.AbsoluteUri : metadata?.Endpoint?.OriginalString;

    /// <summary>The keys of the questions a refusal names, each once, in order; <see langword="null"/> for any other error.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The refused keys, or <see langword="null"/>.</returns>
    internal static List<string>? RefusedKeys(DecisionError error)
    {
        if (error.Kind != DecisionErrorKind.Refused)
        {
            return null;
        }

        var refusedKeys = new List<string>();
        foreach (var failure in error.Failures)
        {
            if (failure.QuestionKey is { } key && !refusedKeys.Contains(key, StringComparer.Ordinal))
            {
                refusedKeys.Add(key);
            }
        }

        return refusedKeys;
    }

    // Builds the record and queues it without waiting. A redactor's failure is caught, and a full queue drops the record.
    // In shadow mode, a decision to shadow has its shadow begun before its record is queued, so an erasure handled in
    // between is noticed, and its candidate starts only once the record was queued.
    private void RecordCore(
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
            refusedKeys = RefusedKeys(error);
        }

        var record = new DecisionRecord(
            decisionId,
            erasureKey,
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

        // A failed call gives nothing to agree with, and a cache hit repeats a decision already shadowed.
        ShadowRunner.Reservation shadow = default;
        var runner = _shadow is not null && response is { IsCached: false } && _shadow.TryBegin(record, out shadow) ? _shadow : null;
        if (!_recorder.TryEnqueue(record))
        {
            runner?.Cancel(shadow);
            return;
        }

        runner?.Start(request, shadow);
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
            RecordingTelemetry.Records.Add(RecordingTelemetry.Failed);
            _recorder.LogRedactionFailed(exception.GetType().Name);
            return false;
        }

        if (redaction.IsDrop)
        {
            RecordingTelemetry.Records.Add(RecordingTelemetry.Dropped);
            return false;
        }

        state = redaction.State;
        return true;
    }

    // The record's id and key, and the fresh claim this call made on its scope, if any.
    private readonly record struct Identity(string DecisionId, string? ErasureKey, DecisionRecording.ScopeState? ClaimedScope, bool Generated)
    {
        // Gives back a fresh claim when the inner call threw, with the id when this call generated it.
        public void Release() => ClaimedScope?.Release(Generated ? DecisionId : null);
    }
}
