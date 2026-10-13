using System.Text;
using System.Text.Json;

namespace Minos.Tests;

public sealed class DecisionCacheKeyTests
{
    private const string Prefix = "minos:";
    private const string Provider = "typesafe";
    private const string Endpoint = "https://api.typesafe.ai/";

    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();

    private static string Key(DecisionRequest request, string provider = Provider, string? endpoint = Endpoint, string? defaultModel = null)
        => DecisionCacheKey.Create(request, provider, endpoint, defaultModel, Prefix);

    private static DecisionRequest Request(string state = "I was charged twice.") => new(Triage, state);

    [Fact]
    public void Is_the_prefix_and_a_43_character_digest()
    {
        var key = Key(Request());

        Assert.StartsWith(Prefix, key, StringComparison.Ordinal);
        Assert.Equal(Prefix.Length + 43, key.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(1000)]
    public void Ends_with_the_same_digest_whatever_the_prefix_length(int length)
    {
        // Up to 128 characters the key is built on the stack, and beyond that in a pooled buffer.
        var prefix = new string('p', length);

        var key = DecisionCacheKey.Create(Request(), Provider, Endpoint, null, prefix);

        Assert.StartsWith(prefix, key, StringComparison.Ordinal);
        Assert.Equal(Key(Request())[Prefix.Length..], key[length..]);
    }

    [Fact]
    public void Is_the_same_for_equal_requests_and_ignores_the_retry_attempt()
    {
        var first = Key(Request());

        Assert.Equal(first, Key(Request()));
        Assert.Equal(first, Key(Request() with { RetryAttempt = 2 }));
        Assert.Equal(first, Key(new DecisionRequest(QuestionSets.TriageDefinition(), "I was charged twice.")));
    }

    [Fact]
    public void Changes_with_the_state()
        => Assert.NotEqual(Key(Request()), Key(Request("I was charged once.")));

    [Fact]
    public void Tells_text_state_from_json_state()
    {
        var text = new DecisionRequest(Triage, DecisionContent.FromString("{}"));
        var json = new DecisionRequest(Triage, DecisionContent.FromJson(JsonDocument.Parse("{}").RootElement));

        Assert.NotEqual(Key(text), Key(json));
    }

    [Fact]
    public void Changes_with_the_model()
    {
        var none = Key(Request());
        var one = Key(Request() with { Model = "jev-latest" });
        var other = Key(Request() with { Model = "jev-1.13.0" });

        Assert.NotEqual(none, one);
        Assert.NotEqual(one, other);
    }

    [Fact]
    public void Keys_the_default_model_when_the_request_names_none()
    {
        var latest = Key(Request(), defaultModel: "jev-latest");

        Assert.NotEqual(Key(Request()), latest);
        Assert.NotEqual(latest, Key(Request(), defaultModel: "jev-1.13.0"));
        Assert.Equal(latest, Key(Request() with { Model = "jev-latest" }));
        Assert.Equal(latest, Key(Request() with { Model = "jev-latest" }, defaultModel: "jev-1.13.0"));
    }

    [Fact]
    public void Changes_with_the_images()
    {
        var png = DecisionImage.FromBytes(new byte[] { 1, 2, 3 }, "image/png");
        var otherBytes = DecisionImage.FromBytes(new byte[] { 1, 2, 4 }, "image/png");
        var otherType = DecisionImage.FromBytes(new byte[] { 1, 2, 3 }, "image/jpeg");

        var none = Key(Request());
        var one = Key(Request() with { Images = [png] });

        Assert.NotEqual(none, one);
        Assert.NotEqual(one, Key(Request() with { Images = [otherBytes] }));
        Assert.NotEqual(one, Key(Request() with { Images = [otherType] }));
        Assert.NotEqual(one, Key(Request() with { Images = [png, png] }));
        Assert.NotEqual(Key(Request() with { Images = [png, otherBytes] }), Key(Request() with { Images = [otherBytes, png] }));
    }

    [Fact]
    public void Changes_with_the_provider_and_the_endpoint()
    {
        var key = Key(Request());

        Assert.NotEqual(key, Key(Request(), provider: "openrouter"));
        Assert.NotEqual(key, Key(Request(), endpoint: "https://openrouter.ai/api/"));
        Assert.NotEqual(key, Key(Request(), endpoint: null));
    }

    [Fact]
    public void Changes_with_any_part_of_the_definition()
    {
        var baseKey = Key(Request());
        QuestionSetDefinition[] variants =
        [
            new(QuestionDefinition.Noul("is_urgent", "Is this urgent?")),
            new(QuestionDefinition.Noul("is_urgent", "Is this urgent now?")),
            new(QuestionDefinition.Noul("urgent", "Is this urgent?")),
            new(QuestionDefinition.Noul("is_urgent", "Is this urgent?", whenTrue: "It must be handled today.")),
            new(QuestionDefinition.Noul("is_urgent", "Is this urgent?", whenFalse: "It can wait.")),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"))),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Invoices"))),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", Criterion.Text("Payments").WithExamples("refund")))),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", Criterion.Text("Payments").WithNotFor("refund")))),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", null))),
            new(QuestionDefinition.Score("severity", "How severe?", "Low", "High")),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", Criterion.Json(DecisionContent.FromJson(JsonDocument.Parse("""{"area":"payments"}""").RootElement))))),
            new(QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", Criterion.Json(DecisionContent.FromJson(JsonDocument.Parse("""{"area":"invoices"}""").RootElement))))),
            // Same key, instructions and option keys and criteria as the Score above; only the kind differs.
            new(QuestionDefinition.Choice("severity", "How severe?", new OptionDefinition("0", "Low"), new OptionDefinition("1", "High"))),
        ];

        var keys = variants.Select(definition => Key(new DecisionRequest(definition, "I was charged twice."))).Append(baseKey).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Separates_adjacent_fields()
    {
        // Without length prefixes these two would hash the same bytes: key "ab" + instructions "c" against "a" + "bc".
        var first = new QuestionSetDefinition(QuestionDefinition.Noul("ab", "c"));
        var second = new QuestionSetDefinition(QuestionDefinition.Noul("a", "bc"));

        Assert.NotEqual(Key(new DecisionRequest(first, "s")), Key(new DecisionRequest(second, "s")));
    }

    [Fact]
    public void Hashes_a_large_state()
    {
        var state = new string('x', 100_000);

        Assert.NotEqual(Key(Request(state)), Key(Request(state + "y")));
    }

    [Fact]
    public void Hashes_a_utf8_state_by_its_bytes()
    {
        var first = new DecisionRequest(Triage, DecisionContent.FromCheckedUtf8State("""{"a":1}"""u8));
        var second = new DecisionRequest(Triage, DecisionContent.FromCheckedUtf8State("""{"a":2}"""u8));

        Assert.NotEqual(Key(first), Key(second));
    }
}
