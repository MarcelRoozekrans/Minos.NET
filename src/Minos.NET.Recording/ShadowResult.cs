namespace Minos.Recording;

/// <summary>
/// What the shadow candidate answered for one decision: the provider and model that served it, the answers and usage, or
/// the kind of error. It is stored in <see cref="DecisionRecord.Shadow"/> beside the primary's own answers and never
/// changes them.
/// </summary>
public sealed class ShadowResult
{
    internal ShadowResult(
        string provider,
        string? endpoint,
        string? defaultModel,
        string? requestedModel,
        string? servedModel,
        string? responseId,
        string? upstreamProvider,
        TimeSpan latency,
        DecisionUsage? usage,
        IReadOnlyList<RecordedAnswer> answers,
        DecisionErrorKind? errorKind,
        IReadOnlyList<string> refusedKeys)
    {
        Provider = provider;
        Endpoint = endpoint;
        DefaultModel = defaultModel;
        RequestedModel = requestedModel;
        ServedModel = servedModel;
        ResponseId = responseId;
        UpstreamProvider = upstreamProvider;
        Latency = latency;
        Usage = usage;
        Answers = DecisionRecord.Freeze(answers);
        ErrorKind = errorKind;
        RefusedKeys = DecisionRecord.Freeze(refusedKeys);
    }

    /// <summary>Gets the name of the candidate provider client.</summary>
    public string Provider { get; }

    /// <summary>Gets the candidate's endpoint as text, or <see langword="null"/>.</summary>
    public string? Endpoint { get; }

    /// <summary>Gets the model the candidate client uses when a request names none, or <see langword="null"/>.</summary>
    public string? DefaultModel { get; }

    /// <summary>Gets the model the candidate was asked for, or <see langword="null"/> when it named none.</summary>
    public string? RequestedModel { get; }

    /// <summary>Gets the model the candidate's provider says served the call, or <see langword="null"/>.</summary>
    public string? ServedModel { get; }

    /// <summary>Gets the id the candidate's provider gave the response, or <see langword="null"/>.</summary>
    public string? ResponseId { get; }

    /// <summary>Gets the upstream provider behind a router, or <see langword="null"/>.</summary>
    public string? UpstreamProvider { get; }

    /// <summary>Gets how long the candidate took. A file keeps it as milliseconds with a fraction, which is exact to the tick.</summary>
    public TimeSpan Latency { get; }

    /// <summary>Gets the candidate's token usage and cost, or <see langword="null"/> when its response reported none.</summary>
    public DecisionUsage? Usage { get; }

    /// <summary>Gets the candidate's answers; empty unless its call succeeded.</summary>
    public IReadOnlyList<RecordedAnswer> Answers { get; }

    /// <summary>Gets the kind of error the candidate's call ended with, or <see langword="null"/> when it succeeded.</summary>
    public DecisionErrorKind? ErrorKind { get; }

    /// <summary>Gets the keys of the questions the candidate refused; empty when none was refused.</summary>
    public IReadOnlyList<string> RefusedKeys { get; }
}
