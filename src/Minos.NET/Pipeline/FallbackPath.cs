namespace Minos;

/// <summary>The fallback stage's path for a response a step after the inner client gave.</summary>
internal static class FallbackPath
{
    /// <summary>
    /// <paramref name="answer"/> with the path of the chain that led to it, keeping the answering step's own model, id,
    /// upstream provider, usage and cache flag. The answer's arrays are shared, not copied.
    /// </summary>
    /// <remarks>
    /// Without a path of its own, the answer gets one route per question, answered by <paramref name="answeredBy"/> and
    /// routed <see cref="RouteReason.FellBack"/>, and the calls are <paramref name="priorCalls"/> followed by
    /// <paramref name="answerCall"/>. With one, from the step's own routing stage, the path is taken over: its routes stay
    /// as they are, and its calls follow <paramref name="priorCalls"/> in place of <paramref name="answerCall"/>.
    /// </remarks>
    /// <param name="answer">The answering step's response, for the request's own definition.</param>
    /// <param name="answeredBy">The answering step's name.</param>
    /// <param name="priorCalls">The calls before the answering step's: failed and skipped steps, in order. Not kept.</param>
    /// <param name="answerCall">The answering step's call.</param>
    internal static DecisionResponse Build(DecisionResponse answer, string answeredBy, List<DecisionCall> priorCalls, DecisionCall answerCall)
    {
        QuestionRoute[] routes;
        DecisionCall[] calls;
        if (answer.Path is { } inner)
        {
            routes = [.. inner.Routes];
            calls = new DecisionCall[priorCalls.Count + inner.Calls.Count];
            priorCalls.CopyTo(calls);
            for (var i = 0; i < inner.Calls.Count; i++)
            {
                calls[priorCalls.Count + i] = inner.Calls[i];
            }
        }
        else
        {
            routes = FellBack(answer, answeredBy);
            calls = new DecisionCall[priorCalls.Count + 1];
            priorCalls.CopyTo(calls);
            calls[^1] = answerCall;
        }

        return new DecisionResponse(
            answer.Definition,
            answer.Slots,
            answer.Probabilities,
            answer.Model,
            answer.InputTokens,
            answer.OutputTokens,
            answer.Cost,
            answer.Id,
            answer.UpstreamProvider,
            answer.IsCached)
        {
            Path = DecisionPath.FromOwnedArrays(routes, calls),
        };
    }

    private static QuestionRoute[] FellBack(DecisionResponse answer, string answeredBy)
    {
        var questions = answer.Definition.QuestionArray;
        var slots = answer.Slots;
        var routes = questions.Length == 0 ? [] : new QuestionRoute[questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            var question = questions[i];
            routes[i] = new QuestionRoute(question.Key, answeredBy, RouteReason.FellBack, DecisionPath.Certainty(question.Kind, slots[i]), null);
        }

        return routes;
    }
}
