using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using MinosClient = Minos.IDecisionClient;
using MinosMetadata = Minos.DecisionClientMetadata;
using MinosResponse = Minos.DecisionResponse;
using UpstreamClient = Microsoft.Extensions.AI.IDecisionClient;
using UpstreamMetadata = Microsoft.Extensions.AI.DecisionClientMetadata;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;
using UpstreamResponse = Microsoft.Extensions.AI.DecisionResponse;

namespace Minos.Extensions.AI.Decisions;

/// <summary>
/// An upstream Microsoft.Extensions.AI <see cref="UpstreamClient"/> over a Minos client: code written against the
/// upstream API calls it, and it asks the Minos client, which may be a whole Minos pipeline.
/// docs/extensions-ai-decisions/mapping.md describes each conversion as "Over Minos".
/// </summary>
/// <remarks>
/// <para>
/// A request is converted as the page says: a new <see cref="QuestionSetDefinition"/> from the upstream questions on each
/// call, a JSON string state as text and an object or array as JSON, and <see cref="DecisionOptions.ModelId"/> as the
/// model, with retry attempt 0, no images and no deadline. The options' other properties are ignored. A number,
/// <c>true</c>, <c>false</c> or <c>null</c> state, which Minos cannot hold, throws a <see cref="DecisionClientException"/>
/// that is not transient, before the Minos client is called.
/// </para>
/// <para>
/// Every answer declares <see cref="DecisionPrecision.TwoDecimalPlaces"/> and carries the <c>minos.</c> answer keys. The
/// response has the request it was given, provenance from the Minos client metadata with the response's model and id and
/// <see cref="DecisionPrecision.TwoDecimalPlaces"/>, the token counts widened with their sum as the total when both are
/// known, and the Minos response as its raw representation. Its additional properties hold only <c>minos.</c> keys:
/// <c>minos.upstreamProvider</c> as a <see cref="string"/>, <c>minos.cost</c> as a <see cref="double"/> and
/// <c>minos.path</c> as the Minos <see cref="DecisionPath"/> instance, each only when the Minos response has it, and
/// <c>minos.isCached</c> as a <see cref="bool"/> always. The path is immutable and safe to share, as upstream asks of a
/// property value; <see cref="UpstreamDecisionClient"/> does not read it back, since a routing stage sets its own path.
/// </para>
/// <para>
/// A Minos <see cref="DecisionError"/> throws a <see cref="DecisionClientException"/> with the error's message, transient
/// when <see cref="DecisionRetryOptions.IsTransient"/> says so; for <see cref="DecisionErrorKind.InvalidResponse"/> it
/// throws a <see cref="DecisionProtocolException"/> instead. Either way the inner exception is a
/// <see cref="MinosDecisionException"/> that carries the error. Answers upstream refuses, such as a distribution that does
/// not sum to 1 within the declared rounding, are never repaired: the adapter throws a new
/// <see cref="DecisionProtocolException"/> with upstream's message, whose inner <see cref="MinosDecisionException"/> carries
/// an <see cref="DecisionErrorKind.InvalidResponse"/> error with that message and the refused answer's exception. An
/// <see cref="OperationCanceledException"/> passes through unchanged. No message the adapter writes carries the state.
/// </para>
/// <para>
/// The client owns the Minos client and disposes it, as a Minos <see cref="DelegatingDecisionClient"/> does its inner
/// client. A call after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
[Experimental("MEAI001")]
public sealed class MinosDecisionClient : UpstreamClient
{
    private readonly MinosClient _innerClient;
    private readonly MinosMetadata? _minosMetadata;
    private readonly UpstreamMetadata _metadata;

    private volatile bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MinosDecisionClient"/> class.</summary>
    /// <param name="innerClient">The Minos client. This client owns it and disposes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The Minos <see cref="MinosMetadata"/> and <see cref="DecisionCapabilities"/> are read once, here. The upstream
    /// metadata takes the provider name, endpoint and default model from the first, each <see langword="null"/> when the
    /// Minos client reports no metadata, and the supported kinds from the second, <see langword="null"/>, meaning
    /// unknown, when it reports no question kinds.
    /// </remarks>
    public MinosDecisionClient(MinosClient innerClient)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        _innerClient = innerClient;

