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

    [Fact]
    public void AJsonCriterion_CannotBeCombinedWithOtherMembers()
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"x":{"json":{"a":1},"description":"d"},"y":null}}}""",
                expected: """{"q":"x"}"""),
            "'questions.q.criteria.x.json' stands alone");

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("7")]
    [InlineData("null")]
    public void AJsonCriterion_MustBeAnObjectOrArray(string json)
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"score","instructions":"i","criteria":[{"json":JSON}]}}""".Replace("JSON", json, StringComparison.Ordinal),
                expected: """{"q":0}"""),
            "'questions.q.criteria[0].json' must be a JSON object or array");

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
        Assert.StartsWith("MIN005 'questions.q': ", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreRuleFailure_EmptyChoice()
    {
        var line = Item(questions: """{"q":{"type":"choice","instructions":"i"}}""", expected: """{"q":"x"}""");
        var problems = Problems(OnLine2(line));
        Assert.All(problems, p => Assert.Equal(2, p.Line));
        Assert.Contains(problems, p => p.Message.StartsWith("MIN001", StringComparison.Ordinal));
    }

    [Fact]
    public void ShapeProblemAndCoreRuleOnTheSameLine_AreBothReported()
    {
        var line = Item(
            state: "7",
            questions: """{"q":{"type":"noul","instructions":"  "}}""");

        var problems = Problems(OnLine2(line));

        Assert.All(problems, p => Assert.Equal(2, p.Line));
        Assert.Contains(problems, p => p.Message.Contains("'state'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Message.StartsWith("MIN003", StringComparison.Ordinal));
    }

    [Fact]
    public void VersionThatIsNotANumber()
        => AssertSingle(Item(extra: ""","version":"one" """), "'version' must be the number 1");

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("1.5")]
    public void ANonIntegerVersion_MustBeTheNumber1(string version)
        => AssertSingle(Item(extra: ",\"version\":" + version), "'version' must be the number 1");

    [Fact]
    public void ScoreLevelBeyondInt32_IsOutOfRange()
        => AssertSingle(
            Item(
                questions: """{"q":{"type":"score","instructions":"i","criteria":["a","b"]}}""",
                expected: """{"q":3000000000}"""),
            "out of range");

    // Built in the test body: xUnit turns an unpaired surrogate in theory data into U+FFFD.
    [Fact]
    public void UnpairedSurrogateEscape_InEveryStringPosition_IsAProblemNotAnException()
    {
        const string Bad = "\\ud800";
        var lines = new[]
        {
            Item(id: $"\"{Bad}\""),
            Item(state: $"\"{Bad}\""),
            Item(extra: $",\"source\":\"{Bad}\""),
            Item(questions: """{"q":{"type":"BAD","instructions":"i"}}""".Replace("BAD", Bad, StringComparison.Ordinal)),
            Item(questions: """{"q":{"type":"noul","instructions":"BAD"}}""".Replace("BAD", Bad, StringComparison.Ordinal)),
            Item(questions: """{"q":{"type":"noul","instructions":"i","criteria":{"true":"BAD"}}}""".Replace("BAD", Bad, StringComparison.Ordinal)),
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"x":{"description":"d","examples":["BAD"]},"y":null}}}""".Replace("BAD", Bad, StringComparison.Ordinal),
                expected: """{"q":"x"}"""),
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"BAD":"d","y":null}}}""".Replace("BAD", Bad, StringComparison.Ordinal),
                expected: """{"q":"y"}"""),
            Item(
                questions: """{"q":{"type":"choice","instructions":"i","criteria":{"x":"d","y":null}}}""",
                expected: """{"q":"BAD"}""".Replace("BAD", Bad, StringComparison.Ordinal)),
            Item(questions: """{"BAD":{"type":"noul","instructions":"i"}}""".Replace("BAD", Bad, StringComparison.Ordinal), expected: "{}"),
        };

        foreach (var line in lines)
        {
            var problems = Problems(OnLine2(line));
            Assert.All(problems, p => Assert.Equal(2, p.Line));
            Assert.Contains(problems, p => p.Message.Contains("not valid Unicode", StringComparison.Ordinal));
        }
    }

    // C1: the reader must build from the copy the validator checked, not from the last duplicate.
    [Theory]
    [InlineData("""{"x":{"type":"zzz"}}""")]
    [InlineData("5")]
    public void DuplicateTopLevelQuestions_IsAProblemNotAnException(string secondCopy)
    {
        var line = Item(extra: ",\"questions\":" + secondCopy);

        var problems = Problems(OnLine2(line));

        Assert.All(problems, p => Assert.Equal(2, p.Line));
        Assert.Contains(problems, p => string.Equals(p.Message, "Duplicate member 'questions'.", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateTopLevelQuestions_WithACoreFailureInTheFirstCopy_ReportsBoth()
    {
        var line = Item(
            questions: """{"q":{"type":"noul","instructions":"  "}}""",
            extra: ""","questions":{"x":{"type":"zzz"}}""");

        var problems = Problems(OnLine2(line));

        Assert.Contains(problems, p => string.Equals(p.Message, "Duplicate member 'questions'.", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Message.StartsWith("MIN003 'questions.q': ", StringComparison.Ordinal));
    }

    // I1: built in the test body, because xUnit turns an unpaired surrogate in theory data into U+FFFD.
    [Fact]
    public void RawUnpairedSurrogate_InAValue_IsAProblemNotAnException()
    {
        var lone = ((char)0xD800).ToString();

        AssertSingle(Item(state: "\"a" + lone + "b\""), "not valid Unicode");
    }

    [Fact]
    public void RawUnpairedSurrogate_InAMemberName_IsAProblemNotAnException()
    {
        var lone = ((char)0xDC00).ToString();

        AssertSingle(Item(questions: "{\"" + lone + "\":{\"type\":\"noul\",\"instructions\":\"i\"}}", expected: "{}"), "not valid Unicode");
    }

    [Fact]
    public void RawSurrogatePair_IsReadAsText()
    {
        var line = Item(state: "\"" + char.ConvertFromUtf32(0x1F600) + "\"");

        var result = DecisionDataset.Parse(line);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value[0].State.TryGetString(out var text));
        Assert.Equal(char.ConvertFromUtf32(0x1F600), text);
    }

    // I2: the format needs at least one question.
    [Fact]
    public void NoQuestions()
        => AssertSingle(Item(questions: "{}", expected: "{}"), "'questions' must have at least one question.");

    // M1: a core failure names the question it is about.
    [Fact]
    public void CoreRuleFailure_NamesTheQuestion()
    {
        var problem = Assert.Single(Problems(OnLine2(Item(questions: """{"q":{"type":"noul","instructions":"  "}}"""))));
        Assert.StartsWith("MIN003 'questions.q': ", problem.Message, StringComparison.Ordinal);
    }

    // M5: Parse(string) skips one leading byte order mark, as Parse(Stream) does.
    [Fact]
    public void Parse_String_SkipsALeadingByteOrderMark()
    {
        var result = DecisionDataset.Parse("\uFEFF" + Valid);

        Assert.True(result.IsSuccess);
        Assert.Equal("a", Assert.Single(result.Value).Id);
    }

    [Fact]
    public void Parse_String_ReportsASecondByteOrderMark()
        => Assert.False(DecisionDataset.Parse("\uFEFF\uFEFF" + Valid).IsSuccess);

    // M6: the JSON error gives the byte in the line, not the parser's LineNumber, which is always 0 for one line.
    [Fact]
    public void MalformedJson_GivesTheByteNotTheParserLineNumber()
    {
        var problem = Assert.Single(Problems(OnLine2("""{"id":"b", x""")));

        Assert.DoesNotContain("LineNumber", problem.Message, StringComparison.Ordinal);
        Assert.StartsWith("The line is not valid JSON at byte 11: ", problem.Message, StringComparison.Ordinal);
    }

    // M4: invalid UTF-8 is a problem on its line, not a silent U+FFFD, and the lines after it are still read.
    [Fact]
    public void InvalidUtf8_IsAProblemOnItsLine()
    {
        var bytes = new List<byte>(Encoding.UTF8.GetBytes(Valid + "\n"));
        bytes.AddRange(Encoding.UTF8.GetBytes(Item(state: "\"ab\"")).Select(b => b == (byte)'a' ? (byte)0xFF : b));
        bytes.AddRange(Encoding.UTF8.GetBytes("\n" + Item(id: "\"c\"", state: "7") + "\n"));
        using var stream = new MemoryStream([.. bytes]);

        var result = DecisionDataset.Parse(stream);

        Assert.False(result.IsSuccess);
        Assert.Equal([2, 3], result.Error.Select(p => p.Line));
        Assert.Contains("not valid UTF-8", result.Error[0].Message, StringComparison.Ordinal);
        Assert.Contains("'state'", result.Error[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stream_SplitsLinesOnCrLfCrAndLf_LikeATextReader()
    {
        var text = Valid + "\r" + Valid.Replace("\"a\"", "\"c\"", StringComparison.Ordinal) + "\r\n\r\n" + Item(state: "7") + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var problem = Assert.Single(DecisionDataset.Parse(stream).Error);

        Assert.Equal(4, problem.Line);
    }

    [Fact]
    public void LargeStream_WithCrLfAndMultiByteText_KeepsEveryLine()
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= 5_000; i++)
        {
            var state = new string('\u00e9', i % 97) + char.ConvertFromUtf32(0x1F600);
            builder.Append(Valid.Replace("\"a\"", $"\"id{i}\"", StringComparison.Ordinal).Replace("\"s\"", $"\"{state}\"", StringComparison.Ordinal)).Append("\r\n");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
        var result = DecisionDataset.Parse(stream);

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000, result.Value.Count);
        Assert.Equal(5_000, result.Value[^1].Line);
        Assert.True(result.Value[95].State.TryGetString(out var text));
        Assert.Equal(new string('\u00e9', 96) + char.ConvertFromUtf32(0x1F600), text);
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
