using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Minos.Generator;
using ZeroAlloc.Results;
using MinosClient = Minos.IDecisionClient;
using MinosMetadata = Minos.DecisionClientMetadata;
using MinosRequest = Minos.DecisionRequest;
using MinosResponse = Minos.DecisionResponse;
using UpstreamClient = Microsoft.Extensions.AI.IDecisionClient;
using UpstreamMetadata = Microsoft.Extensions.AI.DecisionClientMetadata;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;
using UpstreamResponse = Microsoft.Extensions.AI.DecisionResponse;

namespace Minos.Extensions.AI.Decisions;

/// <summary>
/// A Minos <see cref="MinosClient"/> over an upstream Microsoft.Extensions.AI decision client: Minos callers and Minos
/// pipeline stages call it, and it asks the upstream client. docs/extensions-ai-decisions/mapping.md describes each
/// conversion as "Over upstream".
/// </summary>
/// <remarks>
/// <para>
/// A request is converted as the page says: the questions to upstream questions, the state to JSON, the model to
/// <see cref="DecisionOptions.ModelId"/>. The retry attempt and the deadline are dropped. A request the page refuses is
/// not sent: it returns <see cref="DecisionErrorKind.Unsupported"/> with one <see cref="QuestionFailure"/> per problem,
/// in the order a native Minos client uses, generic capability rules first: MIN207 for an empty set, then MIN201 for
/// each question whose kind is outside the upstream <see cref="UpstreamMetadata.SupportedKinds"/> in question order,
/// then MIN206 for images, then each question's MIN205 and MIN208 to MIN211 in question order. Its message is the first
/// failure's.
/// </para>
/// <para>
/// The upstream response must carry the state and the questions that were sent; otherwise the call returns
/// <see cref="DecisionErrorKind.InvalidResponse"/>. The answers are converted back with the page's assumptions. The
/// response takes its model and id from the upstream provenance, its token counts from the upstream usage, with a count
/// above <see cref="int.MaxValue"/> left empty, and the <c>minos.upstreamProvider</c>, <c>minos.cost</c> and
/// <c>minos.isCached</c> response properties when they are there. It has no path.
/// </para>
/// <para>
/// Exceptions become a <see cref="DecisionError"/> with the exception's message, a <see langword="null"/> status code and
/// the exception in <see cref="DecisionError.Exception"/>: <see cref="DecisionProtocolException"/> becomes
/// <see cref="DecisionErrorKind.InvalidResponse"/>, a transient <see cref="DecisionClientException"/> becomes
/// <see cref="DecisionErrorKind.Server"/>, and any other exception <see cref="DecisionErrorKind.Http"/>. An
/// <see cref="OperationCanceledException"/> passes through when the caller's token is cancelled. No message the adapter
/// writes carries the state.
/// </para>
/// <para>
/// The client owns the upstream client and disposes it, as a <see cref="DelegatingDecisionClient"/> does its inner
/// client. A call after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>; a call that fails while the
/// client is being disposed returns <see cref="DecisionErrorKind.Disposed"/>.
/// </para>
/// </remarks>
[Experimental("MEAI001")]
public sealed class UpstreamDecisionClient : MinosClient
{
    /// <summary>The provider name reported when the upstream client names none, as the mapping page states.</summary>
    internal const string DefaultProviderName = "upstream";

    private const string MismatchMessage = "The response does not correspond to the request sent by the client.";
    private const string NoResponseMessage = "The upstream client returned no response.";
    // Copied from Minos.DecisionErrors.DisposedMessage in Minos.NET, which this project cannot see; keep it the same.
    private const string DisposedMessage = "The client was disposed while the request was in flight.";

    private readonly UpstreamClient _innerClient;
    private readonly MinosMetadata _metadata;
    private readonly QuestionKinds _kinds;
    private readonly string _providerName;

    // 1 once Dispose has run. Dispose sets it with Interlocked.Exchange, so only one caller disposes the upstream client;
    // a call in flight reads it when its upstream call fails.
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="UpstreamDecisionClient"/> class.</summary>
    /// <param name="innerClient">The upstream client. This client owns it and disposes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The upstream <see cref="UpstreamMetadata"/> is read once, here. Its provider name, or
    /// <see cref="DefaultProviderName"/> when it has none, names the provider in the Minos metadata and in each failure
    /// message; its supported kinds, or every kind when it lists none, are the kinds this client sends.
    /// </remarks>
    public UpstreamDecisionClient(UpstreamClient innerClient)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        _innerClient = innerClient;

