using ZeroAlloc.Results;

namespace Minos.Recording;

/// <summary>
/// Runs a recording stage's shadow candidate: reserves a slot of the daily cap and of the running candidates, registers
/// the shadow with the recorder, starts the candidate in the background and hands the decision's record, with the
/// candidate's part, back to the recorder. Nothing it does throws into the primary's call.
/// </summary>
/// <remarks>
/// The stage begins a shadow before it queues the record, so an erasure handled in between is noticed, and cancels it
/// when the record was not queued. The candidate gets the request's unredacted state; the record it writes is the
/// queued one, redacted.
/// </remarks>
internal sealed class ShadowRunner
{
    private const string UnknownProvider = "unknown";

    private readonly DecisionRecorder _recorder;
    private readonly IDecisionClient _candidate;
    private readonly int _dailyCap;
    private readonly TimeSpan _timeout;
    private readonly int _maxConcurrent;
    private readonly string? _model;
    private readonly string _provider;
    private readonly string? _endpoint;
    private readonly string? _defaultModel;
    private readonly Lock _capGate = new();
    private DateTime _capDay;
    private int _capUsed;
    private int _running;

    /// <summary>Initializes a new instance of the <see cref="ShadowRunner"/> class, reading the options once.</summary>
    /// <param name="options">The shadow mode settings.</param>
    /// <param name="recorder">The recorder the shadowed records go to.</param>
    public ShadowRunner(ShadowOptions options, DecisionRecorder recorder)
    {
        _recorder = recorder;
        _candidate = options.Candidate;
        _dailyCap = options.DailyCap;
        _timeout = options.Timeout;
        _maxConcurrent = options.MaxConcurrent;
        _model = options.Model;
        var metadata = _candidate.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? UnknownProvider;
        _endpoint = RecordingDecisionClient.EndpointText(metadata);
        _defaultModel = metadata?.DefaultModel;
    }

    /// <summary>
    /// Reserves a cap slot and a running slot and registers the shadow, before the record is queued. A skip for the cap
    /// or for the running candidates is counted.
    /// </summary>
    /// <param name="record">The decision's record, about to be queued.</param>
    /// <param name="shadow">The reservation, for <see cref="Start"/> or <see cref="Cancel"/>.</param>
    /// <returns><see langword="true"/> when the decision is to be shadowed.</returns>
    public bool TryBegin(DecisionRecord record, out Reservation shadow)
    {
        shadow = default;

        // The one call here that can throw, a broken clock, comes before anything is reserved.
        if (!TryReserveCap(out var day))
        {
            RecordingTelemetry.Shadows.Add(RecordingTelemetry.SkippedCap);
            return false;
        }

        if (Interlocked.Increment(ref _running) > _maxConcurrent)
        {
            Interlocked.Decrement(ref _running);
            GiveCapBack(day);
            RecordingTelemetry.Shadows.Add(RecordingTelemetry.SkippedBusy);
            return false;
        }

        if (!_recorder.TryBeginShadow(record.DecisionId, record.ErasureKey, out var stopping))
        {
            // The recorder is disposed, or a shadow of that decision runs: the record's own queuing counts the first.
            Interlocked.Decrement(ref _running);
            GiveCapBack(day);
            return false;
        }

        shadow = new Reservation(record, day, stopping);
        return true;
    }

    /// <summary>Gives back a reservation whose record was not queued: its candidate never starts, and nothing is counted.</summary>
    /// <param name="shadow">The reservation.</param>
    public void Cancel(in Reservation shadow)
    {
        _recorder.CancelShadow(shadow.Record.DecisionId);
        Interlocked.Decrement(ref _running);
        GiveCapBack(shadow.Day);
    }

