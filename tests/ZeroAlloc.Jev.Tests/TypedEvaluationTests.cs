using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.Tests;

/// <summary>Covers the internal <see cref="TypedEvaluation"/> parsing helpers the typed paths share.</summary>
public sealed class TypedEvaluationTests
{
    [Theory]
    [InlineData("response-noul.json")]
    [InlineData("response-type-last.json")]
    [InlineData("response-openrouter.json")]
    public void ParseResponse_FindsTheAnswers(string fixture)
    {
        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(Fixture.Text(fixture)));

        Assert.True(result.IsSuccess);
        Assert.Equal(Answers.Parse<UrgencyCheck>(Fixture.Text(fixture)), result.Value);
    }

    [Fact]
    public void ParseResponse_AnswersFirst_IsFound()
    {
        var json = """{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":"jev-1.13.0","usage":{"input_tokens":1,"output_tokens":1}}""";

        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.Equal(0.3, result.Value.IsUrgent.Probability);
    }

    [Fact]
    public void ParseResponse_NestedAnswersBeforeTheTopLevelOnes_IsSkipped()
    {
        var json = """{"meta":{"answers":{}},"answers":{"is_urgent":{"type":"noul","noul":0.3}},"usage":{"answers":null}}""";

        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsSuccess);
        Assert.Equal(0.3, result.Value.IsUrgent.Probability);
    }

    [Theory]
    [InlineData("""{"model":"jev-1.13.0"}""", "no answers")]
    [InlineData("""{"answers":null}""", "null")]
    [InlineData("""[1]""", "not a JSON object")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"answers":{}}""", "more than one")]
    public void ParseResponse_UnusableBody_IsInvalidResponse(string json, string messagePart)
    {
        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json), statusCode: 200);

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.Contains(messagePart, result.Error.Message, StringComparison.Ordinal);
        Assert.Null(result.Error.Exception);
    }

    [Theory]
    [InlineData("""{"answers":{"department":{"type":"choice","choice":"billing","probabilities":{},"confidence":1}}}""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"usage":""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}}} trailing""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":tru}""")]
    [InlineData("")]
    public void ParseResponse_RejectedOrMalformed_KeepsTheJsonException(string json)
    {
        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Null(result.Error.StatusCode);
        Assert.IsAssignableFrom<JsonException>(result.Error.Exception);
    }

    [Fact]
    public void ParseAnswersObject_ParsesAnAnswersObject()
    {
        var result = TypedEvaluation.ParseAnswersObject<UrgencyCheck>("""{"is_urgent":{"type":"noul","noul":0.7}}"""u8);

        Assert.Equal(0.7, result.Value.IsUrgent.Probability);
    }

    [Fact]
    public void ParseAnswersObject_Rejected_IsInvalidResponse()
    {
        var result = TypedEvaluation.ParseAnswersObject<UrgencyCheck>("{}"u8, statusCode: 200);

        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsType<JsonException>(result.Error.Exception);
    }

    [Fact]
    public void FromResponse_ParsesConsecutiveResponses()
    {
        var noul = JsonSerializer.Deserialize(Fixture.Text("response-noul.json"), Serialization.JevJsonContext.Default.SystemOneResponse)!;
        var choice = JsonSerializer.Deserialize(Fixture.Text("response-choice.json"), Serialization.JevJsonContext.Default.SystemOneResponse)!;

        var first = TypedEvaluation.FromResponse<UrgencyCheck>(noul);
        var second = TypedEvaluation.FromResponse<DepartmentRouting>(choice);
        var third = TypedEvaluation.FromResponse<UrgencyCheck>(noul);

        Assert.Equal(0.95, first.Value.IsUrgent.Probability);
        Assert.Equal(Department.Billing, second.Value.Department.Value);
        Assert.Equal(first.Value, third.Value);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("1 2")]
    [InlineData("[1]]")]
    public void EnsureSingleJsonValue_RejectsAnythingButOneValue(string json)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => TypedEvaluation.EnsureSingleJsonValue(Encoding.UTF8.GetBytes(json), "arg"));

        Assert.Equal("arg", exception.ParamName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(" [1, {\"a\": null}] ")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void EnsureSingleJsonValue_AcceptsOneValue(string json)
        => TypedEvaluation.EnsureSingleJsonValue(Encoding.UTF8.GetBytes(json), "arg");
}