        var upstream = innerClient.GetService(typeof(UpstreamMetadata)) as UpstreamMetadata;
        _providerName = upstream?.ProviderName ?? DefaultProviderName;
        _metadata = new MinosMetadata(_providerName, upstream?.ProviderUri, upstream?.DefaultModelId);
        _kinds = upstream?.SupportedKinds is { } supported ? KindsOf(supported) : QuestionKinds.All;
    }

    /// <inheritdoc />
    public ValueTask<Result<MinosResponse, DecisionError>> EvaluateAsync(MinosRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var call = DecisionMapper.ToUpstream(request, _providerName, _kinds);
        return call.IsFailure
            ? new(Result<MinosResponse, DecisionError>.Failure(call.Error))
            : SendAsync(call.Value, request.Definition, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers for itself first: this client for a type it is, its <see cref="MinosMetadata"/>, and a new
    /// <see cref="DecisionCapabilities"/> with every property set: the supported kinds, no question or option limit, no
    /// images and no model listing. Then it asks the upstream client, which returns the upstream metadata and itself. A
    /// non-null key goes to the upstream client only.
    /// </remarks>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null)
        {
            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType.IsInstanceOfType(_metadata))
            {
                return _metadata;
            }

            if (serviceType == typeof(DecisionCapabilities))
            {
                return new DecisionCapabilities
                {
                    QuestionKinds = _kinds,
                    MaxQuestions = null,
                    MaxChoiceOptions = null,
                    SupportsImages = false,
                    SupportsModelListing = false,
                };
            }
        }

        return _innerClient.GetService(serviceType, serviceKey);
    }

    /// <summary>Disposes the upstream client. Calling it more than once does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _innerClient.Dispose();
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private static QuestionKinds KindsOf(IReadOnlyList<DecisionKind> supported)
    {
        var kinds = QuestionKinds.None;
        foreach (var kind in supported)
        {
            kinds |= kind switch
            {
                DecisionKind.Binary => QuestionKinds.Noul,
                DecisionKind.Choice => QuestionKinds.Choice,
                DecisionKind.Score => QuestionKinds.Score,
                _ => QuestionKinds.None,
            };
        }

        return kinds;
    }

    private async ValueTask<Result<MinosResponse, DecisionError>> SendAsync(UpstreamCall call, QuestionSetDefinition definition, CancellationToken cancellationToken)
    {
        UpstreamResponse? response;
        try
        {
            response = await _innerClient.GetResponseAsync(call.Request, call.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsDisposed)
        {
            return Failure(new DecisionError(DecisionErrorKind.Disposed, DisposedMessage) { Exception = exception });
        }
        catch (DecisionProtocolException exception)
        {
            return Failure(FromException(DecisionErrorKind.InvalidResponse, exception));
        }
        catch (DecisionClientException exception)
        {
            return Failure(FromException(exception.IsTransient ? DecisionErrorKind.Server : DecisionErrorKind.Http, exception));
        }
        catch (Exception exception)
        {
            return Failure(FromException(DecisionErrorKind.Http, exception));
        }

        if (response is null)
        {
            return Failure(new DecisionError(DecisionErrorKind.InvalidResponse, NoResponseMessage));
        }

        if (!Corresponds(response.Request, call.Request))
        {
            return Failure(new DecisionError(DecisionErrorKind.InvalidResponse, MismatchMessage));
        }

        var answers = DecisionMapper.ToMinosAnswers(response.Answers, definition);
        if (answers.IsFailure)
        {
            return Failure(answers.Error);
        }

        var properties = response.AdditionalProperties;
        string? malformed = null;
        if (!ResponseProperties.TryReadString(properties, MinosPropertyKeys.UpstreamProvider, out var upstreamProvider))
        {
            malformed = MinosPropertyKeys.UpstreamProvider;
        }

        if (!ResponseProperties.TryReadCost(properties, out var cost))
        {
            malformed ??= MinosPropertyKeys.Cost;
        }

        if (!ResponseProperties.TryReadBoolean(properties, MinosPropertyKeys.IsCached, out var isCached))
        {
            malformed ??= MinosPropertyKeys.IsCached;
        }

        if (malformed is not null)
        {
            return Failure(new DecisionError(DecisionErrorKind.InvalidResponse, ResponseProperties.MalformedMessage(malformed)));
        }

        var usage = new DecisionUsage
        {
            InputTokens = Narrow(response.Usage?.InputTokenCount),
            OutputTokens = Narrow(response.Usage?.OutputTokenCount),
            Cost = cost,
        };

        // The constructor treats a usage with no count and no cost as none.
        return Result<MinosResponse, DecisionError>.Success(
            new MinosResponse(definition, answers.Value, response.Provenance?.ModelId, usage)
            {
                Id = response.Provenance?.ResponseId,
                UpstreamProvider = upstreamProvider,
                IsCached = isCached,
            });
    }

    private static Result<MinosResponse, DecisionError> Failure(DecisionError error) => Result<MinosResponse, DecisionError>.Failure(error);

    // The exception's message only: the adapter adds nothing, so no state, and the status code stays null.
    private static DecisionError FromException(DecisionErrorKind kind, Exception exception)
        => new(kind, exception.Message) { Exception = exception };

    private static int? Narrow(long? count) => count is >= int.MinValue and <= int.MaxValue ? (int)count.Value : null;

    // Upstream's own check, DecisionResponse.ValidateAgainst, is internal; this is the same comparison.
    private static bool Corresponds(UpstreamRequest actual, UpstreamRequest expected)
    {
        if (ReferenceEquals(actual, expected))
        {
            return true;
        }

        if (!JsonElement.DeepEquals(actual.State, expected.State)
            || actual.Questions.Count != expected.Questions.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Questions.Count; i++)
        {
            if (!Equivalent(actual.Questions[i], expected.Questions[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Equivalent(DecisionQuestion actual, DecisionQuestion expected)
    {
        if (actual.Kind != expected.Kind
            || !string.Equals(actual.Id, expected.Id, StringComparison.Ordinal)
            || !string.Equals(actual.Instructions, expected.Instructions, StringComparison.Ordinal))
        {
            return false;
        }

        return (actual, expected) switch
        {
            (BinaryDecisionQuestion a, BinaryDecisionQuestion e) =>
                string.Equals(a.TrueDescription, e.TrueDescription, StringComparison.Ordinal)
                && string.Equals(a.FalseDescription, e.FalseDescription, StringComparison.Ordinal),
            (ChoiceDecisionQuestion a, ChoiceDecisionQuestion e) => Same(a.Candidates, e.Candidates, static c => (c.Id, c.Description)),
            (ScoreDecisionQuestion a, ScoreDecisionQuestion e) => Same(a.Levels, e.Levels, static l => (l.Id, l.Description)),
            _ => false,
        };
    }

    private static bool Same<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, Func<T, (string Id, string Description)> parts)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < actual.Count; i++)
        {
            var (actualId, actualDescription) = parts(actual[i]);
            var (expectedId, expectedDescription) = parts(expected[i]);
            if (!string.Equals(actualId, expectedId, StringComparison.Ordinal)
                || !string.Equals(actualDescription, expectedDescription, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the response-level <c>minos.</c> keys, as values or as JSON after serialization.</summary>
    private static class ResponseProperties
    {
        internal static bool TryReadString(IReadOnlyDictionary<string, object?>? properties, string key, out string? text)
        {
            text = null;
            if (properties is null || !properties.TryGetValue(key, out var value))
            {
                return true;
            }

            text = value switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                _ => null,
            };
            return text is not null;
        }

        internal static bool TryReadCost(IReadOnlyDictionary<string, object?>? properties, out double? cost)
        {
            cost = null;
            if (properties is null || !properties.TryGetValue(MinosPropertyKeys.Cost, out var value))
            {
                return true;
            }

            double? number = value switch
            {
                double d => d,
                float f => f,
                decimal m => (double)m,
                int i => i,
                long l => l,
                JsonElement { ValueKind: JsonValueKind.Number } json => json.GetDouble(),
                _ => null,
            };

            if (number is not { } n || n < 0 || !double.IsFinite(n))
            {
                return false;
            }

            cost = n;
            return true;
        }

        internal static bool TryReadBoolean(IReadOnlyDictionary<string, object?>? properties, string key, out bool flag)
        {
            flag = false;
            if (properties is null || !properties.TryGetValue(key, out var value))
            {
                return true;
            }

            bool? read = value switch
            {
                bool b => b,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => null,
            };

            flag = read ?? false;
            return read is not null;
        }

        // Names the key, never its value.
        internal static string MalformedMessage(string key) => key switch
        {
            MinosPropertyKeys.UpstreamProvider => "The response's " + MinosPropertyKeys.UpstreamProvider + " property is not a string.",
            MinosPropertyKeys.Cost => "The response's " + MinosPropertyKeys.Cost + " property is not a cost of zero or more.",
            _ => "The response's " + MinosPropertyKeys.IsCached + " property is not true or false.",
        };
    }
}
