using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Minos;

/// <summary>
/// How a routing stage, such as escalation, answered a request: per question the client that answered and why, and
/// each call it made. <see cref="DecisionResponse.Path"/> holds it.
/// </summary>
/// <remarks>Immutable and safe to share across threads.</remarks>
public sealed class DecisionPath
{
    /// <summary>Initializes a new instance of the <see cref="DecisionPath"/> class from copies of the lists.</summary>
    /// <param name="routes">One route per question, in definition order.</param>
    /// <param name="calls">Each call made, in the order made; the first is the primary client's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="routes"/> or <paramref name="calls"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="calls"/> is empty, or a route is a <see langword="default"/> value.</exception>
    public DecisionPath(IReadOnlyList<QuestionRoute> routes, IReadOnlyList<DecisionCall> calls)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(calls);
        if (calls.Count == 0)
        {
            throw new ArgumentException("A path has at least one call: the primary client's.", nameof(calls));
        }

        QuestionRoute[] routeArray = [.. routes];
        for (var i = 0; i < routeArray.Length; i++)
        {
            // A default route has no key or client name; the message names the position only, never a question key.
            if (routeArray[i].Key is null)
            {
                throw new ArgumentException($"Route {i} is a default value; make routes with the QuestionRoute constructor.", nameof(routes));
            }
        }

        DecisionCall[] callArray = [.. calls];
        Routes = routeArray.Length == 0 ? [] : new ReadOnlyCollection<QuestionRoute>(routeArray);
        Calls = new ReadOnlyCollection<DecisionCall>(callArray);
    }

    // Takes ownership of arrays a routing stage built for this path alone, without copying or checking them. A factory
    // over a private constructor, so a caller elsewhere with arrays in hand never picks it instead of the copying one.
    internal static DecisionPath FromOwnedArrays(QuestionRoute[] routes, DecisionCall[] calls) => new(routes, calls);

    private DecisionPath(QuestionRoute[] routes, DecisionCall[] calls)
    {
        Debug.Assert(calls.Length != 0, "A path has at least one call.");
        Routes = routes.Length == 0 ? [] : new ReadOnlyCollection<QuestionRoute>(routes);
        Calls = new ReadOnlyCollection<DecisionCall>(calls);
    }

    /// <summary>Gets one route per question, in definition order.</summary>
    public IReadOnlyList<QuestionRoute> Routes { get; }

    /// <summary>Gets each call made, in the order made; the first is the primary client's.</summary>
    public IReadOnlyList<DecisionCall> Calls { get; }

    /// <summary>The certainty a threshold is compared with: the larger of <c>p</c> and <c>1 - p</c> for a Noul, otherwise the confidence.</summary>
    /// <param name="kind">The question's kind.</param>
    /// <param name="slot">The question's answer.</param>
    /// <returns>A value between 0.5 and 1 for a Noul, and the confidence for a Choice or Score.</returns>
    internal static double Certainty(QuestionKind kind, in AnswerSlot slot)
        => kind == QuestionKind.Noul ? Math.Max(slot.Value, 1 - slot.Value) : slot.Confidence;
}
