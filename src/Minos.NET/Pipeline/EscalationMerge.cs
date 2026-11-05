using System.Collections.Concurrent;
using System.Diagnostics;
using Minos.Telemetry;

namespace Minos;

/// <summary>The escalation stage's subset definitions and its merge of two clients' answers into one response.</summary>
internal static class EscalationMerge
{
    // QuestionSetDefinition's protocol-data slot for the subset cache.
    private const int SubsetSlot = 3;

    // The most subsets one definition keeps, and the most questions a definition can have for its subsets to be cached:
    // the cache key is a bitmask of the unsure questions' positions.
    internal const int MaxCachedSubsets = 64;
    private const int MaxMaskedQuestions = 64;

    /// <summary>The definition of the questions at <paramref name="unsure"/>, each question's definition unchanged, in definition order.</summary>
    /// <remarks>
    /// The same pattern of unsure questions gets the same instance, so a repeating pattern allocates no new definition
    /// and a protocol's per-definition caches keep working. At most <see cref="MaxCachedSubsets"/> patterns are kept per
    /// definition; past that, and for a definition of more than 64 questions, the subset is built each time.
    /// </remarks>
    internal static QuestionSetDefinition Subset(QuestionSetDefinition definition, int[] unsure)
    {
        if (definition.QuestionArray.Length > MaxMaskedQuestions)
        {
            return Build(definition, unsure);
        }

        var mask = 0UL;
        foreach (var index in unsure)
        {
            mask |= 1UL << index;
        }

        var cache = Cache(definition);
        if (cache.Subsets.TryGetValue(mask, out var cached))
        {
            return cached;
        }

        var subset = Build(definition, unsure);
        if (cache.IsFull)
        {
            return subset;
        }

        // The count check and the add happen together, so racing calls never push the cache past its cap. Only a miss
        // on a cache that is not yet full takes the lock; a hit above reads without one.
        lock (cache)
        {
            if (cache.Count >= MaxCachedSubsets)
            {
                return subset;
            }

            var stored = cache.Subsets.GetOrAdd(mask, subset);
            if (ReferenceEquals(stored, subset) && ++cache.Count == MaxCachedSubsets)
            {
                cache.IsFull = true;
            }

            return stored;
        }
    }

    /// <summary>The number of subsets cached for <paramref name="definition"/>, for tests.</summary>
    internal static int CachedSubsetCount(QuestionSetDefinition definition) => Cache(definition).Subsets.Count;

    /// <summary>
    /// The primary's response with the target's answers for the unsure questions in their places: each escalated slot is
    /// the target's, moved to the primary definition's offset, with its probabilities copied there. The primary's
    /// arrays are copied, not changed.
    /// </summary>
    /// <param name="primary">The primary client's response.</param>
    /// <param name="target">The target's response, for the subset definition of <paramref name="unsure"/>.</param>
    /// <param name="unsure">The positions in the primary's definition of the target's questions, in order.</param>
    /// <param name="routes">The routes; the escalated questions' routes are replaced.</param>
    /// <param name="targetName">The target's name in the path.</param>
    /// <param name="calls">The calls made: the primary's, or the inner path's in its place, then the target's.</param>
    internal static DecisionResponse Merge(DecisionResponse primary, DecisionResponse target, int[] unsure, QuestionRoute[] routes, string targetName, DecisionCall[] calls)
    {
        var definition = primary.Definition;
        var questions = definition.QuestionArray;
        var slots = (AnswerSlot[])primary.Slots.Clone();
        var probabilities = (double[])primary.Probabilities.Clone();
        for (var j = 0; j < unsure.Length; j++)
        {
            var i = unsure[j];
            var question = questions[i];
            var slot = target.Slots[j];

            // A Noul has no probability slice, and readers give its slot offset 0.
            var offset = question.Kind == QuestionKind.Noul ? 0 : definition.Offsets[i];
            target.Probabilities.AsSpan(slot.Offset, question.OptionArray.Length).CopyTo(probabilities.AsSpan(offset));
            slots[i] = slot with { Offset = offset };
            routes[i] = new QuestionRoute(question.Key, targetName, RouteReason.Escalated, routes[i].PrimaryCertainty, DecisionPath.Certainty(question.Kind, slot));
        }

        // Every answer the primary did not give came from the target, so the response is cached only when both were.
        return Build(primary, slots, probabilities, routes, calls, primary.IsCached && target.IsCached, targetName, escalated: unsure.Length, skipped: null);
    }

