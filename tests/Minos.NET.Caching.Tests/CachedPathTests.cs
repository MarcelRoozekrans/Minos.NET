using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZeroAlloc.Results;

namespace Minos.Caching.Tests;

public sealed class CachedPathTests
{
    private static readonly DecisionClientMetadata TypeSafe = new("typesafe", new Uri("https://api.typesafe.ai/"), "jev-latest");

    // The bytes today's writer, the 1.9.0 one, gave for Responses.Answer().AsCached(): version 1, no path.
    private static ReadOnlySpan<byte> Entry190 =>
        """{"v":1,"model":"jev-1.13.0","input_tokens":296,"output_tokens":20,"cost":0.0012,"id":"gen-0001","provider":"Together","slots":[[0,0.9,0,0],[1,0,0.7,0],[2,1.9,0.8,3]],"probabilities":[0.2,0.7,0.1,0,0.1,0.9]}"""u8;

    private static DecisionPath MergedPath() => new(
        [
            new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 0.9, null),
            new QuestionRoute("department", "big", RouteReason.Escalated, 0.4, 0.8),
            new QuestionRoute("severity", "primary", RouteReason.TargetFailed, 0.55, 0.3),
        ],
        [
            new DecisionCall("primary", "small-1", new DecisionUsage { InputTokens = 10, OutputTokens = 2, Cost = 0.001 }, false),
            new DecisionCall("big", null, null, true),
            new DecisionCall("third", "m", new DecisionUsage { InputTokens = 5 }, false),
        ]);

    private static DecisionResponse Merged()
    {
        var plain = Responses.Answer();
        return new DecisionResponse(
            Responses.Triage, plain.Slots, plain.Probabilities, plain.Model, plain.InputTokens, plain.OutputTokens, plain.Cost, plain.Id, plain.UpstreamProvider)
        {
            Path = MergedPath(),
        };
    }

    private static void AssertPath(DecisionPath expected, DecisionPath? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Routes, actual.Routes);
        Assert.Equal(expected.Calls.Count, actual.Calls.Count);
        for (var i = 0; i < expected.Calls.Count; i++)
        {
            Assert.Equal(expected.Calls[i], actual.Calls[i]);
        }
    }

    [Fact]
    public void A_merged_response_round_trips_with_its_path()
    {
        var entry = CachedDecision.From(Merged().AsCached());

        var read = CachedDecisionFormat.Read(CachedDecisionFormat.Write(entry)).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.True(read.IsCached);
        AssertPath(MergedPath(), read.Path);
        Assert.Equal(Merged().Slots, read.Slots);
    }

    [Fact]
    public async Task A_merged_response_round_trips_through_the_memory_and_the_distributed_cache()
    {
        var distributed = new CountingDistributedCache();
        var inner = new ScriptedClient(TypeSafe).Then(Result<DecisionResponse, DecisionError>.Success(Merged()));
        using var firstMemory = Responses.Memory();
        using var first = new CachingDecisionClient(inner, firstMemory, distributed);
        var miss = await first.EvaluateAsync(Responses.Request());

        var memoryHit = await first.EvaluateAsync(Responses.Request());
        using var secondMemory = Responses.Memory();
        using var second = new CachingDecisionClient(new ScriptedClient(TypeSafe), secondMemory, distributed);
        var distributedHit = await second.EvaluateAsync(Responses.Request());

        AssertPath(MergedPath(), miss.Value.Path);
        Assert.True(memoryHit.Value.IsCached);
        AssertPath(MergedPath(), memoryHit.Value.Path);
        Assert.True(distributedHit.Value.IsCached);
        AssertPath(MergedPath(), distributedHit.Value.Path);
    }

    [Fact]
    public void An_entry_without_a_path_reads_as_Path_null()
    {
        var entry = CachedDecision.From(Responses.Answer().AsCached());

        var bytes = CachedDecisionFormat.Write(entry);
        var read = CachedDecisionFormat.Read(bytes).ResponseFor(Responses.Triage);

        using var document = JsonDocument.Parse(bytes);
        Assert.False(document.RootElement.TryGetProperty("path", out _));
        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
        Assert.NotNull(read);
        Assert.Null(read.Path);
    }

    [Fact]
    public void An_entry_written_by_1_9_0_still_reads()
    {
        var read = CachedDecisionFormat.Read(Entry190).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.Null(read.Path);
        Assert.Equal("jev-1.13.0", read.Model);
        Assert.Equal(Responses.Answer().Slots, read.Slots);
    }

    [Fact]
    public void A_path_with_the_wrong_route_count_is_a_miss()
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["path"]!["routes"]!.AsArray().RemoveAt(2);

        var read = CachedDecisionFormat.Read(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Null(read.ResponseFor(Responses.Triage));
    }

    [Theory]
    [InlineData("""[["a",9,0.5,null]]""")]
    [InlineData("""[["a",0,1.5,null]]""")]
    [InlineData("""[["a",0,0.5,-1]]""")]
    [InlineData("""[["a",0,0.5]]""")]
    [InlineData("""[[1,0,0.5,null]]""")]
    public void A_route_the_reader_cannot_build_makes_the_entry_unreadable(string routes)
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["path"]!["routes"] = JsonNode.Parse(routes);

        var read = CachedDecisionFormat.Read(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Same(CachedDecision.Unreadable, read);
    }

    [Fact]
    public void A_path_in_an_entry_below_version_3_is_unreadable()
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["v"] = 1;

        Assert.Same(CachedDecision.Unreadable, CachedDecisionFormat.Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Fact]
    public void A_path_with_no_calls_makes_the_entry_unreadable()
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["path"]!["calls"] = new JsonArray();

        Assert.Same(CachedDecision.Unreadable, CachedDecisionFormat.Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Fact]
    public void A_hit_of_a_merged_response_keeps_calls_with_their_stored_IsCached()
    {
        var read = CachedDecisionFormat.Read(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached()))).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.True(read.IsCached);
        Assert.Equal([false, true, false], read.Path!.Calls.Select(call => call.IsCached));
    }

    [Fact]
    public void A_path_and_marked_sources_are_both_kept()
    {
        var plain = Responses.Answer();
        AnswerSlot[] slots = [.. plain.Slots];
        slots[1] = slots[1] with { Source = ProbabilitySource.Logprobs };
        var marked = new DecisionResponse(Responses.Triage, slots, plain.Probabilities, plain.Model, plain.InputTokens, plain.OutputTokens, plain.Cost, null, null)
        {
            Path = MergedPath(),
        };

        var read = CachedDecisionFormat.Read(CachedDecisionFormat.Write(CachedDecision.From(marked.AsCached()))).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.Equal(ProbabilitySource.Logprobs, read.Slots[1].Source);
        AssertPath(MergedPath(), read.Path);
    }
}
