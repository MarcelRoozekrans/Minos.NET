using System.Text;

namespace Minos.Evaluation.Tests;

public sealed class DatasetLoadTests
{
    private const string EveryKind = """
        {"id":"a","state":"Help! My payouts have been failing.","source":"unit#1","version":1,"questions":{
          "is_urgent":{"type":"noul","instructions":"Is it urgent?","criteria":{"true":"Time-sensitive","false":"Can wait"}},
          "department":{"type":"choice","instructions":"Which team?","criteria":{
            "billing":{"description":"Payments","examples":["refund"],"not_for":["login"]},
            "technical":"Bugs",
            "other":null}},
          "frustration":{"type":"score","instructions":"How frustrated?","criteria":["Calm","Frustrated","Very angry"]}},
          "expected":{"is_urgent":true,"department":"billing","frustration":2}}
        """;

    private static DecisionDataset ParseOk(string jsonl)
    {
        var result = DecisionDataset.Parse(jsonl);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }

    [Fact]
    public void Parse_ReadsAJsonCriterion_AsCriterionJson()
    {
        const string line = """
            {"id":"j","state":"s","questions":{
              "department":{"type":"choice","instructions":"Which team?","criteria":{"billing":{"json":{"description":"Payments","weight":2}},"other":null}},
              "mood":{"type":"score","instructions":"How upset?","criteria":["calm",{"json":["angry","very"]}]}},
              "expected":{"department":"billing","mood":1}}
            """;

        var item = Assert.Single(ParseOk(line.ReplaceLineEndings(" ")));

        var billing = item.Definition.Questions[0].Options[0].Criterion!;
        Assert.Null(billing.Description);
        Assert.True(billing.JsonContent!.Value.TryGetJson(out var billingJson));
        Assert.Equal("""{"description":"Payments","weight":2}""", billingJson.GetRawText());
        var level = item.Definition.Questions[1].Options[1].Criterion!;
        Assert.True(level.JsonContent!.Value.TryGetJson(out var levelJson));
        Assert.Equal("""["angry","very"]""", levelJson.GetRawText());
        Assert.Equal("calm", item.Definition.Questions[1].Options[0].Criterion!.Description);
    }

    [Fact]
    public void Parse_ReadsEachKind()
    {
        var item = Assert.Single(ParseOk(EveryKind.ReplaceLineEndings(" ")));

        Assert.Equal("a", item.Id);
        Assert.Equal("unit#1", item.Source);
        Assert.Equal(1, item.Line);
        Assert.True(item.State.TryGetString(out var state));
        Assert.Equal("Help! My payouts have been failing.", state);

        var questions = item.Definition.Questions;
        Assert.Equal(["is_urgent", "department", "frustration"], questions.Select(q => q.Key));
        Assert.Equal([QuestionKind.Noul, QuestionKind.Choice, QuestionKind.Score], questions.Select(q => q.Kind));

        Assert.Equal(DecisionContent.FromString("Time-sensitive"), questions[0].WhenTrue);
        Assert.Equal(DecisionContent.FromString("Can wait"), questions[0].WhenFalse);

        Assert.Equal(["billing", "technical", "other"], questions[1].Options.Select(o => o.Key));
        var billing = questions[1].Options[0].Criterion!;
        Assert.Equal("Payments", billing.Description);
        Assert.Equal(["refund"], billing.Examples);
        Assert.Equal(["login"], billing.NotFor);
        Assert.Equal("Bugs", questions[1].Options[1].Criterion!.Description);
        Assert.Null(questions[1].Options[2].Criterion);

        Assert.Equal(["0", "1", "2"], questions[2].Options.Select(o => o.Key));
        Assert.Equal(["Calm", "Frustrated", "Very angry"], questions[2].Options.Select(o => o.Criterion!.Description));

        Assert.Equal(ExpectedAnswer.ForNoul(true), item.Expected["is_urgent"]);
        Assert.Equal(ExpectedAnswer.ForChoice("billing"), item.Expected["department"]);
        Assert.Equal(ExpectedAnswer.ForScore(2), item.Expected["frustration"]);
    }

    [Fact]
    public void Parse_ReadsStructuredStateAndInstructions()
    {
        const string state = """{"ticket":{"id":7},"tags":["a","b"]}""";
        const string instructions = """["Is it","urgent?"]""";
        var line = $$$"""{"id":"s","state":{{{state}}},"questions":{"q":{"type":"noul","instructions":{{{instructions}}}}},"expected":{"q":false}}""";

        var item = Assert.Single(ParseOk(line));

        Assert.Equal(DecisionContent.FromUtf8Json("""{"ticket":{"id":7},"tags":["a","b"]}"""u8), item.State);
        Assert.Equal(DecisionContent.FromUtf8Json("""["Is it","urgent?"]"""u8), item.Definition.Questions[0].Instructions);
    }

    [Fact]
    public void Parse_SkipsBlankLines_AndKeepsLineNumbers()
    {
        const string item = """{"id":"x","state":"s","questions":{"q":{"type":"noul","instructions":"i"}},"expected":{"q":true}}""";
        var jsonl = string.Join('\n', (IEnumerable<string>)[item, string.Empty, "   ", item.Replace("\"x\"", "\"y\"", StringComparison.Ordinal)]);

        var dataset = ParseOk(jsonl);

        Assert.Equal(2, dataset.Count);
        Assert.Equal([1, 4], dataset.Select(i => i.Line));
        Assert.Equal(["x", "y"], dataset.Select(i => i.Id));
    }

    [Fact]
    public void Parse_QuestionWithoutExpected_IsKeptUnscored()
    {
        const string line = """{"id":"x","state":"s","questions":{"a":{"type":"noul","instructions":"i"},"b":{"type":"noul","instructions":"j"}},"expected":{"a":true}}""";

        var item = Assert.Single(ParseOk(line));

        Assert.Equal(2, item.Definition.Questions.Count);
        Assert.Equal(["a"], item.Expected.Keys);
    }

    [Fact]
    public void Expected_IsReadOnly()
    {
        var item = Assert.Single(ParseOk(EveryKind.ReplaceLineEndings(" ")));

        Assert.IsNotType<Dictionary<string, ExpectedAnswer>>(item.Expected);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, ExpectedAnswer>>(item.Expected);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary["is_urgent"] = ExpectedAnswer.ForNoul(false));
    }

    [Fact]
    public void Load_ReadsAFile_AndThrowsForAMissingOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"minos-dataset-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, EveryKind.ReplaceLineEndings(" ") + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            Assert.Single(DecisionDataset.Load(path).Value);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Throws<FileNotFoundException>(() => DecisionDataset.Load(path));
    }
}