        _minosMetadata = innerClient.GetService(typeof(MinosMetadata)) as MinosMetadata;
        var kinds = (innerClient.GetService(typeof(DecisionCapabilities)) as DecisionCapabilities)?.QuestionKinds;
        _metadata = new UpstreamMetadata(
            _minosMetadata?.ProviderName, _minosMetadata?.Endpoint, _minosMetadata?.DefaultModel, kinds is { } k ? KindsOf(k) : null);
    }

    /// <inheritdoc />
    public Task<UpstreamResponse> GetResponseAsync(UpstreamRequest request, DecisionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Throws the non-transient DecisionClientException for a state Minos cannot hold, before anything is sent.
        var minosRequest = DecisionMapper.ToMinos(request, options);
        return SendAsync(request, minosRequest, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers for itself first, by the upstream rule of the exact requested type: this client for
    /// <see cref="MinosDecisionClient"/>, its upstream <see cref="UpstreamMetadata"/>, and the wrapped client for the Minos
    /// <see cref="MinosClient"/> interface. Then it asks the Minos client, which returns its own metadata, its resolved
    /// <see cref="DecisionCapabilities"/> and its pipeline stages. A non-null key goes to the Minos client only.
    /// </remarks>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null)
        {
            if (serviceType == typeof(MinosDecisionClient))
            {
                return this;
            }

            if (serviceType == typeof(UpstreamMetadata))
            {
                return _metadata;
            }

            if (serviceType == typeof(MinosClient))
            {
                return _innerClient;
            }
        }

        return _innerClient.GetService(serviceType, serviceKey);
    }

    /// <summary>Disposes the Minos client. Calling it more than once does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _innerClient.Dispose();
    }

    private static List<DecisionKind> KindsOf(QuestionKinds kinds)
    {
        var list = new List<DecisionKind>(3);
        if ((kinds & QuestionKinds.Noul) != QuestionKinds.None)
        {
            list.Add(DecisionKind.Binary);
        }

        if ((kinds & QuestionKinds.Choice) != QuestionKinds.None)
        {
            list.Add(DecisionKind.Choice);
        }

        if ((kinds & QuestionKinds.Score) != QuestionKinds.None)
        {
            list.Add(DecisionKind.Score);
        }

        return list;
    }

    // The error's message only, so no state; the error itself rides on the inner exception.
    private static Exception ToException(DecisionError error) => error.Kind == DecisionErrorKind.InvalidResponse
        ? new DecisionProtocolException(error.Message, new MinosDecisionException(error))
        : new DecisionClientException(error.Message, DecisionRetryOptions.IsTransient(error), new MinosDecisionException(error));

    private static Dictionary<string, object?> PropertiesOf(MinosResponse response)
    {
        var properties = new Dictionary<string, object?>(4, StringComparer.Ordinal);
        if (response.UpstreamProvider is { } upstreamProvider)
        {
            properties[MinosPropertyKeys.UpstreamProvider] = upstreamProvider;
        }

        if (response.Path is { } path)
        {
            properties[MinosPropertyKeys.Path] = path;
        }

        if (response.Usage?.Cost is { } cost)
        {
            properties[MinosPropertyKeys.Cost] = cost;
        }

        properties[MinosPropertyKeys.IsCached] = response.IsCached;
        return properties;
    }

    private static UsageDetails? UsageOf(DecisionUsage? usage)
    {
        if (usage is null || (usage.InputTokens is null && usage.OutputTokens is null))
        {
            return null;
        }

        long? input = usage.InputTokens;
        long? output = usage.OutputTokens;
        return new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            TotalTokenCount = input + output,
        };
    }

    private async Task<UpstreamResponse> SendAsync(UpstreamRequest request, DecisionRequest minosRequest, CancellationToken cancellationToken)
    {
        var result = await _innerClient.EvaluateAsync(minosRequest, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw ToException(result.Error);
        }

        var response = result.Value;
        try
        {
            var answers = DecisionMapper.ToUpstreamAnswers(response, request.Questions);
            var provenance = new DecisionProvenance(
                _minosMetadata?.ProviderName, _minosMetadata?.Endpoint, response.Model, response.Id, DecisionPrecision.TwoDecimalPlaces);
            return new UpstreamResponse(request, answers, provenance, UsageOf(response.Usage), response, PropertiesOf(response));
        }
        catch (DecisionProtocolException exception)
        {
            // Upstream refused the Minos answers; they are not repaired. Upstream's message names no value.
            var error = new DecisionError(DecisionErrorKind.InvalidResponse, exception.Message) { Exception = exception };
            throw new DecisionProtocolException(exception.Message, new MinosDecisionException(error));
        }
    }
}
