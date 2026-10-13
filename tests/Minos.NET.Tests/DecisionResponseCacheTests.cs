namespace Minos.Tests;

public sealed class DecisionResponseCacheTests
{
    private static DecisionResponse Response()
        => new(
            QuestionSets.UrgencyDefinition(),
            [QuestionAnswer.Noul(0.75)],
            model: "jev-latest",
            usage: new DecisionUsage { InputTokens = 296, OutputTokens = 20, Cost = 0.001 });

    [Fact]
    public void A_response_from_the_provider_is_not_cached()
    {
        var response = Response();

        Assert.False(response.IsCached);
        Assert.Null(response.CacheHitTag);
    }

    [Fact]
    public void AsCached_keeps_the_answers_model_and_usage_and_sets_the_flag()
    {
        var response = Response();

        var cached = response.AsCached();

        Assert.NotSame(response, cached);
        Assert.False(response.IsCached);
        Assert.True(cached.IsCached);
        Assert.Same(response.Definition, cached.Definition);
        Assert.Same(response.Slots, cached.Slots);
        Assert.Same(response.Probabilities, cached.Probabilities);
        Assert.Equal("jev-latest", cached.Model);
        Assert.Equal(response.Usage, cached.Usage);
        Assert.Equal(0.75, cached.Answers[0].Probability);
    }

    [Fact]
    public void The_hit_tag_is_one_shared_boxed_true()
    {
        var first = Response().AsCached();
        var second = Response().AsCached();

        Assert.Equal(true, first.CacheHitTag);
        Assert.Same(first.CacheHitTag, second.CacheHitTag);
    }
}
