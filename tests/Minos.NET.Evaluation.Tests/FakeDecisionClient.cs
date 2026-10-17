using System.Collections.Concurrent;
using ZeroAlloc.Results;

namespace Minos.Evaluation.Tests;

internal sealed class FakeDecisionClient(
    Func<DecisionRequest, CancellationToken, ValueTask<Result<DecisionResponse, DecisionError>>> handler,
    DecisionClientMetadata? metadata = null) : IDecisionClient
{
    private readonly ConcurrentQueue<DecisionRequest> _requests = new();
    private int _active;
    private int _peak;

    public IReadOnlyCollection<DecisionRequest> Requests => _requests;

    public int PeakConcurrency => Volatile.Read(ref _peak);

    public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(request);
        var now = Interlocked.Increment(ref _active);
        int seen;
        while (now > (seen = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, seen) != seen)
        {
        }

        try
        {
            return await handler(request, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(DecisionClientMetadata) ? metadata : null;

    public void Dispose()
    {
    }
}
