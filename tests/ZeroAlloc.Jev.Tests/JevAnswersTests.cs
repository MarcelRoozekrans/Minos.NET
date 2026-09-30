using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.Tests;

/// <summary>A built set's answers: the same typed values as a generated set's, read by handle without allocating.</summary>
public sealed class JevAnswersTests
{
    private const string KeyedResponse = """
        {"answers":{
          "product":{"type":"choice","choice":"team-plan","probabilities":{"pro-plan":0.25,"team-plan":0.7},"confidence":0.66},
          "extra":{"type":"noul","noul":1},
          "effort":{"type":"score","score":0.8,"probabilities":{"0":0.4,"1":0.4,"2":0.2},"confidence":0.5}
        }}
        """;

    [Fact]
    public void Noul_EqualsTheGeneratedSetsAnswer()
    {
        var set = Built(JevQuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out var urgent));

        var answer = Parse(set, Fixture.Text("response-noul.json")).Get(urgent);

        Assert.Equal(Answers.Parse<UrgencyCheck>(Fixture.Text("response-noul.json")).IsUrgent.Probability, answer.Probability);
    }

    [Fact]
    public void EnumChoice_EqualsTheGeneratedSetsAnswer()
    {
        var set = Built(JevQuestionSet.CreateBuilder().Choice<Department>("department", "Which team?", out var department));

        var answer = Parse(set, Fixture.Text("response-choice.json")).Get(department);

        Assert.Equal(Answers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json")).Department, answer);
    }

    [Fact]
    public void EnumScore_EqualsTheGeneratedSetsAnswer()
    {
        var set = Built(JevQuestionSet.CreateBuilder().Score<Frustration>("frustration", "How frustrated?", out var frustration, l => l
            .Level(Frustration.Calm, "Calm").Level(Frustration.Frustrated, "Frustrated").Level(Frustration.VeryAngry, "Very angry")));

        var answer = Parse(set, Fixture.Text("response-score.json")).Get(frustration);

        Assert.Equal(Answers.Parse<FrustrationCheck>(Fixture.Text("response-score.json")).Frustration, answer);
    }

    [Fact]
    public void EnumScore_LevelIndexes_MapToTheMembersInCallOrder()
    {
        // OutOfOrderLevel declares High = 2, Low = 0, Medium = 1; given in declaration order, level 0 is High.
        var set = Built(JevQuestionSet.CreateBuilder().Score<OutOfOrderLevel>("level", "How?", out var level, l => l
            .Level(OutOfOrderLevel.High, "High").Level(OutOfOrderLevel.Low, "Low").Level(OutOfOrderLevel.Medium, "Medium")));

        var answer = Parse(set, """
            {"answers":{"level":{"type":"score","score":0.25,"probabilities":{"0":0.6,"1":0.3,"2":0.1},"confidence":0.4}}}
            """).Get(level);

        Assert.Equal(OutOfOrderLevel.High, answer.Value);
        Assert.Equal(0.6, answer.Probabilities[OutOfOrderLevel.High]);
        Assert.Equal(0.1, answer.Probabilities[OutOfOrderLevel.Medium]);
    }

    [Fact]
    public void Mixed_UsesSeparateBufferSlices_AndSkipsUnknownAnswers()
    {
        const string response = """
            {"answers":{
              "mood":{"type":"score","score":0.2,"probabilities":{"0":0.8,"1":0.2,"2":0.0},"confidence":0.7},
              "extra_question":{"type":"noul","noul":1},
              "requests_credentials":{"noul":0.1,"type":"noul"},
              "team":{"type":"choice","choice":"technical","probabilities":{"billing":0.1,"technical":0.9},"confidence":0.85}
            }}
            """;
        var set = Built(JevQuestionSet.CreateBuilder()
            .Noul("requests_credentials", "Does `message` ask for a credential?", out var credentials)
            .Choice<Department>("team", "Which team should handle `message`?", out var team)
            .Score<Frustration>("mood", "How frustrated is the customer?", out var mood, l => l
                .Level(Frustration.Calm, "Calm").Level(Frustration.Frustrated, "Frustrated").Level(Frustration.VeryAngry, "Very angry")));
        var generated = Answers.Parse<TicketTriage>(response);

        var answers = Parse(set, response);

        Assert.Equal(generated.RequestsCredentials.Probability, answers.Get(credentials).Probability);
        Assert.Equal(generated.Team, answers.Get(team));
        Assert.Equal(generated.Mood, answers.Get(mood));
    }

    [Fact]
    public void KeyedChoice_ReadsTheKeyConfidenceAndProbabilities()
    {
        var (set, product, _) = KeyedSet();

        var answer = Parse(set, KeyedResponse).Get(product);

        Assert.Equal("team-plan", answer.Value);
        Assert.Equal(0.66, answer.Confidence);
        Assert.Equal(3, answer.Probabilities.Count);
        Assert.Equal(0.25, answer.Probabilities["pro-plan"]);
        Assert.Equal(0.0, answer.Probabilities["other"]);
        Assert.Equal(0.7, answer.Probabilities[1]);
        Assert.Equal(
            [("pro-plan", 0.25), ("team-plan", 0.7), ("other", 0.0)],
            Enumerate(answer.Probabilities));
    }

    [Fact]
    public void KeyedScore_ArgmaxTie_IsTheLowerLevel()
    {
        var (set, _, effort) = KeyedSet();

        var answer = Parse(set, KeyedResponse).Get(effort);

        Assert.Equal(0, answer.Level);
        Assert.Equal(0.8, answer.Expected);
        Assert.Equal(0.5, answer.Confidence);
        Assert.Equal(0.2, answer.Probabilities[2]);
        Assert.Equal(0.4, answer.Probabilities["1"]);
    }

    [Fact]
    public void KeyedAnswers_HaveValueEquality()
    {
        var (set, product, effort) = KeyedSet();
        var first = Parse(set, KeyedResponse);
        var second = Parse(set, KeyedResponse);

        Assert.Equal(first.Get(product), second.Get(product));
        Assert.Equal(first.Get(product).GetHashCode(), second.Get(product).GetHashCode());
        Assert.Equal(first.Get(effort), second.Get(effort));
        Assert.True(first.Get(product) == second.Get(product));
        Assert.NotEqual(default, first.Get(product));
    }

    [Fact]
    public void KeyedProbabilityMap_RejectsUnknownKeys()
    {
        var (set, product, _) = KeyedSet();
        var probabilities = Parse(set, KeyedResponse).Get(product).Probabilities;

        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => probabilities["legacy-plan"]).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => probabilities[null!]).ParamName);
        Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => probabilities[3]).ParamName);
    }

    [Fact]
    public void MissingAnswer_IsInvalidResponse()
    {
        var (set, _, _) = KeyedSet();

        var result = TypedEvaluation.ParseResponse(Encoding.UTF8.GetBytes("""{"answers":{"extra":{"type":"noul","noul":1}}}"""), set.Parser);

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Contains("'product'", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_AllocatesNothing()
    {
        var (set, product, effort) = KeyedSet();
        var answers = Parse(set, KeyedResponse);

        // Warm up the JIT before measuring.
        _ = answers.Get(product);
        _ = answers.Get(effort);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = answers.Get(product);
            _ = answers.Get(effort);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void AHandleFromAnotherSet_Throws()
    {
        var (set, _, _) = KeyedSet();
        var (_, otherProduct, _) = KeyedSet();
        var answers = Parse(set, KeyedResponse);

        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(otherProduct)).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(default(KeyedChoiceHandle))).ParamName);
    }

    [Fact]
    public void AHandleAddedAfterTheBuild_Throws()
    {
        var builder = JevQuestionSet.CreateBuilder().Noul("is_urgent", "Urgent?", out _);
        var set = Built(builder);
        builder.Noul("later", "Later?", out var later);
        var answers = Parse(set, Fixture.Text("response-noul.json"));

        Assert.Throws<ArgumentException>(() => answers.Get(later));
    }

    private static (JevQuestionSet Set, KeyedChoiceHandle Product, KeyedScoreHandle Effort) KeyedSet()
    {
        var set = Built(JevQuestionSet.CreateBuilder()
            .Choice("product", "Which product?", out var product, o => o.Option("pro-plan", "Pro").Option("team-plan", "Team").Option("other"))
            .Score("effort", "How much effort?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days")));
        return (set, product, effort);
    }

    private static List<(string, double)> Enumerate(KeyedProbabilityMap map)
    {
        var items = new List<(string, double)>();
        foreach (var (key, probability) in map)
        {
            items.Add((key, probability));
        }

        return items;
    }

    private static JevAnswers Parse(JevQuestionSet set, string response)
    {
        var result = TypedEvaluation.ParseResponse(Encoding.UTF8.GetBytes(response), set.Parser);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static JevQuestionSet Built(JevQuestionSetBuilder builder)
    {
        var built = builder.Build();
        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
