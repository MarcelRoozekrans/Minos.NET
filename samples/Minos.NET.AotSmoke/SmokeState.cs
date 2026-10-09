using System.Text.Json.Serialization;

namespace Minos.AotSmoke;

/// <summary>A typed state, serialized through <see cref="SmokeStateJsonContext"/>, for <see cref="SmokeStateTriage"/>.</summary>
public sealed record SmokeState(string Subject, string Body);

[JsonSerializable(typeof(SmokeState))]
internal sealed partial class SmokeStateJsonContext : JsonSerializerContext;

/// <summary>
/// A question set linked to <see cref="SmokeState"/>, so <c>EvaluateAsync&lt;T, TState&gt;</c> can be exercised
/// under Native AOT alongside the plain-text <see cref="SmokeTriage"/>.
/// </summary>
[JevQuestions(State = typeof(SmokeState))]
public partial record SmokeStateTriage
{
    [Noul("Does `body` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }
}
