using System.Text;

namespace Minos.Evaluation.Tests;

public sealed class DatasetProblemTests
{
    private const string Valid = """{"id":"a","state":"s","questions":{"q":{"type":"noul","instructions":"i"}},"expected":{"q":true}}""";

    private static string Item(
        string id = "\"b\"",
        string state = "\"s\"",
        string questions = """{"q":{"type":"noul","instructions":"i"}}""",
        string expected = """{"q":true}""",
        string extra = "")
        => $$"""{"id":{{id}},"state":{{state}},"questions":{{questions}},"expected":{{expected}}{{extra}}}""";

    private static (int Line, string Message)[] Problems(string jsonl)
    {
        var result = DecisionDataset.Parse(jsonl);
        Assert.True(!result.IsSuccess, "the file should have problems");
        return [.. result.Error.Select(p => (p.Line, p.Message))];
    }

    private static string OnLine2(string line) => Valid + "\n" + line;

    private static void AssertSingle(string line, string fragment, int problemLine = 2)
    {
        var problem = Assert.Single(Problems(OnLine2(line)));
        Assert.Equal(problemLine, problem.Line);
        Assert.Contains(fragment, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJson()
        => AssertSingle("""{"id":"b", """, "not valid JSON");

    [Fact]
    public void LineThatIsNotAnObject()
        => AssertSingle("[1,2]", "JSON object");

    [Theory]
    [InlineData("id", """{"state":"s","questions":{"q":{"type":"noul","instructions":"i"}},"expected":{"q":true}}""")]
    [InlineData("state", """{"id":"b","questions":{"q":{"type":"noul","instructions":"i"}},"expected":{"q":true}}""")]
    [InlineData("questions", """{"id":"b","state":"s","expected":{"q":true}}""")]
    [InlineData("expected", """{"id":"b","state":"s","questions":{"q":{"type":"noul","instructions":"i"}}}""")]
    public void MissingMember(string member, string line)
        => AssertSingle(line, $"'{member}'");

    [Fact]
    public void DuplicateId()
    {
        var problem = Assert.Single(Problems(Valid + "\n" + Valid.Replace("\"a\"", "\"c\"", StringComparison.Ordinal) + "\n" + Valid));
        Assert.Equal(3, problem.Line);
        Assert.Contains("line 1", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMember_TopLevel()
        => AssertSingle(Item(extra: ""","expect":{}"""), "'expect'");

    [Fact]
    public void UnknownMember_OnAQuestion()
        => AssertSingle(
            Item(questions: """{"q":{"type":"noul","instruction":"i","instructions":"i"}}"""),
            "'questions.q.instruction'");

    [Fact]
    public void UnknownMember_InACriterion()
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"x":{"description":"d","example":["e"]},"y":null}}}""",
                expected: """{"q":"x"}"""),
            "'questions.q.criteria.x.example'");

    [Theory]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("null")]
    public void StateOfTheWrongType(string state)
        => AssertSingle(Item(state: state), "'state'");

    [Fact]
    public void WrongType()
        => AssertSingle(
            Item(questions: """{"q":{"type":"yesno","instructions":"i"}}"""),
            "'yesno'");

    [Fact]
    public void ExpectedKeyWithoutQuestion()
        => AssertSingle(Item(expected: """{"q":true,"ghost":false}"""), "'ghost'");

    [Fact]
    public void ExpectedOptionNotInCriteria()
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"card_arrival":"c","other":"o"}}}""",
                expected: """{"q":"missing"}"""),
            "'missing'");

    [Fact]
    public void ExpectedOptionDiffersOnlyInCase()
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"card_arrival":"c","other":"o"}}}""",
                expected: """{"q":"Card_Arrival"}"""),
            "'Card_Arrival'");

    [Theory]
    [InlineData("-1")]
    [InlineData("3")]
    public void ScoreLevelOutOfRange(string level)
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"score","instructions":"i","criteria":["a","b","c"]}}""",
                expected: $$"""{"q":{{level}}}"""),
            "out of range");

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e0")]
    public void ScoreLevelNotAnInteger(string level)
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"score","instructions":"i","criteria":["a","b","c"]}}""",
                expected: $$"""{"q":{{level}}}"""),
            "integer");

    [Theory]
    [InlineData("""{"q":{"type":"noul","instructions":"i"}}""", "\"yes\"")]
    [InlineData("""{"q":{"type":"choice","instructions":"i","criteria":{"a":"a","b":"b"}}}""", "1")]
    [InlineData("""{"q":{"type":"score","instructions":"i","criteria":["a","b"]}}""", "true")]
    public void ExpectedValueOfTheWrongType(string questions, string value)
        => AssertSingle(Item(questions: questions, expected: $$"""{"q":{{value}}}"""), "'expected.q'");

    [Fact]
    public void NewerVersion()
        => AssertSingle(
            Item(extra: ""","version":2"""),
            "This file uses dataset format version 2; this loader reads version 1.");

    [Fact]
    public void VersionOne_IsAccepted()
        => Assert.True(DecisionDataset.Parse(Item(extra: ""","version":1""")).IsSuccess);

    [Fact]
    public void DuplicateQuestionKey()
        => AssertSingle(
            Item(questions: """{"q":{"type":"noul","instructions":"i"},"q":{"type":"noul","instructions":"j"}}"""),
            "'questions.q'");

    [Fact]
    public void CoreRuleFailure_BlankInstructions()
    {
        var problem = Assert.Single(Problems(OnLine2(Item(questions: """{"q":{"type":"noul","instructions":"  "}}"""))));
        Assert.Equal(2, problem.Line);
        Assert.StartsWith("MIN003", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreRuleFailure_OneLevelScore()
    {
        var line = Item(
            questions: """{"q":{"type":"score","instructions":"i","criteria":["only"]}}""",
            expected: """{"q":0}""");
        var problem = Assert.Single(Problems(OnLine2(line)));
        Assert.Equal(2, problem.Line);
        Assert.StartsWith("MIN005", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreRuleFailure_EmptyChoice()
    {
        var line = Item(questions: """{"q":{"type":"choice","instructions":"i"}}""", expected: """{"q":"x"}""");
        var problems = Problems(OnLine2(line));
        Assert.All(problems, p => Assert.Equal(2, p.Line));
        Assert.Contains(problems, p => p.Message.StartsWith("MIN", StringComparison.Ordinal));
    }

    [Fact]
    public void SeveralProblemsInOneFile()
    {
        var jsonl = string.Join('\n', (IEnumerable<string>)["{bad", Valid, Item(state: "7"), Item(id: "\"d\"", extra: ""","version":9""")]);

        var problems = Problems(jsonl);

        Assert.Equal([1, 3, 4], problems.Select(p => p.Line));
    }

    [Fact]
    public void BomAndCrlf()
    {
        var text = Valid + "\r\n" + Valid.Replace("\"a\"", "\"c\"", StringComparison.Ordinal) + "\r\n" + Item(state: "7") + "\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        using var stream = new MemoryStream(bytes);

        var result = DecisionDataset.Parse(stream);

        var problem = Assert.Single(result.Error);
        Assert.Equal(3, problem.Line);
    }

    [Fact]
    public void LargeStream()
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= 12_000; i++)
        {
            builder.Append(i == 11_000
                ? Item(id: "\"bad\"", state: "7")
                : Valid.Replace("\"a\"", $"\"id{i}\"", StringComparison.Ordinal)).Append('\n');
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
        var result = DecisionDataset.Parse(stream);

        var problem = Assert.Single(result.Error);
        Assert.Equal(11_000, problem.Line);
    }
}
