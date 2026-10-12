using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Minos.Caching.Tests;

public sealed class CachedDecisionSerializerTests
{
    private static readonly CachedDecisionSerializer Serializer = new();

    private static byte[] Write(CachedDecision entry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serializer.Serialize(entry, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    private static CachedDecision Read(byte[] bytes) => Serializer.Deserialize(new ReadOnlySequence<byte>(bytes));

    private static CachedDecision Stored() => CachedDecision.From(Responses.Answer().AsCached());

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
        Assert.Equal(original.Id, read.Id);
        Assert.Equal(original.Provider, read.Provider);
        Assert.Equal(original.Usage, read.Usage);
        Assert.Equal(original.Slots, read.Slots);
        Assert.Equal(original.Probabilities, read.Probabilities);
    }

    [Theory]
    [InlineData("""{"v":2,"slots":[],"probabilities":[]}""")]
    [InlineData("""{"slots":[],"probabilities":[]}""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"v":1,"slots":[[0,0.9]],"probabilities":[]}""")]
    [InlineData("""{"v":1,"slots":"x","probabilities":[]}""")]
    public void An_unknown_version_or_a_corrupt_entry_reads_as_unreadable(string json)
    {
        var entry = Read(Encoding.UTF8.GetBytes(json));

        Assert.Null(entry.ResponseFor(Responses.Triage, out _));
    }

    [Fact]
    public void An_entry_that_does_not_fit_the_definition_reads_as_a_miss()
    {
        var other = new QuestionSetDefinition(QuestionDefinition.Noul("is_urgent", "Is this urgent?"));

        Assert.Null(Read(Write(Stored())).ResponseFor(other, out _));
    }
}
