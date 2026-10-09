namespace Minos.Samples.Reranking;

/// <summary>A deliberately simple first-stage retriever: it counts the distinct query words that an article shares.</summary>
public static class KeywordShortlist
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "that", "this", "you", "your", "are", "was", "were", "but", "not", "can",
        "has", "had", "have", "from", "they", "them", "what", "when", "how", "its", "our", "any", "all",
    };

    /// <summary>Lower-cases the text, splits it on anything that is not a letter or a digit, and drops short words and stop words.</summary>
    public static IReadOnlySet<string> Tokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        var lower = text.ToLowerInvariant();
        for (var i = 0; i <= lower.Length; i++)
        {
            var inWord = i < lower.Length && char.IsLetterOrDigit(lower[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                var token = lower.Substring(start, i - start);
                if (token.Length >= 3 && !StopWords.Contains(token))
                {
                    tokens.Add(token);
                }

                start = -1;
            }
        }

        return tokens;
    }

    /// <summary>How many distinct query tokens appear in the article's title and body.</summary>
    public static int Score(string query, Article article)
    {
        ArgumentNullException.ThrowIfNull(article);
        var wanted = Tokens(query);
        var present = Tokens(article.Title + " " + article.Body);
        var score = 0;
        foreach (var token in wanted)
        {
            if (present.Contains(token))
            {
                score++;
            }
        }

        return score;
    }

    /// <summary>The best <paramref name="count"/> articles, by score and then by id.</summary>
    public static IReadOnlyList<Article> Top(string query, IReadOnlyList<Article> articles, int count)
    {
        ArgumentNullException.ThrowIfNull(articles);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var scored = new (Article Article, int Score)[articles.Count];
        for (var i = 0; i < scored.Length; i++)
        {
            scored[i] = (articles[i], Score(query, articles[i]));
        }

        Array.Sort(scored, static (x, y) =>
        {
            var byScore = y.Score.CompareTo(x.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(x.Article.Id, y.Article.Id);
        });

        var top = new Article[Math.Min(count, scored.Length)];
        for (var i = 0; i < top.Length; i++)
        {
            top[i] = scored[i].Article;
        }

        return top;
    }
}
