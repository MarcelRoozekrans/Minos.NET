using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Minos.Extensions.AI.Tests;

public sealed class CacheScopeTests
{
    private static readonly ChatClientMetadata OpenRouter = new("openrouter", new Uri("https://openrouter.ai/api/v1"), "m1");

    private static readonly QuestionSetDefinition Refund = new(QuestionDefinition.Noul("refund", "Does the customer ask for a refund?"));

    private static readonly string ScriptedType = typeof(ScriptedChatClient).FullName!;

    [Theory]
    [InlineData(ProbabilityMode.Auto, "auto")]
    [InlineData(ProbabilityMode.Logprobs, "logprobs")]
    [InlineData(ProbabilityMode.Verbalized, "verbalized")]
    // A value outside the enum is not Auto: it never makes Auto's fallback on a rejected logprob request.
    [InlineData((ProbabilityMode)7, "7")]
    public void The_scope_is_the_probability_mode(ProbabilityMode mode, string scope)
    {
        using var client = Unanswered(OpenRouter).AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = mode });

        Assert.Equal(scope, client.GetService<DecisionClientMetadata>()!.CacheScope);
    }

    [Theory]
    [InlineData(ProbabilityMode.Auto, "auto")]
    [InlineData(ProbabilityMode.Logprobs, "logprobs")]
    [InlineData(ProbabilityMode.Verbalized, "verbalized")]
    public void Without_metadata_the_scope_names_the_chat_client_type(ProbabilityMode mode, string scope)
    {
        using var client = Unanswered().AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = mode });

        Assert.Equal(scope + "/" + ScriptedType, client.GetService<DecisionClientMetadata>()!.CacheScope);
    }

    [Fact]
    public void Empty_metadata_counts_as_none()
    {
        using var client = Unanswered(new ChatClientMetadata()).AsDecisionClient();

        Assert.Equal("auto/" + ScriptedType, client.GetService<DecisionClientMetadata>()!.CacheScope);
    }

    [Theory]
    [InlineData("openrouter", null, null)]
    [InlineData(null, "https://openrouter.ai/api/v1", null)]
    [InlineData(null, null, "m1")]
    public void Any_part_of_the_metadata_leaves_the_type_out(string? provider, string? endpoint, string? model)
    {
        var metadata = new ChatClientMetadata(provider, endpoint is null ? null : new Uri(endpoint), model);
        using var client = Unanswered(metadata).AsDecisionClient();

        Assert.Equal("auto", client.GetService<DecisionClientMetadata>()!.CacheScope);
    }

    [Fact]
    public void A_model_in_the_options_leaves_the_type_out()
    {
        using var client = Unanswered().AsDecisionClient(new ChatDecisionClientOptions { Model = "m2" });

        Assert.Equal("auto", client.GetService<DecisionClientMetadata>()!.CacheScope);
    }

    [Fact]
    public void Chat_clients_of_different_types_without_metadata_get_different_scopes()
    {
        using var scripted = Unanswered().AsDecisionClient();
        using var wrapped = new WrappingChatClient(Unanswered()).AsDecisionClient();

        Assert.Equal("auto/" + typeof(WrappingChatClient).FullName, wrapped.GetService<DecisionClientMetadata>()!.CacheScope);
        Assert.NotEqual(scripted.GetService<DecisionClientMetadata>()!.CacheScope, wrapped.GetService<DecisionClientMetadata>()!.CacheScope);
    }

    [Fact]
    public async Task A_logprobs_client_does_not_get_the_verbalized_answer_an_auto_client_cached()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        // The same chat client type and metadata, so only the probability mode tells the two clients apart.
        var verbalizing = new ScriptedChatClient((_, _) => ChatCompletions.WithoutLogprobs("""{"A":0.9,"B":0.1}"""), OpenRouter);
        var logprobs = new ScriptedChatClient((_, _) => ChatCompletions.WithTopLogprobs(("B", -0.1f), ("A", -2.5f)), OpenRouter);
        using var auto = new CachingDecisionClient(verbalizing.AsDecisionClient(), memory, distributed);
        using var strict = new CachingDecisionClient(
            logprobs.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs }), memory, distributed);
        var request = new DecisionRequest(Refund, "I want my money back.");

        var stored = await auto.EvaluateAsync(request);
        var again = await auto.EvaluateAsync(request);
        var answered = await strict.EvaluateAsync(request);

        Assert.True(stored.IsSuccess, stored.IsFailure ? stored.Error.Message : null);
        Assert.Equal(ProbabilitySource.Verbalized, stored.Value.ProbabilitySource);
        Assert.True(again.Value.IsCached);
        Assert.True(answered.IsSuccess, answered.IsFailure ? answered.Error.Message : null);
        Assert.False(answered.Value.IsCached);
        Assert.Equal(ProbabilitySource.Logprobs, answered.Value.ProbabilitySource);
        Assert.Single(logprobs.Calls);
    }

    [Fact]
    public async Task Two_clients_over_the_same_chat_client_type_without_metadata_share_entries()
    {
        // The documented limit: the type is all the bridge can tell them apart by, so give a client metadata or a Model.
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var first = new ScriptedChatClient((_, _) => ChatCompletions.WithoutLogprobs("""{"A":0.9,"B":0.1}"""));
        var second = new ScriptedChatClient((_, _) => throw new InvalidOperationException("The chat client was called."));
        using var one = new CachingDecisionClient(first.AsDecisionClient(), memory);
        using var other = new CachingDecisionClient(second.AsDecisionClient(), memory);
        var request = new DecisionRequest(Refund, "I want my money back.");

        await one.EvaluateAsync(request);
        var result = await other.EvaluateAsync(request);

        Assert.True(result.Value.IsCached);
        Assert.Empty(second.Calls);
    }

    private static ScriptedChatClient Unanswered(ChatClientMetadata? metadata = null)
        => new((_, _) => throw new InvalidOperationException("The chat client was called."), metadata);

    // A second chat client type, which forwards the metadata request, and so its lack of metadata, to the inner client.
    private sealed class WrappingChatClient(IChatClient inner) : DelegatingChatClient(inner);
}
