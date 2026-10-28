using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Minos.Caching.Tests;

public sealed class CachedDecisionFormatTests
{
    private static byte[] Write(CachedDecision entry) => CachedDecisionFormat.Write(entry);

    private static CachedDecision Read(byte[] bytes) => CachedDecisionFormat.Read(bytes);

    private static CachedDecision Stored() => CachedDecision.From(Responses.Answer().AsCached());

    // A valid Triage entry with one property replaced by the JSON in value, or removed when value is null, so that
    // property is the only thing wrong with it.
    private static byte[] StoredWith(string property, string? value)
    {
        var json = JsonNode.Parse(Write(Stored()))!.AsObject();
        if (value is null)
        {
            json.Remove(property);
        }
        else
        {
            json[property] = JsonNode.Parse(value);
        }

        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    private static CachedDecision Entry(AnswerSlot slot1)
    {
        var stored = Stored();
        AnswerSlot[] slots = [stored.Slots[0], slot1, stored.Slots[2]];
        return new CachedDecision(slots, stored.Probabilities, stored.Model, stored.InputTokens, stored.OutputTokens, stored.Cost, stored.Id, stored.UpstreamProvider);
    }

    [Fact]
    public void Writes_compact_json_with_version_1()
    {
        using var document = JsonDocument.Parse(Write(Stored()));

        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
        Assert.Equal("jev-1.13.0", document.RootElement.GetProperty("model").GetString());
        Assert.DoesNotContain("\n", Encoding.UTF8.GetString(Write(Stored())), StringComparison.Ordinal);
    }

    [Fact]
    public void Round_trips_every_field()
    {
        var original = Responses.Answer();

        var read = Read(Write(CachedDecision.From(original.AsCached()))).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.True(read.IsCached);
        Assert.Equal(original.Model, read.Model);
        Assert.Equal("gen-0001", read.Id);
        Assert.Equal("Together", read.UpstreamProvider);
        Assert.Equal(original.Usage, read.Usage);
        Assert.Equal(original.Slots, read.Slots);
        Assert.Equal(original.Probabilities, read.Probabilities);
    }

    [Fact]
    public void The_valid_entry_the_altered_cases_start_from_reads()
    {
        Assert.NotNull(Read(StoredWith("model", "\"jev-1.13.0\"")).ResponseFor(Responses.Triage));
    }

    // Slots the way a response with marked answers holds them: a Logprobs choice and a Verbalized score.
    private static CachedDecision Marked()
    {
        var stored = Stored();
        AnswerSlot[] slots =
        [
            stored.Slots[0],
            stored.Slots[1] with { Source = ProbabilitySource.Logprobs },
            stored.Slots[2] with { Source = ProbabilitySource.Verbalized },
        ];
        return new CachedDecision(slots, stored.Probabilities, stored.Model, stored.InputTokens, stored.OutputTokens, stored.Cost, stored.Id, stored.UpstreamProvider);
    }

    private static DecisionResponse MarkedResponse()
    {
        var marked = Marked();
        return new DecisionResponse(
            Responses.Triage, marked.Slots, marked.Probabilities, marked.Model, marked.InputTokens, marked.OutputTokens, marked.Cost, marked.Id, marked.UpstreamProvider);
    }

    private static byte[] MarkedWith(string property, string? value)
    {
        var json = JsonNode.Parse(Write(Marked()))!.AsObject();
        if (value is null)
        {
            json.Remove(property);
        }
        else
        {
            json[property] = JsonNode.Parse(value);
        }

        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    [Fact]
    public void ModelOnly_IsWrittenAsBefore()
    {
        var text = Encoding.UTF8.GetString(Write(Stored()));
        using var document = JsonDocument.Parse(text);

        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
        Assert.DoesNotContain("sources", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkedAnswers_RoundTrip_AsVersion2()
    {
        var bytes = Write(CachedDecision.From(MarkedResponse().AsCached()));
        using var document = JsonDocument.Parse(bytes);

        Assert.Equal(2, document.RootElement.GetProperty("v").GetInt32());
        Assert.Equal("[0,1,2]", document.RootElement.GetProperty("sources").GetRawText());

        var read = Read(bytes).ResponseFor(Responses.Triage);

        Assert.NotNull(read);
        Assert.True(read.IsCached);
        Assert.Equal(
            [ProbabilitySource.Model, ProbabilitySource.Logprobs, ProbabilitySource.Verbalized],
            read.Slots.Select(slot => slot.Source));
    }

    [Fact]
    public void Version1_WithoutSources_ReadsAsModel()
    {
        const string v1 = """{"v":1,"model":"jev-1.13.0","slots":[[0,1,0.9,0],[1,1,0.7,0],[2,1.9,0.8,0]],"probabilities":[0.2,0.7,0.1,0,0.1,0.9]}""";

        var entry = Read(Encoding.UTF8.GetBytes(v1));

        Assert.NotSame(CachedDecision.Unreadable, entry);
        Assert.All(entry.Slots, slot => Assert.Equal(ProbabilitySource.Model, slot.Source));
    }

    [Theory]
    [InlineData("[0,1]")]
    [InlineData("[0,1,2,0]")]
    [InlineData("[0,1,3]")]
    [InlineData("[0,1,-1]")]
    [InlineData("[0,\"x\",2]")]
    [InlineData("\"x\"")]
    public void Malformed_Sources_IsUnreadable(string sources)
    {
        Assert.Same(CachedDecision.Unreadable, Read(MarkedWith("sources", sources)));
    }

    [Fact]
    public void Sources_OnAVersion1Entry_IsUnreadable()
    {
        Assert.Same(CachedDecision.Unreadable, Read(MarkedWith("v", "1")));
    }

    [Fact]
    public void Version2_WithoutSources_IsUnreadable()
    {
        Assert.Same(CachedDecision.Unreadable, Read(MarkedWith("sources", null)));
    }

    [Theory]
    [InlineData("v", "3")]
    [InlineData("v", null)]
    [InlineData("v", "null")]
    [InlineData("model", "5")]
    [InlineData("cost", "\"x\"")]
    [InlineData("slots", "\"x\"")]
    [InlineData("slots", "[[0,0.9]]")]
    [InlineData("probabilities", "[\"x\"]")]
    public void An_unknown_version_or_a_wrong_field_in_an_otherwise_valid_entry_reads_as_unreadable(string property, string? value)
    {
        var entry = Read(StoredWith(property, value));

        Assert.Same(CachedDecision.Unreadable, entry);
        Assert.Null(entry.ResponseFor(Responses.Triage));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"v":1,"slots":[],"probabilities":[]""")]
    public void Bytes_that_are_no_entry_read_as_unreadable(string json)
    {
        Assert.Same(CachedDecision.Unreadable, Read(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Invalid_utf8_in_a_string_reads_as_unreadable()
    {
        var bytes = Write(Stored());
        var at = bytes.AsSpan().IndexOf("jev-1.13.0"u8);
        Assert.True(at > 0);
        bytes[at] = 0xFF; // Never valid in UTF-8.

        Assert.Same(CachedDecision.Unreadable, Read(bytes));
    }

    [Fact]
    public void An_entry_that_does_not_fit_the_definition_reads_as_a_miss()
    {
        var other = new QuestionSetDefinition(QuestionDefinition.Noul("is_urgent", "Is this urgent?"));

        Assert.Null(Read(Write(Stored())).ResponseFor(other));
    }

    [Fact]
    public void An_entry_built_from_the_stored_slots_fits()
    {
        Assert.NotNull(Entry(Stored().Slots[1]).ResponseFor(Responses.Triage));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    public void A_slot_whose_option_or_offset_does_not_fit_its_question_is_a_miss(int valueIndex, int offsetShift)
    {
        var choice = Stored().Slots[1];
        var entry = Entry(choice with { ValueIndex = valueIndex, Offset = choice.Offset + offsetShift });

        Assert.Null(entry.ResponseFor(Responses.Triage));
    }
}