    /// <summary>The primary's answers, unchanged and sharing its arrays, with a path; for when the target answered nothing.</summary>
    /// <param name="primary">The primary client's response.</param>
    /// <param name="routes">The routes.</param>
    /// <param name="calls">The calls made: the primary's, or the inner path's in its place, then the target's when it was called or skipped.</param>
    /// <param name="targetName">The target's name for the telemetry, or <see langword="null"/> when no question was unsure.</param>
    /// <param name="skipped">Why the unsure questions were not escalated: <see cref="DecisionTelemetry.SkippedNoTimeLeft"/> or <see cref="DecisionTelemetry.SkippedTargetFailed"/>.</param>
    internal static DecisionResponse KeepPrimary(DecisionResponse primary, QuestionRoute[] routes, DecisionCall[] calls, string? targetName = null, string? skipped = null)
        => Build(primary, primary.Slots, primary.Probabilities, routes, calls, primary.IsCached, targetName, escalated: 0, skipped);

    // The primary's model, id and upstream provider; the usage summed per field over the calls, each field null only
    // when no call reported it.
    private static DecisionResponse Build(DecisionResponse primary, AnswerSlot[] slots, double[] probabilities, QuestionRoute[] routes, DecisionCall[] calls, bool isCached, string? targetName, int escalated, string? skipped)
    {
        int? inputTokens = null;
        int? outputTokens = null;
        double? cost = null;
        foreach (var call in calls)
        {
            inputTokens = Sum(inputTokens, call.Usage?.InputTokens);
            outputTokens = Sum(outputTokens, call.Usage?.OutputTokens);
            cost = Sum(cost, call.Usage?.Cost);
        }

        // A taken-over inner path keeps its story for the questions this stage left alone: a confident question's inner
        // route stands unless it only says the question fell back, so an answer the inner stage escalated still reads
        // Escalated to its target.
        if (primary.Path is { } inner)
        {
            KeepInnerRoutes(routes, inner.Routes);
        }

        // The telemetry holder exists only when a question was unsure, which is when the caller names the target. When the
        // primary's path is taken over, primary.Path is the inner path whose telemetry holder belongs to this response too.
        Debug.Assert(targetName is not null || (escalated == 0 && skipped is null), "Only a call with an unsure question escalates or skips.");
        var escalation = targetName is null ? null : new EscalationTelemetry(targetName, escalated, skipped);
        var response = new DecisionResponse(primary.Definition, slots, probabilities, primary.Model, inputTokens, outputTokens, cost, primary.Id, primary.UpstreamProvider, isCached)
        {
            Path = DecisionPath.FromOwnedArrays(routes, calls, escalation),
        };

        if (escalation is not null)
        {
            escalation.Owner = response;
        }

        return response;
    }

    private static void KeepInnerRoutes(QuestionRoute[] routes, IReadOnlyList<QuestionRoute> innerRoutes)
    {
        Debug.Assert(innerRoutes.Count == routes.Length, "An inner path has one route per question.");
        for (var i = 0; i < routes.Length; i++)
        {
            var innerRoute = innerRoutes[i];
            if (routes[i].Reason == RouteReason.Confident && innerRoute.Reason != RouteReason.FellBack)
            {
                routes[i] = innerRoute;
            }
        }
    }

    private static QuestionSetDefinition Build(QuestionSetDefinition definition, int[] unsure)
    {
        var questions = new QuestionDefinition[unsure.Length];
        for (var j = 0; j < unsure.Length; j++)
        {
            questions[j] = definition.QuestionArray[unsure[j]];
        }

        return new QuestionSetDefinition(questions);
    }

    private static SubsetCache Cache(QuestionSetDefinition definition)
        => definition.GetOrAddProtocolData(SubsetSlot, static _ => new SubsetCache());

    private static int? Sum(int? total, int? value) => value is null ? total : (total ?? 0) + value;

    private static double? Sum(double? total, double? value) => value is null ? total : (total ?? 0) + value;

    // The subsets by unsure-index bitmask. Count is written only under the lock, and mirrors Subsets.Count without taking
    // the dictionary's bucket locks. IsFull lets a miss on a full cache skip the lock.
    private sealed class SubsetCache
    {
        private volatile bool _isFull;

        public ConcurrentDictionary<ulong, QuestionSetDefinition> Subsets { get; } = new();

        public int Count { get; set; }

        public bool IsFull
        {
            get => _isFull;
            set => _isFull = value;
        }
    }
}
