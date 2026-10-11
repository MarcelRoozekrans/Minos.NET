using System.Buffers;
using Minos.SystemOne.Protocols;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.SystemOne.Transport;

/// <summary>
/// One attempt per call: writes the request through the protocol, POSTs it to the settings' path for its model, and reads
/// the response or maps the error through it. It never retries, logs or traces; stages do that.
/// </summary>
/// <remarks>
/// Every pooled buffer is returned before the call completes. An attempt that would start after the owning client was
/// disposed returns <see cref="DecisionErrorKind.Disposed"/> without being sent. An attempt torn down by disposing an
/// owned <see cref="HttpClient"/> is mapped to <see cref="DecisionErrorKind.Disposed"/> by <paramref name="api"/>'s
/// <see cref="DecisionErrorMapper"/>, and one whose send throws <see cref="ObjectDisposedException"/> once the client is
/// disposed is reported as <see cref="DecisionErrorKind.Disposed"/> here.
/// </remarks>
/// <param name="api">The REST client that sends the attempt.</param>
/// <param name="protocol">Writes the request and reads the response.</param>
/// <param name="settings">
/// The resolved settings: the <c>Authorization</c> header value, or none, and the path for a model. On Cloudflare, where
/// the model is the path, a model that is not a single name is a <see cref="DecisionErrorKind.Validation"/> failure that is
/// never sent.
/// </param>
/// <param name="metadata">What <see cref="GetService"/> returns for <see cref="DecisionClientMetadata"/>; its default model fills a request without one.</param>
/// <param name="pool">The pool the request body is rented from.</param>
/// <param name="disposed">Reads whether the owning client has been disposed.</param>
internal sealed class DecisionTransport(
    IDecisionApi api, IDecisionProtocol protocol, SystemOneClientSettings settings, DecisionClientMetadata metadata, ArrayPool<byte> pool, Func<bool> disposed)
    : IDecisionClient
{
    private readonly string _model = metadata.DefaultModel ?? SystemOneDefaults.Model;

    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (disposed())
        {
            return new(Result<DecisionResponse, DecisionError>.Failure(DecisionErrors.Disposed(null)));
        }

        var model = request.Model ?? _model;
        if (!settings.AcceptsModel(model))
        {
            return new(Result<DecisionResponse, DecisionError>.Failure(
                new DecisionError(DecisionErrorKind.Validation, SystemOneClientSettings.CloudflareModelMessage)));
        }

        var body = protocol.WriteRequest(request.Definition, request.State, model, pool);
        return SendAsync(body, request, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(metadata) ? metadata : null;
    }

    // The owning SystemOneClient disposes the HttpClient; the transport owns nothing to dispose.
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
                sent = await api.SendAsync(body, settings.PathFor(request.Model ?? _model), settings.Authorization, retryCount, ct).ConfigureAwait(false);
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