    /// <summary>Starts the candidate in the background, without the caller's execution context, and returns.</summary>
    /// <param name="request">The primary's request, with the unredacted state.</param>
    /// <param name="shadow">The reservation, whose record was queued.</param>
    public void Start(DecisionRequest request, in Reservation shadow)
    {
        var reserved = shadow;
        try
        {
            var candidateRequest = new DecisionRequest(request.Definition, request.State) { Model = _model, Images = request.Images };

            // Without the flow, the caller's recording scope and Activity stay out of the candidate's call. A caller that
            // suppressed the flow already must not suppress it again, which throws.
            if (ExecutionContext.IsFlowSuppressed())
            {
                _ = Task.Run(() => RunAsync(candidateRequest, reserved));
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    _ = Task.Run(() => RunAsync(candidateRequest, reserved));
                }
            }
        }
        catch (Exception exception)
        {
            // The candidate never started, but its cap slot was spent and its record is queued.
            Interlocked.Decrement(ref _running);
            _recorder.AbandonShadow(reserved.Record.DecisionId);
            _recorder.LogRecordingFailed(exception.GetType().Name);
        }
    }

    // Calls the candidate and hands the record with its part to the recorder. Every path frees the running slot first, so
    // a decision made once the second write was queued finds it free, and ends with EndShadow or AbandonShadow.
    private async Task RunAsync(DecisionRequest request, Reservation shadow)
    {
        var running = true;
        try
        {
            var result = await EvaluateAsync(request, shadow.Stopping).ConfigureAwait(false);
            Interlocked.Decrement(ref _running);
            running = false;
            if (result is null)
            {
                _recorder.AbandonShadow(shadow.Record.DecisionId);
            }
            else
            {
                _recorder.EndShadow(shadow.Record.WithShadow(result));
            }
        }
        catch (Exception exception)
        {
            // Not the candidate's own exception, which EvaluateAsync records: the shadow is lost, never the call.
            if (running)
            {
                Interlocked.Decrement(ref _running);
            }

            _recorder.AbandonShadow(shadow.Record.DecisionId);
            _recorder.LogRecordingFailed(exception.GetType().Name);
        }
    }

    // The candidate's part, or null when the recorder's disposal cancelled it. Its error message is never kept.
    private async Task<ShadowResult?> EvaluateAsync(DecisionRequest request, CancellationToken stopping)
    {
        var time = _recorder.Time;
        using var timeout = new CancellationTokenSource(_timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, stopping);
        var timestamp = time.GetTimestamp();
        Result<DecisionResponse, DecisionError> outcome;
        try
        {
            outcome = await _candidate.EvaluateAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Failure(request, time.GetElapsedTime(timestamp), DecisionErrorKind.Timeout, null);
        }
        catch (HttpRequestException)
        {
            return Failure(request, time.GetElapsedTime(timestamp), DecisionErrorKind.Network, null);
        }
        catch (Exception)
        {
            return Failure(request, time.GetElapsedTime(timestamp), DecisionErrorKind.Server, null);
        }

        var latency = time.GetElapsedTime(timestamp);
        if (!outcome.IsSuccess)
        {
            var error = outcome.Error;
            return Failure(request, latency, error.Kind, RecordingDecisionClient.RefusedKeys(error));
        }

        var response = outcome.Value;
        var answers = new List<RecordedAnswer>(response.Answers.Count);
        foreach (var answer in response.Answers)
        {
            answers.Add(RecordedAnswer.From(answer));
        }

        return new ShadowResult(
            _provider, _endpoint, _defaultModel, request.Model, response.Model, response.Id, response.UpstreamProvider, latency,
            response.Usage, answers, errorKind: null, []);
    }

    private ShadowResult Failure(DecisionRequest request, TimeSpan latency, DecisionErrorKind kind, IReadOnlyList<string>? refusedKeys)
        => new(_provider, _endpoint, _defaultModel, request.Model, null, null, null, latency, null, [], kind, refusedKeys ?? []);

    // Checks the cap and takes a slot in one step; the count starts again when the UTC day changes.
    private bool TryReserveCap(out DateTime day)
    {
        day = _recorder.Time.GetUtcNow().UtcDateTime.Date;
        lock (_capGate)
        {
            if (day != _capDay)
            {
                _capDay = day;
                _capUsed = 0;
            }

            if (_capUsed >= _dailyCap)
            {
                return false;
            }

            _capUsed++;
            return true;
        }
    }

    // Gives back a slot of a candidate that never started, unless its day is over.
    private void GiveCapBack(DateTime day)
    {
        lock (_capGate)
        {
            if (day == _capDay && _capUsed > 0)
            {
                _capUsed--;
            }
        }
    }

    /// <summary>A shadow begun for a record: the record, the day its cap slot counts on, and the recorder's disposal signal.</summary>
    internal readonly record struct Reservation(DecisionRecord Record, DateTime Day, CancellationToken Stopping);
}
