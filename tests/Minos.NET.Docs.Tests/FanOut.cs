namespace Minos.Docs.Tests;

#region FanOutQuestions
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
[Questions]
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
#endregion

#region FanOutRouting
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
#endregion

public static class ReviewTriage
{
    #region FanOutCall
    public static async Task<ReviewActions?> TriageAsync(
        IDecisionClient client, string reviewText, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<AppReview>(reviewText, cancellationToken);
        // On failure, result.Error.Kind and .Message say why: log them and leave the review for a person.
        return result.IsSuccess ? ReviewRouting.Route(result.Value) : null;
    }
    #endregion
}

#region FanOutBuilt
public sealed class BuiltReviewTriage
{
    private readonly QuestionSet _questions;
    private readonly ChoiceHandle<ReviewTopic> _topic;
    private readonly ScoreHandle<Sentiment> _sentiment;
    private readonly NoulHandle _mentionsDataLoss;
    private readonly NoulHandle _namesCompetitor;

    // Build the set once and share it: it is immutable and thread-safe. Each question method hands back a
    // handle, and the handle reads that question's answer.
    public BuiltReviewTriage()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice("topic", "What is the review mainly about?", out _topic, options => options
                .Describe(ReviewTopic.Crash, "The app crashes, freezes or closes on its own")
                .Describe(ReviewTopic.Performance, "The app is slow, drains the battery or uses too much data")
                .Describe(ReviewTopic.Pricing, "The price, the subscription or in-app purchases")
                .Describe(ReviewTopic.FeatureIdea, "A feature the reviewer wants added")
                .Describe(ReviewTopic.Praise, "Mostly praise, with no complaint"))
            .Score("sentiment", "How does the reviewer feel about the app?", out _sentiment, levels => levels
                .Level(Sentiment.VeryNegative, "Very negative")
                .Level(Sentiment.Negative, "Negative")
                .Level(Sentiment.Mixed, "Mixed or neutral")
                .Level(Sentiment.Positive, "Positive")
                .Level(Sentiment.VeryPositive, "Very positive"))
            .Noul(
                "mentions_data_loss",
                "Does the reviewer say they lost data, such as notes, files or saved progress?",
                out _mentionsDataLoss)
            .Noul("names_competitor", "Does the review name a competing app?", out _namesCompetitor)
            .Noul("would_recommend", "Would the reviewer still recommend the app to a friend?", out _) // not read here
            .Build();

        // These questions are fixed, so a failure is a bug in this code: built.Error.Failures lists what is wrong.
        _questions = built.IsSuccess ? built.Value : throw new InvalidOperationException(built.Error.Message);
    }

    public async Task<ReviewActions?> TriageAsync(
        IDecisionClient client, string reviewText, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync(_questions, reviewText, cancellationToken);
        if (result.IsFailure)
        {
            return null;
        }

        var answers = result.Value;
        return ReviewRouting.Route(
            answers.Get(_topic),
            answers.Get(_sentiment),
            answers.Get(_mentionsDataLoss),
            answers.Get(_namesCompetitor));
    }
}
#endregion
