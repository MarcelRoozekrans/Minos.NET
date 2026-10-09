using System.Globalization;
using System.Text;

namespace Minos.Samples.Reranking;

public sealed record RankedQuery(string Query, string BestId, IReadOnlyList<string> KeywordOrder, IReadOnlyList<string> DecisionOrder)
{
    /// <summary>Whether the best article is among the first <paramref name="k"/> of the keyword order.</summary>
    public bool KeywordHit(int k) => IsHit(KeywordOrder, BestId, k);

    /// <summary>Whether the best article is among the first <paramref name="k"/> of the Jev order.</summary>
    public bool DecisionHit(int k) => IsHit(DecisionOrder, BestId, k);

    private static bool IsHit(IReadOnlyList<string> order, string bestId, int k)
    {
        for (var i = 0; i < k && i < order.Count; i++)
        {
            if (string.Equals(order[i], bestId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record RerankingReport(IReadOnlyList<RankedQuery> Queries)
{
    public int KeywordHitsAt1 => Queries.Count(q => q.KeywordHit(1));

    public int KeywordHitsAt3 => Queries.Count(q => q.KeywordHit(3));

    public int DecisionHitsAt1 => Queries.Count(q => q.DecisionHit(1));

    public int DecisionHitsAt3 => Queries.Count(q => q.DecisionHit(3));

    public string Render()
    {
        var text = new StringBuilder();
        foreach (var q in Queries)
        {
            text.Append(q.Query).Append('\n');
            text.Append("  keyword  ").Append(Top3(q.KeywordOrder)).Append("  ").Append(Marker(q.KeywordHit(1), q.KeywordHit(3))).Append('\n');
            text.Append("  jev      ").Append(Top3(q.DecisionOrder)).Append("  ").Append(Marker(q.DecisionHit(1), q.DecisionHit(3))).Append("\n\n");
        }

        var n = Queries.Count.ToString(CultureInfo.InvariantCulture);
        text.Append(CultureInfo.InvariantCulture, $"hit@1 keyword {KeywordHitsAt1}/{n} -> jev {DecisionHitsAt1}/{n}\n");
        text.Append(CultureInfo.InvariantCulture, $"hit@3 keyword {KeywordHitsAt3}/{n} -> jev {DecisionHitsAt3}/{n}\n");
        return text.ToString();
    }

    private static string Top3(IReadOnlyList<string> order) => string.Join(' ', order.Take(3));

    private static string Marker(bool at1, bool at3) => at1 ? "hit@1" : at3 ? "hit@3" : "miss";
}

/// <summary>Shortlists articles by keyword, then re-ranks the shortlist with one Jev request that asks a Noul per candidate.</summary>
public static class RerankingSample
{
    public const int ShortlistSize = 8;

    public static async Task<RerankingReport> RunAsync(IDecisionClient jev, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jev);
        var ranked = new List<RankedQuery>();
        foreach (var query in Queries.All)
        {
            var candidates = KeywordShortlist.Top(query.Text, Articles.All, ShortlistSize);

            // The candidates change with every query, so the question set is built at run time.
            var builder = QuestionSet.CreateBuilder();
            var handles = new NoulHandle[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var article = candidates[i];
                builder.Noul(
                    article.Id,
                    "Does this help-centre article answer the customer's question? Title: " + article.Title + ". " + article.Body,
                    out handles[i]);
            }

            var set = builder.Build();
            if (set.IsFailure)
            {
                throw new InvalidOperationException(query.Text + ": " + set.Error.Kind + ": " + set.Error.Message);
            }

            var result = await jev.EvaluateAsync(set.Value, query.Text, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(query.Text + ": " + result.Error.Kind + ": " + result.Error.Message);
            }

            var probabilities = new double[candidates.Count];
            for (var i = 0; i < probabilities.Length; i++)
            {
                probabilities[i] = result.Value.Get(handles[i]).Probability;
            }

            ranked.Add(new RankedQuery(query.Text, query.BestId, Ids(candidates), Rank(candidates, probabilities)));
        }

        return new RerankingReport(ranked);
    }

    /// <summary>Orders the candidates by probability, highest first; equal probabilities keep their keyword order.</summary>
    public static IReadOnlyList<string> Rank(IReadOnlyList<Article> candidates, IReadOnlyList<double> probabilities)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(probabilities);
        if (candidates.Count != probabilities.Count)
        {
            throw new ArgumentException("There must be one probability per candidate.", nameof(probabilities));
        }

        var order = new int[candidates.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (x, y) =>
        {
            var byProbability = probabilities[y].CompareTo(probabilities[x]);
            return byProbability != 0 ? byProbability : x.CompareTo(y);
        });

        var ids = new string[order.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = candidates[order[i]].Id;
        }

        return ids;
    }

    private static string[] Ids(IReadOnlyList<Article> articles)
    {
        var ids = new string[articles.Count];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = articles[i].Id;
        }

        return ids;
    }
}
