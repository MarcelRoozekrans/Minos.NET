using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Serialization;

namespace Minos.Tests;

public sealed class GeneratedQuestionSetTests
{
    private static readonly string[] ExpectedPriorityCriteriaKeys = ["low", "urgent"];

    [Fact]
    public void Noul_QuestionsMatchFixture() => AssertQuestions<UrgencyCheck>("request-noul.json");

    [Fact]
    public void NoulWithoutCriteria_QuestionsMatchFixture() => AssertQuestions<MinimalUrgencyCheck>("request-noul-minimal.json");

    [Fact]
    public void Choice_QuestionsMatchFixture() => AssertQuestions<DepartmentRouting>("request-choice.json");

    [Fact]
    public void Score_QuestionsMatchFixture() => AssertQuestions<FrustrationCheck>("request-score.json");

    [Fact]
    public void StructuredCriteria_QuestionsMatchFixture() => AssertQuestions<StructuredRouting>("request-structured-criteria.json");

    [Fact]
    public void StructuredCriteria_DeserializeThroughWireModel()
    {
        var json = "{\"state\":\"x\",\"questions\":" + Encoding.UTF8.GetString(StructuredRouting.QuestionsUtf8) + "}";

        var request = JsonSerializer.Deserialize(json, JevJsonContext.Default.SystemOneRequest)!;

        var department = Assert.IsType<ChoiceQuestion>(request.Questions["department"]);
        Assert.True(department.Criteria["billing"]!.Value.TryGetJson(out var billing));
        Assert.Equal("I was charged twice", billing.GetProperty("examples")[0].GetString());
        Assert.True(department.Criteria["sales"]!.Value.IsString);
    }

    [Fact]
    public void Questions_DeserializeThroughWireModel()
    {
        var json = "{\"state\":\"x\",\"questions\":" + Encoding.UTF8.GetString(TicketTriage.QuestionsUtf8) + "}";

        var request = JsonSerializer.Deserialize(json, JevJsonContext.Default.SystemOneRequest)!;

        Assert.IsType<NoulQuestion>(request.Questions["requests_credentials"]);
        var team = Assert.IsType<ChoiceQuestion>(request.Questions["team"]);
        Assert.Equal(4, team.Criteria.Count);
        Assert.Null(team.Criteria["other"]);
        var mood = Assert.IsType<ScoreQuestion>(request.Questions["mood"]);
        Assert.Equal(3, mood.Criteria.Count);
    }

    [Fact]
    public void Noul_ParsesFixture()
    {
        var answers = Answers.Parse<UrgencyCheck>(Fixture.Text("response-noul.json"));

        Assert.Equal(0.95, answers.IsUrgent.Probability);
        Assert.True(answers.IsUrgent.Value);
    }

    [Fact]
    public void Noul_TypeLast_ParsesFixture()
        => Assert.Equal(0.4, Answers.Parse<UrgencyCheck>(Fixture.Text("response-type-last.json")).IsUrgent.Probability);

    [Fact]
    public void Choice_ParsesFixture()
    {
        var department = Answers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json")).Department;

        Assert.Equal(Department.Billing, department.Value);
        Assert.Equal(0.81, department.Confidence);
        Assert.Equal(0.88, department.Probabilities[Department.Billing]);
        Assert.Equal(0.12, department.Probabilities[Department.Technical]);
        Assert.Equal(0.0, department.Probabilities[Department.Other]);
    }

