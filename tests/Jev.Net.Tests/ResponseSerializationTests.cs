using System.Text.Json;
using Jev.Net.Serialization;

namespace Jev.Net.Tests;

public sealed class ResponseSerializationTests
{
    [Fact]
    public void Noul_Deserializes()
    {
        var response = Deserialize("response-noul.json");

        Assert.Equal("jev-1.13.0", response.Model);
        var answer = Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]);
        Assert.Equal(0.95, answer.Noul);
        Assert.Equal(296, response.Usage.InputTokens);
        Assert.Equal(20, response.Usage.OutputTokens);
    }

    [Fact]
    public void Choice_Deserializes()
    {
        var response = Deserialize("response-choice.json");

        var answer = Assert.IsType<ChoiceAnswer>(response.Answers["department"]);
        Assert.Equal("billing", answer.Choice);
        Assert.Equal(3, answer.Probabilities.Count);
        Assert.Equal(0.88, answer.Probabilities["billing"]);
        Assert.Equal(0.12, answer.Probabilities["technical"]);
        Assert.Equal(0.0, answer.Probabilities["sales"]);
        Assert.Equal(0.81, answer.Confidence);
        Assert.Equal(318, response.Usage.InputTokens);
        Assert.Equal(34, response.Usage.OutputTokens);
    }

    [Fact]
    public void Score_Deserializes()
    {
        var response = Deserialize("response-score.json");

        var answer = Assert.IsType<ScoreAnswer>(response.Answers["frustration"]);
        Assert.Equal(1.05, answer.Score);
        Assert.Equal(3, answer.Legend.Count);
        Assert.Equal("Calm", answer.Legend["0"]);
        Assert.Equal("Frustrated", answer.Legend["1"]);
        Assert.Equal("Very angry", answer.Legend["2"]);
        Assert.Equal(3, answer.Probabilities.Count);
        Assert.Equal(0.0, answer.Probabilities["0"]);
        Assert.Equal(0.95, answer.Probabilities["1"]);
        Assert.Equal(0.05, answer.Probabilities["2"]);
        Assert.Equal(0.92, answer.Confidence);
    }

    [Fact]
    public void Discriminator_InLastPosition_IsAccepted()
    {
        var response = Deserialize("response-type-last.json");

        var answer = Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]);
        Assert.Equal(0.4, answer.Noul);
    }

    [Fact]
    public void UnknownAnswerType_Throws()
    {
        var exception = Assert.Throws<JsonException>(() => Deserialize("response-unknown-type.json"));

        Assert.Contains("ranking", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredField_Throws()
    {
        Assert.Throws<JsonException>(() => Deserialize("response-missing-usage.json"));
    }

    [Fact]
    public void NullModel_Throws()
    {
        const string Json = """
            {
              "model": null,
              "answers": {
                "is_urgent": { "type": "noul", "noul": 0.95 }
              },
              "usage": { "input_tokens": 296, "output_tokens": 20 }
            }
            """;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(Json, JevJsonContext.Default.SystemOneResponse));
    }

    [Fact]
    public void NullChoice_Throws()
    {
        const string Json = """
            {
              "model": "jev-1.13.0",
              "answers": {
                "department": {
                  "type": "choice",
                  "choice": null,
                  "probabilities": { "billing": 0.88, "technical": 0.12, "sales": 0.0 },
                  "confidence": 0.81
                }
              },
              "usage": { "input_tokens": 318, "output_tokens": 34 }
            }
            """;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(Json, JevJsonContext.Default.SystemOneResponse));
    }

    [Fact]
    public void Models_Deserialize()
    {
        var list = JsonSerializer.Deserialize(Fixture.Text("models.json"), JevJsonContext.Default.ModelList);

        Assert.NotNull(list);
        Assert.Equal(2, list.Models.Count);
        Assert.Equal("jev-latest", list.Models[0].Name);
        Assert.Equal("The most recent stable, official release.", list.Models[0].Description);
        Assert.Equal("2026-09-15", list.Models[0].ReleaseDate);
        Assert.Equal("jev-preview", list.Models[1].Name);
    }

    private static SystemOneResponse Deserialize(string fixture)
    {
        var response = JsonSerializer.Deserialize(Fixture.Text(fixture), JevJsonContext.Default.SystemOneResponse);
        Assert.NotNull(response);
        return response;
    }
}
