using System.Buffers;
using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>
/// One attempt per call: checks the request, its questions and images, against the provider's capabilities and the
/// protocol's own limits, writes the request through the protocol, sends it through <paramref name="send"/>, and reads
/// the response through the protocol. It never retries, logs or traces; stages do that.
/// </summary>
/// <remarks>
/// Every pooled buffer is returned before the call completes. An attempt that would start after the owning client was
/// disposed returns <see cref="DecisionErrorKind.Disposed"/> without being sent. An attempt torn down by disposing an
/// owned <see cref="HttpClient"/> is mapped to <see cref="DecisionErrorKind.Disposed"/> by <paramref name="send"/>'s
/// error mapper, and one whose send throws <see cref="ObjectDisposedException"/> once the client is disposed is
/// reported as <see cref="DecisionErrorKind.Disposed"/> here. A request the provider's capabilities or the protocol do
/// not accept returns <see cref="DecisionErrorKind.Unsupported"/> without being sent, before any buffer is rented, with
/// the capability failures first and then the protocol's; the check runs after the disposed check and before the
/// request body is written, and allocates nothing when the request passes.
/// </remarks>
/// <param name="send">Sends one attempt of the written body.</param>
/// <param name="protocol">Writes the request and reads the response.</param>
/// <param name="capabilities">The provider's resolved capabilities.</param>
/// <param name="providerLabel">The provider's name in failure messages, computed once so the check allocates nothing per call.</param>
/// <param name="model">The model a request without one uses.</param>
/// <param name="metadata">What <see cref="GetService"/> returns for <see cref="DecisionClientMetadata"/>.</param>
/// <param name="pool">The pool the request body is rented from.</param>
/// <param name="disposed">Reads whether the owning client has been disposed.</param>
internal sealed class DecisionTransport(
    RawSend send, IDecisionProtocol protocol, CapabilitySet capabilities, string providerLabel, string model,
    DecisionClientMetadata metadata, ArrayPool<byte> pool, Func<bool> disposed) : IDecisionClient
{
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (disposed())
        {
            return new(Result<DecisionResponse, DecisionError>.Failure(DecisionErrors.Disposed(null)));
        }

        var failures = capabilities.Collect(request.Definition, request.Images.Count, providerLabel);
        protocol.CollectUnsupported(request.Definition, providerLabel, ref failures);
        if (failures is not null)
        {
            return new(Result<DecisionResponse, DecisionError>.Failure(CapabilitySet.Unsupported(failures)));
        }

        var body = protocol.WriteRequest(request.Definition, request.State, request.Images, request.Model ?? model, pool);
        return SendAsync(body, request, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(metadata) ? metadata : null;
    }

    // The owning client disposes the HttpClient; the transport owns nothing to dispose.
    public void Dispose()
    {
    }

    private async ValueTask<Result<DecisionResponse, DecisionError>> SendAsync(RawJson body, DecisionRequest request, CancellationToken ct)
    {
        RawJson? response = null;
        try
        {
            var retryCount = request.RetryAttempt == 0 ? default(int?) : request.RetryAttempt;
            Result<RawJson, DecisionError> sent;
            try
            {
                sent = await send(body, retryCount, ct).ConfigureAwait(false);
            }
            catch (ObjectDisposedException exception) when (disposed())
            {
                // Dispose landed after the check above but before the send checked it: the HttpClient threw rather than
                // answer, and ZeroAlloc.Rest rethrows that unmapped.
                return Result<DecisionResponse, DecisionError>.Failure(DecisionErrors.Disposed(exception));
            }

            if (sent.IsFailure)
            {
                return Result<DecisionResponse, DecisionError>.Failure(sent.Error);
            }

            response = sent.Value;
            return protocol.ReadResponse(response.Span, request.Definition);
        }
        finally
        {
            response?.Dispose();
            body.Dispose();
        }
    }
}