    [Fact]
    public void Choice_ParsedTwice_AreEqualByRecordEquality()
    {
        var first = Answers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json"));
        var second = Answers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Score_ParsedTwice_AreEqualByRecordEquality()
    {
        var first = Answers.Parse<FrustrationCheck>(Fixture.Text("response-score.json"));
        var second = Answers.Parse<FrustrationCheck>(Fixture.Text("response-score.json"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Score_ParsesFixture()
    {
        var frustration = Answers.Parse<FrustrationCheck>(Fixture.Text("response-score.json")).Frustration;

        Assert.Equal(Frustration.Frustrated, frustration.Value);
        Assert.Equal(1.05, frustration.Expected);
        Assert.Equal(0.92, frustration.Confidence);
        Assert.Equal(0.05, frustration.Probabilities[Frustration.VeryAngry]);
    }

    [Fact]
    public void Mixed_UsesSeparateBufferSlices_AndSkipsUnknownAnswers()
    {
        const string response = """
            {"answers":{
              "mood":{"type":"score","score":0.2,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.8,"1":0.2,"2":0.0},"confidence":0.7},
              "extra_question":{"type":"noul","noul":1},
              "requests_credentials":{"noul":0.1,"type":"noul"},
              "team":{"type":"choice","choice":"technical","probabilities":{"billing":0.1,"technical":0.9},"confidence":0.85}
            }}
            """;

        var triage = Answers.Parse<TicketTriage>(response);

        Assert.Equal(0.1, triage.RequestsCredentials.Probability);
        Assert.Equal(Department.Technical, triage.Team.Value);
        Assert.Equal(0.1, triage.Team.Probabilities[Department.Billing]);
        Assert.Equal(0.0, triage.Team.Probabilities[Department.Sales]);
        Assert.Equal(Frustration.Calm, triage.Mood.Value);
        Assert.Equal(0.8, triage.Mood.Probabilities[Frustration.Calm]);
    }

    [Theory]
    [InlineData("""{"answers":{}}""")]
    [InlineData("""{"answers":{"department":{"type":"noul","noul":0.5}}}""")]
    [InlineData("""{"answers":{"department":{"type":"choice","choice":"legal","probabilities":{},"confidence":0.5}}}""")]
    public void Choice_InvalidAnswers_Throw(string response)
        => Assert.ThrowsAny<JsonException>(() => Answers.Parse<DepartmentRouting>(response));

    [Fact]
    public void Score_UnknownLevel_Throws()
        => Assert.ThrowsAny<JsonException>(() => Answers.Parse<FrustrationCheck>(
            """{"answers":{"frustration":{"type":"score","score":1,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"3":1.0},"confidence":0.5}}}"""));

    [Fact]
    public void Parse_LeavesReaderOnTheAnswersEnd()
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(Fixture.Text("response-noul.json")));
        while (reader.Read() && !(reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("answers"u8)))
        {
        }

        reader.Read();
        UrgencyCheck.Parse(ref reader);

        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
        Assert.True(reader.Read());
        Assert.True(reader.ValueTextEquals("usage"u8));
    }

    [Fact]
    public void Escaping_RoundTripsExactly()
    {
        var questions = JsonNode.Parse(EdgeCases.QuestionsUtf8)!;

        Assert.Equal(EdgeCases.TrickyInstructions, (string?)questions["tricky"]!["instructions"]);
    }

    [Fact]
    public void ExplicitValues_KeyOverride_AndAliases()
    {
        var criteria = JsonNode.Parse(EdgeCases.QuestionsUtf8)!["priority"]!["criteria"]!.AsObject();
        Assert.Equal(ExpectedPriorityCriteriaKeys, criteria.Select(pair => pair.Key));

        var priority = Answers.Parse<EdgeCases>("""
            {"answers":{
              "tricky":{"type":"noul","noul":0.5},
              "priority":{"type":"choice","choice":"urgent","probabilities":{"low":0.3,"urgent":0.7},"confidence":0.6}
            }}
            """).Priority;

        Assert.Equal(Priority.High, priority.Value);
        Assert.Equal(0.3, priority.Probabilities[Priority.Low]);
        Assert.Equal(0.3, priority.Probabilities[Priority.Legacy]);
    }

    private static void AssertQuestions<T>(string fixture)
        where T : IJevQuestionSet<T>
    {
        var expected = Fixture.Load(fixture)["questions"];
        var actual = JsonNode.Parse(T.QuestionsUtf8);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected {expected?.ToJsonString()} but generated {actual?.ToJsonString()}.");
    }
}
