using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
/// <c>minos.path</c> as a <see cref="JsonElement"/>, each only when the Minos response has it, and <c>minos.isCached</c>
/// as a <see cref="bool"/> always. A Minos response whose usage cost is negative or not a finite number is refused, as
/// answers upstream refuses are, so nothing invalid reaches the additional properties: the adapter throws a
/// <see cref="DecisionProtocolException"/> that names the cost but not its value, whose inner
/// <see cref="MinosDecisionException"/> carries an <see cref="DecisionErrorKind.InvalidResponse"/> error with the same
/// message. Every value has a
/// JSON contract in <see cref="AIJsonUtilities.DefaultOptions"/>, so the response serializes without reflection.
/// </para>
/// <para>
/// The path is a JSON object with camelCase names and no null members:
/// <c>{"routes":[{"key","answeredBy","reason","primaryCertainty","targetCertainty"}],"calls":[{"clientName","model","isCached","status","errorKind","inputTokens","outputTokens","cost"}]}</c>,
/// one route per question and one call per call, in order. <c>reason</c>, <c>status</c> and <c>errorKind</c> are the
/// Minos enum names in camelCase, such as <c>confident</c>, <c>fellBack</c>, <c>answered</c> and <c>rateLimited</c>.
/// <c>status</c> is always written; <c>targetCertainty</c>, <c>model</c>, <c>errorKind</c> and the usage members only
/// when set. A non-finite call cost, which JSON numbers cannot hold, is written as the string <c>NaN</c>,
/// <c>Infinity</c> or <c>-Infinity</c>; a call's cost is kept as reported, since the path records what each call said.
/// The path's internal routing telemetry is left out on purpose: it is not public data and feeds only the routing
/// stage's own spans and metrics. <see cref="UpstreamDecisionClient"/> does not read the path back, since a routing stage
/// sets its own.
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
/// <para>
/// A Minos client reports failures as a <see cref="DecisionError"/>. One that throws anything other than
/// <see cref="ArgumentNullException"/>, <see cref="ObjectDisposedException"/> or <see cref="OperationCanceledException"/>
/// breaks the Minos contract, and the exception passes through unwrapped.
/// </para>
/// </remarks>
[Experimental("MEAI001")]
public sealed class MinosDecisionClient : UpstreamClient
{
    // Names the field, never its value.
    private const string InvalidCostMessage = "The Minos response's usage cost is negative or not a finite number.";

    private readonly MinosClient _innerClient;
    private readonly MinosMetadata? _minosMetadata;
    private readonly UpstreamMetadata _metadata;

    // 1 once Dispose has run. Dispose sets it with Interlocked.Exchange, so only one caller disposes the Minos client.
    private int _disposed;

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

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
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _innerClient.Dispose();
        }
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
            properties[MinosPropertyKeys.Path] = PathJson(path);
        }

        if (response.Usage?.Cost is { } cost)
        {
            properties[MinosPropertyKeys.Cost] = cost;
        }

        properties[MinosPropertyKeys.IsCached] = response.IsCached;
        return properties;
    }

    // Written by hand, so no reflection and no contract to keep in a serializer context.
    private static JsonElement PathJson(DecisionPath path)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("routes");
            foreach (var route in path.Routes)
            {
                writer.WriteStartObject();
                writer.WriteString("key", route.Key);
                writer.WriteString("answeredBy", route.AnsweredBy);
                writer.WriteString("reason", NameOf(route.Reason.ToString()));
                writer.WriteNumber("primaryCertainty", route.PrimaryCertainty);
                if (route.TargetCertainty is { } target)
                {
                    writer.WriteNumber("targetCertainty", target);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("calls");
            foreach (var call in path.Calls)
            {
                writer.WriteStartObject();
                writer.WriteString("clientName", call.ClientName);
                if (call.Model is { } model)
                {
                    writer.WriteString("model", model);
                }

                writer.WriteBoolean("isCached", call.IsCached);
                writer.WriteString("status", NameOf(call.Status.ToString()));
                if (call.ErrorKind is { } errorKind)
                {
                    writer.WriteString("errorKind", NameOf(errorKind.ToString()));
                }

                if (call.Usage?.InputTokens is { } input)
                {
                    writer.WriteNumber("inputTokens", input);
                }

                if (call.Usage?.OutputTokens is { } output)
                {
                    writer.WriteNumber("outputTokens", output);
                }

                if (call.Usage?.Cost is { } cost)
                {
                    if (double.IsFinite(cost))
                    {
                        writer.WriteNumber("cost", cost);
                    }
                    else
                    {
                        writer.WriteString("cost", double.IsNaN(cost) ? "NaN" : cost > 0 ? "Infinity" : "-Infinity");
                    }
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static string NameOf(string enumName) => JsonNamingPolicy.CamelCase.ConvertName(enumName);

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
        if (response.Usage?.Cost is { } cost && (cost < 0 || !double.IsFinite(cost)))
        {
            throw new DecisionProtocolException(
                InvalidCostMessage, new MinosDecisionException(new DecisionError(DecisionErrorKind.InvalidResponse, InvalidCostMessage)));
        }

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
