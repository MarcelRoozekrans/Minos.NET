using System.Text.Json.Serialization;

namespace Minos.AotSurface;

/// <summary>A typed state, so <c>EvaluateAsync&lt;T, TState&gt;</c> and <c>DecisionContent.FromValue&lt;T&gt;</c> have one.</summary>
public sealed record SurfaceState(string Subject, string Body);

[JsonSerializable(typeof(SurfaceState))]
internal sealed partial class SurfaceStateJsonContext : JsonSerializerContext;

/// <summary>A question set linked to <see cref="SurfaceState"/>, so ILC analyses the generated state-set code too.</summary>
[Questions(State = typeof(SurfaceState))]
public partial record SurfaceStateTriage
{
    [Noul("Does `body` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }
}
