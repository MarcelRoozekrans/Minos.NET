# Speculative fan-out

Ask every question you might need in a single request, then let your code read only the answers that matter for this
input. Jev answers all of them in one call. The questions share one round trip, so adding another question costs far
less than sending another request, and there is no reason to decide up front which questions apply. Each question still
adds some tokens to the call.

TypeSafe describes the pattern in [Speculative fan-out](https://docs.typesafe.ai/patterns/fan-out), including their
measured numbers. This page shows it with a typed question set.

## The questions

An app-store review gets five questions at once: its topic, the reviewer's sentiment, and three yes/no questions. The
data-loss question only matters for crash reports, and nothing below reads the last one; they are asked for every
review anyway.

<!-- snippet: FanOutQuestions -->
```cs
public enum ReviewTopic
{
    [Criteria("The app crashes, freezes or closes on its own")]
    Crash,

    [Criteria("The app is slow, drains the battery or uses too much data")]
    Performance,

    [Criteria("The price, the subscription or in-app purchases")]
    Pricing,

    [Criteria("A feature the reviewer wants added")]
    FeatureIdea,

    [Criteria("Mostly praise, with no complaint")]
    Praise,
}

public enum Sentiment
{
    [Level("Very negative")]
    VeryNegative,

    [Level("Negative")]
    Negative,

    [Level("Mixed or neutral")]
    Mixed,

    [Level("Positive")]
    Positive,

    [Level("Very positive")]
    VeryPositive,
}

// Five questions, one request. Not every answer matters for every review: data loss is only read for crash
// reports, and no rule below reads WouldRecommend at all. Each question adds some tokens, but they all share
// the one round trip, so it is cheaper to ask them all than to decide first which ones apply.
[JevQuestions]
public partial record AppReview
{
    [Choice("What is the review mainly about?")]
    public partial Choice<ReviewTopic> Topic { get; }

    [Score("How does the reviewer feel about the app?")]
    public partial Score<Sentiment> Sentiment { get; }

    [Noul("Does the reviewer say they lost data, such as notes, files or saved progress?")]
    public partial Noul MentionsDataLoss { get; }

    [Noul("Does the review name a competing app?")]
    public partial Noul NamesCompetitor { get; }

    [Noul("Would the reviewer still recommend the app to a friend?")]
    public partial Noul WouldRecommend { get; }
}
```
<!-- endSnippet -->

## Reading the answers

Routing reads `Expected` on the sentiment Score, the probability-weighted level, so 1.5 means "between negative and
mixed", and `Probability` on the Nouls. Each rule adds a flag, so one review can go to more than one team. For a
pricing review, the data-loss answer is present and simply not read.

<!-- snippet: FanOutRouting -->
```cs
[Flags]
public enum ReviewActions
{
    None = 0,
    PageOnCall = 1,
    ToProductTeam = 2,
    ToCompetitiveResearch = 4,
}

public static class ReviewRouting
{
    public static ReviewActions Route(AppReview review)
        => Route(review.Topic, review.Sentiment, review.MentionsDataLoss, review.NamesCompetitor);

    // The rules read four of the five answers; WouldRecommend is there for whoever wants it later.
    public static ReviewActions Route(
        Choice<ReviewTopic> topic, Score<Sentiment> sentiment, Noul mentionsDataLoss, Noul namesCompetitor)
    {
        var actions = ReviewActions.None;

        // Data loss is only read for crash reports; for any other topic its answer is present and ignored.
        if (topic.Value == ReviewTopic.Crash && mentionsDataLoss.Probability > 0.6)
        {
            actions |= ReviewActions.PageOnCall;
        }

        // Below 1.5 is nearer "Negative" than "Mixed".
        if (topic.Value == ReviewTopic.Pricing && sentiment.Expected < 1.5)
        {
            actions |= ReviewActions.ToProductTeam;
        }

        if (namesCompetitor.Probability > 0.7)
        {
            actions |= ReviewActions.ToCompetitiveResearch;
        }

        return actions;
    }
}
```
<!-- endSnippet -->

## Calling Jev

<!-- snippet: FanOutCall -->
```cs
public static async Task<ReviewActions?> TriageAsync(IJevClient jev, string reviewText, CancellationToken ct)
{
    var result = await jev.EvaluateAsync<AppReview>(reviewText, ct);
    // On failure, result.Error.Kind and .Message say why: log them and leave the review for a person.
    return result.IsSuccess ? ReviewRouting.Route(result.Value) : null;
}
```
<!-- endSnippet -->

## C# notes

- Each answer is a struct over the response's shared buffer: reading `Value`, `Expected`, `Confidence` or
  `Probability` allocates nothing.
- The thresholds here (0.6, 1.5, 0.7) are starting points. Tune them on your own reviews.
- The same questions can be built at run time instead of declared; see
  [Question sets built at run time](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev#question-sets-built-at-run-time).
