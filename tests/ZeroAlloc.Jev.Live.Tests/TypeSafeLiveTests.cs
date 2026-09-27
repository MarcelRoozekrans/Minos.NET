using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace ZeroAlloc.Jev.Live.Tests;

public sealed class TypeSafeLiveTests
{
    // Reflection-based (not source-generated): see the comment on GeneratedQuestionSet_ParsesTypedAnswers.
    private static readonly JsonSerializerOptions QuestionsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITestOutputHelper _output;

    public TypeSafeLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact(JevProvider.TypeSafe)]
    public async Task Evaluate_AnswersEveryQuestion()
    {
        using var client = Live.Client(JevProvider.TypeSafe);

        var result = await client.EvaluateAsync(Live.Request(JevProvider.TypeSafe));

        Assert.True(result.IsSuccess);
        Live.AssertAnsweredEveryQuestion(result.Value);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task ListModels_IncludesTheDefaultModel()
    {
        using var client = Live.Client(JevProvider.TypeSafe);

        var result = await client.ListModelsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Models);
        _output.WriteLine("Models: " + string.Join(", ", result.Value.Models.Select(model => model.Name)));
        Assert.Contains(result.Value.Models, model => string.Equals(model.Name, JevDefaults.Model, StringComparison.Ordinal));
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task GeneratedQuestionSet_ParsesTypedAnswers()
    {
        // JevQuestion.Instructions is a JevContent, whose [JsonConverter] names the library's internal
        // JevContentConverter. A source-generated context cannot compile a reference to it from this assembly
        // without InternalsVisibleTo (which this project must not add), so this one deserialization uses plain
        // reflection-based JsonSerializerOptions instead: reflection resolves and constructs the converter at run
        // time, which is unaffected by the compile-time accessibility check that source generation fails on.
        var questions = JsonSerializer.Deserialize<Dictionary<string, JevQuestion>>(
            LiveTriage.QuestionsUtf8,
            QuestionsJsonOptions)!;

        using var client = Live.Client(JevProvider.TypeSafe);
        var request = new SystemOneRequest
        {
            State = "Help! My payouts have been failing for 3 days and nobody answers.",
            Model = Live.Model(JevProvider.TypeSafe),
            Questions = questions,
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsSuccess);

        var answersJson = JsonSerializer.SerializeToUtf8Bytes(
            result.Value.Answers,
            LiveJsonContext.Default.IReadOnlyDictionaryStringJevAnswer);

        var reader = new Utf8JsonReader(answersJson);
        reader.Read();
        var triage = LiveTriage.Parse(ref reader);

        Assert.InRange(triage.IsUrgent.Probability, 0.0, 1.0);
        Assert.InRange(triage.Team.Confidence, 0.0, 1.0);
        Assert.True(Enum.IsDefined(triage.Team.Value));
        Assert.InRange(triage.Frustration.Confidence, 0.0, 1.0);
        Assert.True(Enum.IsDefined(triage.Frustration.Value));
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task BadKey_IsUnauthorized()
    {
        using var client = new JevClient(new JevClientOptions { ApiKey = "invalid-key-for-live-test", MaxRetries = 0 });

        var result = await client.EvaluateAsync(Live.Request(JevProvider.TypeSafe));

        Assert.True(result.IsFailure);
        Live.LogError(_output, result.Error);
        Assert.Equal(JevErrorKind.Unauthorized, result.Error.Kind);
    }

    [LiveFact(JevProvider.TypeSafe)]
    public async Task InvalidRequest_IsValidation()
    {
        using var client = new JevClient(new JevClientOptions { MaxRetries = 0 });
        var request = new SystemOneRequest
        {
            State = "Help! My payouts have been failing for 3 days and nobody answers.",
            Model = Live.Model(JevProvider.TypeSafe),
            Questions = new Dictionary<string, JevQuestion>(),
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsFailure);
        Live.LogError(_output, result.Error);
        Assert.Equal(JevErrorKind.Validation, result.Error.Kind);
    }
}

// Phase 2.1's typed EvaluateAsync<T> will replace this manual round trip. The library's own JevJsonContext is
// internal, so this test-local context mirrors its options exactly to serialize the public JevAnswer wire type
// (which carries its own JsonPolymorphic attributes) without InternalsVisibleTo. It cannot also cover
// Dictionary<string, JevQuestion>: JevQuestion.Instructions is a JevContent, whose [JsonConverter] names the
// library's internal JevContentConverter, and source generation must emit a direct, compile-time reference to a
// converter type, which fails to compile here (SYSLIB1220/SYSLIB1030) without InternalsVisibleTo. That
// deserialization uses reflection-based JsonSerializerOptions instead; see QuestionsJsonOptions above.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(IReadOnlyDictionary<string, JevAnswer>))]
internal sealed partial class LiveJsonContext : JsonSerializerContext;
