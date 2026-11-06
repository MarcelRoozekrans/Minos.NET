using ZeroAlloc.Results;

namespace Minos.Samples.TicketTriage;

/// <summary>A stage that records how long the wrapped call took, whether it succeeded or failed.</summary>
internal sealed class TimingDecisionClient(IDecisionClient inner, CallSlot slot, TimeProvider? time = null) : DelegatingDecisionClient(inner)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public override async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var start = _time.GetTimestamp();
        try
        {
            return await InnerClient.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.LatencyMs = _time.GetElapsedTime(start).TotalMilliseconds;
        }
    }
}
