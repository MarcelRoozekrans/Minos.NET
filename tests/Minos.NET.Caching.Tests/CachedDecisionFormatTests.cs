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
        return new CachedDecision(slots, stored.Probabilities, stored.Model, stored.InputTokens, stored.OutputTokens, stored.Cost, stored.Id, stored.Provider);
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

        var read = Read(Write(CachedDecision.From(original.AsCached()))).ResponseFor(Responses.Triage, out _);

        Assert.NotNull(read);
        Assert.True(read.IsCached);
        Assert.Equal(original.Model, read.Model);
        Assert.Equal("gen-0001", read.Id);
        Assert.Equal("Together", read.Provider);
        Assert.Equal(original.Usage, read.Usage);
        Assert.Equal(original.Slots, read.Slots);
        Assert.Equal(original.Probabilities, read.Probabilities);
    }

    [Fact]
    public void The_valid_entry_the_altered_cases_start_from_reads()
    {
        Assert.NotNull(Read(StoredWith("model", "\"jev-1.13.0\"")).ResponseFor(Responses.Triage, out _));
    }

    [Theory]
    [InlineData("v", "2")]
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
        Assert.Null(entry.ResponseFor(Responses.Triage, out _));
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

        Assert.Null(Read(Write(Stored())).ResponseFor(other, out _));
    }

    [Fact]
    public void An_entry_built_from_the_stored_slots_fits()
    {
        Assert.NotNull(Entry(Stored().Slots[1]).ResponseFor(Responses.Triage, out _));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    public void A_slot_whose_option_or_offset_does_not_fit_its_question_is_a_miss(int valueIndex, int offsetShift)
    {
        var choice = Stored().Slots[1];
        var entry = Entry(choice with { ValueIndex = valueIndex, Offset = choice.Offset + offsetShift });

        Assert.Null(entry.ResponseFor(Responses.Triage, out _));
    }
}
