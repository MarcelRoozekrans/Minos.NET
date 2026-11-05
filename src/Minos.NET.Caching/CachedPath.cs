namespace Minos.Caching;

/// <summary>
/// The escalation path of a cached answer, as the cache keeps it: the routes without their question keys, which the
/// definition gives back when the response is rebuilt, and the calls as they were stored.
/// </summary>
internal sealed class CachedPath
{
    public CachedPath(CachedRoute[] routes, DecisionCall[] calls)
    {
        Routes = routes;
        Calls = calls;
    }

    public CachedRoute[] Routes { get; }

    public DecisionCall[] Calls { get; }

    /// <summary>The stored form of <paramref name="path"/>, or <see langword="null"/> for no path.</summary>
    /// <param name="path">A response's path.</param>
    /// <returns>The stored form.</returns>
    public static CachedPath? From(DecisionPath? path)
    {
        if (path is null)
        {
            return null;
        }

        var routes = new CachedRoute[path.Routes.Count];
        for (var i = 0; i < routes.Length; i++)
        {
            var route = path.Routes[i];
            routes[i] = new CachedRoute(route.AnsweredBy, route.Reason, route.PrimaryCertainty, route.TargetCertainty);
        }

        return new CachedPath(routes, [.. path.Calls]);
    }

    /// <summary>The path for <paramref name="definition"/>: each route takes the key of the question in its position.</summary>
    /// <param name="definition">The definition whose question count this path's route count matches.</param>
    /// <returns>The path.</returns>
    public DecisionPath ToPath(QuestionSetDefinition definition)
    {
        var questions = definition.QuestionArray;
        var routes = new QuestionRoute[Routes.Length];
        for (var i = 0; i < routes.Length; i++)
        {
            var route = Routes[i];
            routes[i] = new QuestionRoute(questions[i].Key, route.AnsweredBy, route.Reason, route.PrimaryCertainty, route.TargetCertainty);
        }

        return new DecisionPath(routes, Calls);
    }
}

/// <summary>One stored route: a <see cref="QuestionRoute"/> without its key.</summary>
/// <param name="AnsweredBy">The client that answered.</param>
/// <param name="Reason">Why it did.</param>
/// <param name="PrimaryCertainty">The primary client's certainty.</param>
/// <param name="TargetCertainty">The target's certainty, or <see langword="null"/>.</param>
internal readonly record struct CachedRoute(string AnsweredBy, RouteReason Reason, double PrimaryCertainty, double? TargetCertainty);
