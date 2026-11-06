using ZeroAlloc.Results;

namespace Minos.Samples.TicketTriage;

/// <summary>What one stage saw for one item: the local answers and how long its call took.</summary>
/// <remarks>
/// One slot per stage per item. The pipeline uses a local slot for <see cref="LocalCaptureClient"/> and a separate target
/// slot for <see cref="TimingDecisionClient"/>, so their <see cref="LatencyMs"/> values never collide. Both are reset per item.
/// </remarks>
internal sealed class CallSlot
{
    public DecisionResponse? Response { get; set; }

    public double? LatencyMs { get; set; }

    public void Reset()
    {
        Response = null;
        LatencyMs = null;
    }
}

/// <summary>
/// A stage placed directly over the local client, under the escalation stage. It keeps the local response, which escalation
/// replaces on the way out, and the time the local call took, so the record can show both what the local model said and
/// what the pipeline finally answered.
/// </summary>
internal sealed class LocalCaptureClient(IDecisionClient inner, CallSlot slot, TimeProvider? time = null) : DelegatingDecisionClient(inner)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public override async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var start = _time.GetTimestamp();
        var result = await InnerClient.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            slot.Response = result.Value;
            slot.LatencyMs = _time.GetElapsedTime(start).TotalMilliseconds;
        }

        return result;
    }
}
