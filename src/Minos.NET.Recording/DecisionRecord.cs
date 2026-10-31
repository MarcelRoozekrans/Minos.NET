using System.Collections.ObjectModel;

namespace Minos.Recording;

/// <summary>
/// What one decision call did: the question set, the state, the model and endpoint that served it, the answers and
/// usage, or the kind of error. It is the immutable value a recorder writes and <see cref="DecisionRecordFile"/> reads.
/// </summary>
public sealed class DecisionRecord
{
    internal DecisionRecord(
        string decisionId,
        string? erasureKey,
        DateTimeOffset started,
        TimeSpan latency,
        string provider,
        string? endpoint,
        string? defaultModel,
        string? requestedModel,
        string? servedModel,
        string? responseId,
        string? upstreamProvider,
        DecisionUsage? usage,
        bool isCached,
        QuestionSetDefinition definition,
        DecisionContent state,
        int imageCount,
        IReadOnlyList<string> imageHashes,
        IReadOnlyList<RecordedAnswer> answers,
        DecisionErrorKind? errorKind,
        IReadOnlyList<string> refusedKeys,
        ShadowResult? shadow = null)
    {
        DecisionId = decisionId;
        ErasureKey = erasureKey;
        Started = started;
        Latency = latency;
        Provider = provider;
        Endpoint = endpoint;
        DefaultModel = defaultModel;
        RequestedModel = requestedModel;
        ServedModel = servedModel;
        ResponseId = responseId;
        UpstreamProvider = upstreamProvider;
        Usage = usage;
        IsCached = isCached;
        Definition = definition;
        State = state;
        ImageCount = imageCount;
        ImageHashes = imageHashes;
        Answers = answers;
        ErrorKind = errorKind;
        RefusedKeys = refusedKeys;
        Shadow = shadow;
    }

    /// <summary>Gets the id that ties the record to its outcomes and erasures.</summary>
    public string DecisionId { get; }

    /// <summary>Gets the key an erasure uses to remove every record of one subject, or <see langword="null"/>.</summary>
    public string? ErasureKey { get; }

    /// <summary>Gets when the call started.</summary>
    public DateTimeOffset Started { get; }

    /// <summary>Gets how long the call took. A file keeps it as milliseconds with a fraction, which is exact to the tick.</summary>
    public TimeSpan Latency { get; }

    /// <summary>Gets the name of the provider client that served the call.</summary>
    public string Provider { get; }

    /// <summary>Gets the provider's endpoint as text, or <see langword="null"/>.</summary>
    public string? Endpoint { get; }

    /// <summary>Gets the model the client uses when a request names none, or <see langword="null"/>.</summary>
    public string? DefaultModel { get; }

    /// <summary>Gets the model the request asked for, or <see langword="null"/> when it named none.</summary>
    public string? RequestedModel { get; }

    /// <summary>Gets the model the provider says served the call, or <see langword="null"/>.</summary>
    public string? ServedModel { get; }

    /// <summary>Gets the id the provider gave the response, or <see langword="null"/>.</summary>
    public string? ResponseId { get; }

    /// <summary>Gets the upstream provider behind a router, or <see langword="null"/>.</summary>
    public string? UpstreamProvider { get; }

    /// <summary>Gets the token usage and cost, or <see langword="null"/> when the response reported none.</summary>
    public DecisionUsage? Usage { get; }

    /// <summary>Gets a value that says whether the answers came from a cache stage instead of the provider.</summary>
    public bool IsCached { get; }

    /// <summary>Gets the questions that were asked.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>Gets the state the questions were asked about, as it was written: after any redaction.</summary>
    public DecisionContent State { get; }

    /// <summary>Gets the number of images that were sent.</summary>
    public int ImageCount { get; }

    /// <summary>Gets the SHA-256 of each image, as lowercase hex. The images themselves are not recorded.</summary>
    public IReadOnlyList<string> ImageHashes { get; }

    /// <summary>Gets the answers; empty unless the call succeeded.</summary>
    public IReadOnlyList<RecordedAnswer> Answers { get; }

    /// <summary>Gets the kind of error the call ended with, or <see langword="null"/> when it succeeded.</summary>
    public DecisionErrorKind? ErrorKind { get; }

    /// <summary>Gets the keys of the questions the model refused; empty when none was refused.</summary>
    public IReadOnlyList<string> RefusedKeys { get; }

    /// <summary>Gets what the shadow candidate answered for the same decision, or <see langword="null"/> when the decision was not shadowed.</summary>
    public ShadowResult? Shadow { get; }

    /// <summary>Returns a copy of the record that carries <paramref name="shadow"/> and is otherwise the same.</summary>
    /// <param name="shadow">The candidate's result.</param>
    internal DecisionRecord WithShadow(ShadowResult shadow)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        return new DecisionRecord(
            DecisionId, ErasureKey, Started, Latency, Provider, Endpoint, DefaultModel, RequestedModel, ServedModel,
            ResponseId, UpstreamProvider, Usage, IsCached, Definition, State, ImageCount, ImageHashes, Answers, ErrorKind,
            RefusedKeys, shadow);
    }

    internal static IReadOnlyList<T> Freeze<T>(IEnumerable<T>? items)
    {
        var array = items is null ? [] : items.ToArray();
        return array.Length == 0 ? [] : new ReadOnlyCollection<T>(array);
    }
}
