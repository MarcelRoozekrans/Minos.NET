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

    // The bytes today's writer gave for a response with a path, before calls kept an outcome: version 3, calls of six items.
    private static ReadOnlySpan<byte> PathEntryBeforeOutcomes =>
        """{"v":3,"model":"jev-1.13.0","input_tokens":296,"output_tokens":20,"cost":0.0012,"id":"gen-0001","provider":"Together","slots":[[0,0.9,0,0],[1,0,0.7,0],[2,1.9,0.8,3]],"probabilities":[0.2,0.7,0.1,0,0.1,0.9],"path":{"routes":[["primary",0,0.9,null],["big",1,0.4,0.8],["primary",3,0.55,0.3]],"calls":[["primary","small-1",10,2,0.001,false],["big",null,null,null,null,true]]}}"""u8;

    private static DecisionPath FallbackPath() => new(
        [
            new QuestionRoute("is_urgent", "backup", RouteReason.FellBack, 0, null),
            new QuestionRoute("department", "backup", RouteReason.FellBack, 0, null),
            new QuestionRoute("severity", "backup", RouteReason.FellBack, 0, null),
        ],
        [
            new DecisionCall("primary", "small-1", null, false) { Outcome = CallOutcome.Failed, ErrorKind = DecisionErrorKind.Timeout },
            new DecisionCall("second", null, null, false) { Outcome = CallOutcome.Failed },
            new DecisionCall("third", null, null, false) { Outcome = CallOutcome.NotCalledNoTimeLeft },
            new DecisionCall("backup", "m", new DecisionUsage { InputTokens = 5 }, false),
        ]);

    private static DecisionResponse Fallback()
    {
        var plain = Responses.Answer();
        return new DecisionResponse(
            Responses.Triage, plain.Slots, plain.Probabilities, plain.Model, plain.InputTokens, plain.OutputTokens, plain.Cost, plain.Id, plain.UpstreamProvider)
        {
            Path = FallbackPath(),
        };
    }

    [Fact]
    public async Task A_fallback_response_round_trips_with_outcomes_and_FellBack_routes()
    {
        var read = CachedDecisionFormat.Read(CachedDecisionFormat.Write(CachedDecision.From(Fallback().AsCached()))).ResponseFor(Responses.Triage);
        Assert.NotNull(read);
        AssertPath(FallbackPath(), read.Path);

        var distributed = new CountingDistributedCache();
        var inner = new ScriptedClient(TypeSafe).Then(Result<DecisionResponse, DecisionError>.Success(Fallback()));
        using var firstMemory = Responses.Memory();
        using var first = new CachingDecisionClient(inner, firstMemory, distributed);
        await first.EvaluateAsync(Responses.Request());
        var memoryHit = await first.EvaluateAsync(Responses.Request());
        using var secondMemory = Responses.Memory();
        using var second = new CachingDecisionClient(new ScriptedClient(TypeSafe), secondMemory, distributed);
        var distributedHit = await second.EvaluateAsync(Responses.Request());

        Assert.True(memoryHit.Value.IsCached);
        AssertPath(FallbackPath(), memoryHit.Value.Path);
        Assert.True(distributedHit.Value.IsCached);
        AssertPath(FallbackPath(), distributedHit.Value.Path);
    }

    [Fact]
    public void An_Answered_call_writes_no_outcome_so_the_entry_keeps_its_old_shape()
    {
        var bytes = CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached()));

        using var document = JsonDocument.Parse(bytes);
        foreach (var call in document.RootElement.GetProperty("path").GetProperty("calls").EnumerateArray())
        {
            Assert.Equal(6, call.GetArrayLength());
        }
    }

    [Fact]
    public void An_entry_written_before_this_phase_reads_calls_as_Answered()
    {
        var read = CachedDecisionFormat.Read(PathEntryBeforeOutcomes).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.Equal(2, read.Path!.Calls.Count);
        Assert.All(read.Path.Calls, call =>
        {
            Assert.Equal(CallOutcome.Answered, call.Outcome);
            Assert.Null(call.ErrorKind);
        });
        Assert.True(read.Path.Calls[1].IsCached);
    }

    [Theory]
    [InlineData("""["a",null,null,null,null,false,3]""")]
    [InlineData("""["a",null,null,null,null,false,-1]""")]
    [InlineData("""["a",null,null,null,null,false,1,99]""")]
    [InlineData("""["a",null,null,null,null,false,1,-1]""")]
    [InlineData("""["a",null,null,null,null,false,"1"]""")]
    [InlineData("""["a",null,null,null,null,false,1,"x"]""")]
    [InlineData("""["a",null,null,null,null,false,1,1,1]""")]
    public void An_unknown_outcome_is_a_miss(string call)
    {
        var read = CachedDecisionFormat.Read(WithRawPath($$"""{"routes":[],"calls":[{{call}}]}"""));

        Assert.Same(CachedDecision.Unreadable, read);
        Assert.Null(read.ResponseFor(Responses.Triage));
    }

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

    // The record reader rejects an empty client name too, so the two readers agree.
    [Theory]
    [InlineData("routes")]
    [InlineData("calls")]
    public void An_empty_client_name_makes_the_entry_unreadable(string list)
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["path"]![list]![0]![0] = string.Empty;

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

    private static byte[] WithPath(string pathJson)
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["path"] = JsonNode.Parse(pathJson);
        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    private static byte[] WithRawPath(string pathJson)
    {
        var text = Encoding.UTF8.GetString(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())));
        var start = text.IndexOf("\"path\":", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(string.Concat(text.AsSpan(0, start), "\"path\":", pathJson, "}"));
    }

    [Theory]
    [InlineData("""{"routes":[],"calls":[["a",null,null,null,null]]}""")]
    [InlineData("""{"routes":[],"calls":[["a",null,null,null,null,"yes"]]}""")]
    [InlineData("""{"routes":[],"calls":[[null,null,null,null,null,false]]}""")]
    [InlineData("""{"routes":[],"calls":[["a",null,-1,null,null,false]]}""")]
    [InlineData("""{"routes":[],"calls":[["a",null,null,-2,null,false]]}""")]
    [InlineData("""{"routes":[],"calls":[["a",null,null,null,-0.5,false]]}""")]
    [InlineData("""{"routes":[["a",-1,0.5,null]],"calls":[["a",null,null,null,null,false]]}""")]
    [InlineData("""{"routes":[["a",5,0.5,null]],"calls":[["a",null,null,null,null,false]]}""")]
    [InlineData("null")]
    public void A_malformed_stored_path_is_unreadable_and_never_throws(string pathJson)
    {
        var read = CachedDecisionFormat.Read(WithPath(pathJson));

        Assert.Same(CachedDecision.Unreadable, read);
        Assert.Null(read.ResponseFor(Responses.Triage));
    }

    [Fact]
    public void A_certainty_beyond_double_range_is_unreadable()
    {
        var read = CachedDecisionFormat.Read(WithRawPath("""{"routes":[["a",0,1e999,null]],"calls":[["a",null,null,null,null,false]]}"""));

        Assert.Same(CachedDecision.Unreadable, read);
    }

    [Fact]
    public void A_version_3_entry_with_the_wrong_number_of_sources_is_unreadable()
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["sources"] = JsonNode.Parse("[0]");

        Assert.Same(CachedDecision.Unreadable, CachedDecisionFormat.Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Fact]
    public void An_unknown_property_inside_the_path_is_skipped()
    {
        var json = JsonNode.Parse(CachedDecisionFormat.Write(CachedDecision.From(Merged().AsCached())))!.AsObject();
        json["path"]!["later"] = JsonNode.Parse("""{"x":[1,2]}""");

        var read = CachedDecisionFormat.Read(Encoding.UTF8.GetBytes(json.ToJsonString())).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        AssertPath(MergedPath(), read.Path);
    }
}
