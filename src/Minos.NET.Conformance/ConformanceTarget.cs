using System.Text.Json.Serialization;

namespace Minos.Conformance;

/// <summary>The wire format a target speaks: it decides the answer type names S2 expects and how the answers are read.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConformanceWire>))]
public enum ConformanceWire
{
    /// <summary>System One, <c>POST v1/systemone</c>: an <c>answers</c> object keyed by question; a Noul's type is <c>noul</c>.</summary>
    SystemOne,

    /// <summary>OpenAI's Decisions API, <c>POST v1/decisions</c>: an <c>answers</c> array matched by <c>name</c>; a Noul's type is <c>predicate</c>.</summary>
    Decisions,
}

/// <summary>How the suite runs: against the real endpoint, recording what it answers, or from a recording.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConformanceMode>))]
public enum ConformanceMode
{
    /// <summary>Real calls; nothing is saved.</summary>
    Live,

    /// <summary>Real calls, each probe's exchange saved, errors included, into a fixture file.</summary>
    Record,

    /// <summary>No calls: each probe is answered from a fixture file by its id.</summary>
    Replay,
}

/// <summary>An endpoint to check: its name, wire format, the model to ask, the rule its reported model follows, and how to build a client over an <see cref="HttpClient"/>.</summary>
public sealed class ConformanceTarget
{
    /// <summary>Initializes a new instance of the <see cref="ConformanceTarget"/> class.</summary>
    /// <param name="name">The target's name, such as <c>typesafe</c>; a fixture names it too.</param>
    /// <param name="wire">The wire format the endpoint speaks.</param>
    /// <param name="model">The model every probe asks for.</param>
    /// <param name="modelRule">How the reported model must relate to <paramref name="model"/>, or <see langword="null"/> when the target promises no model, which skips R1 and R2 for responses that name none.</param>
    /// <param name="createClient">Builds the client over the runner's <see cref="HttpClient"/>, which has no base address yet; the client owns nothing else.</param>
    public ConformanceTarget(string name, ConformanceWire wire, string model, ModelRule? modelRule, Func<HttpClient, IDecisionClient> createClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(createClient);

        Name = name;
        Wire = wire;
        Model = model;
        ModelRule = modelRule;
        CreateClient = createClient;
    }

    /// <summary>Gets the target's name.</summary>
    public string Name { get; }

    /// <summary>Gets the wire format the endpoint speaks.</summary>
    public ConformanceWire Wire { get; }

    /// <summary>Gets the model every probe asks for.</summary>
    public string Model { get; }

    /// <summary>Gets the rule the reported model follows, or <see langword="null"/> when the target promises no model.</summary>
    public ModelRule? ModelRule { get; }

    /// <summary>Gets the factory that builds the client over the runner's <see cref="HttpClient"/>.</summary>
    public Func<HttpClient, IDecisionClient> CreateClient { get; }
}
