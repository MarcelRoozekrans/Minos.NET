using System.Text;
using Minos.OpenAI.Protocols;

namespace Minos.OpenAI.Tests;

public sealed class OpenAIResponseReaderTests
{
    private const string Predicate = """{"type":"predicate","name":"refund","probability":0.92}""";
    private const string Choice = """{"type":"choice","name":"topic","choice":"billing","probabilities":[{"value":"billing","probability":0.8},{"value":"other","probability":0.2}],"confidence":0.6}""";
    private const string Score = """{"type":"score","name":"urgency","score":1.1,"probabilities":[{"value":0,"label":"0","probability":0.1},{"value":1,"label":"1","probability":0.7},{"value":2,"label":"2","probability":0.2}],"confidence":0.5}""";

    [Fact]
    public void SuccessAllKinds_ReadsEveryAnswer()
    {
        var response = Success(Fixture("success-all-kinds.json"), TestSets.AllKinds());

        AssertAllKinds(response);
    }

    [Fact]
    public void AnswersOutOfOrder_AreReadByName()
    {
        var response = Success(Body($$"""{"answers":[{{Score}},{{Predicate}},{{Choice}}]}"""), TestSets.AllKinds());

        AssertAllKinds(response);
    }

    [Fact]
    public void NameBeforeType_AndTypeBeforeName_AreBothRead()
    {
        var body = Body("""
            {"answers":[
              {"name":"refund","probability":0.92,"type":"predicate"},
              {"type":"choice","choice":"billing","probabilities":[{"value":"billing","probability":0.8},{"value":"other","probability":0.2}],"confidence":0.6,"name":"topic"},
            """ + Score + "]}");

        AssertAllKinds(Success(body, TestSets.AllKinds()));
    }

    [Fact]
    public void UnknownFields_AreSkipped()
    {
        var body = Body("""
            {"object":"decision","answers":[
              {"type":"predicate","extra":{"a":[1,2]},"name":"refund","probability":0.92,"reason":"x"},
              {"type":"choice","name":"topic","choice":"billing","probabilities":[{"value":"billing","note":1,"probability":0.8},{"value":"other","probability":0.2}],"confidence":0.6,"more":null},
              {"type":"score","name":"urgency","score":1.1,"probabilities":[{"value":0,"label":"0","probability":0.1,"x":[]},{"value":1,"label":"1","probability":0.7},{"value":2,"label":"2","probability":0.2}],"confidence":0.5}],
             "created":1760140800}
            """);

        AssertAllKinds(Success(body, TestSets.AllKinds()));
    }

    [Fact]
    public void AnUnknownName_IsSkipped()
    {
        var body = Body($$"""{"answers":[{{Predicate}},{"type":"predicate","name":"not_asked","probability":0.5},{{Choice}},{{Score}}]}""");

        AssertAllKinds(Success(body, TestSets.AllKinds()));
    }

    [Fact]
    public void AMissingAnswer_IsInvalidResponse()
    {
        var error = Failure(Body($$"""{"answers":[{{Predicate}},{{Choice}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(200, error.StatusCode);
        Assert.EndsWith("The response has no answer for 'urgency'.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"choice","name":"refund","probability":0.92}""")]
    [InlineData("""{"type":"score","name":"refund","probability":0.92}""")]
    [InlineData("""{"type":"something","name":"refund","probability":0.92}""")]
    public void AWrongTypeForTheQuestionsKind_IsInvalidResponse(string answer)
    {
        var error = Failure(Body($$"""{"answers":[{{answer}},{{Choice}},{{Score}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(200, error.StatusCode);
    }

    [Fact]
    public void AChoiceNotAmongTheOptions_IsInvalidResponse()
    {
        var choice = Choice.Replace("\"choice\":\"billing\"", "\"choice\":\"sales\"", StringComparison.Ordinal);

        var error = Failure(Body($$"""{"answers":[{{Predicate}},{{choice}},{{Score}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.EndsWith("'sales' is not one of the options.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"predicate","name":"refund"}""")]
    [InlineData("""{"type":"predicate","probability":0.92}""")]
    [InlineData("""{"name":"refund","probability":0.92}""")]
    [InlineData("""{"type":"predicate","name":5,"probability":0.92}""")]
    [InlineData("""{"type":"predicate","name":"refund","probability":"high"}""")]
    [InlineData("[]")]
    public void AMalformedAnswer_IsInvalidResponse(string answer)
    {
        var error = Failure(Body($$"""{"answers":[{{answer}},{{Choice}},{{Score}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
    }

    [Theory]
    [InlineData("""{"type":"score","name":"urgency","score":1.1,"probabilities":[{"value":3,"label":"3","probability":0.1}],"confidence":0.5}""")]
    [InlineData("""{"type":"score","name":"urgency","score":1.1,"probabilities":[{"value":"0","label":"0","probability":0.1}],"confidence":0.5}""")]
    [InlineData("""{"type":"score","name":"urgency","score":1.1,"probabilities":[{"label":"0","probability":0.1}],"confidence":0.5}""")]
    [InlineData("""{"type":"score","name":"urgency","probabilities":[],"confidence":0.5}""")]
    [InlineData("""{"type":"choice","name":"urgency","choice":"0","probabilities":[],"confidence":0.5}""")]
    public void AMalformedScore_IsInvalidResponse(string answer)
    {
        var error = Failure(Body($$"""{"answers":[{{Predicate}},{{Choice}},{{answer}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
    }

    [Theory]
    [InlineData("""{"type":"predicate","name":"refund","probability":1.2}""", "", "")]
    [InlineData("""{"type":"predicate","name":"refund","probability":-0.01}""", "", "")]
    [InlineData(Predicate, "\"probability\":0.8", "\"probability\":1.8")]
    [InlineData(Predicate, "\"confidence\":0.6", "\"confidence\":-0.6")]
    [InlineData(Predicate, "\"probability\":0.7", "\"probability\":7")]
    [InlineData(Predicate, "\"confidence\":0.5", "\"confidence\":1.5")]
    [InlineData(Predicate, "\"score\":1.1", "\"score\":2.1")]
    [InlineData(Predicate, "\"score\":1.1", "\"score\":-1")]
    public void AValueOutsideItsRange_IsInvalidResponse(string predicate, string find, string replace)
    {
        var choice = find.Length == 0 ? Choice : Choice.Replace(find, replace, StringComparison.Ordinal);
        var score = find.Length == 0 ? Score : Score.Replace(find, replace, StringComparison.Ordinal);
        Assert.True(find.Length == 0 || !string.Equals(choice, Choice, StringComparison.Ordinal) || !string.Equals(score, Score, StringComparison.Ordinal));

        var error = Failure(Body($$"""{"answers":[{{predicate}},{{choice}},{{score}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.Equal(200, error.StatusCode);
    }

    [Theory]
    [InlineData("""{"answers":{}}""")]
    [InlineData("""{"answers":null}""")]
    [InlineData("""{"model":"gpt-6-luna"}""")]
    [InlineData("""[]""")]
    public void AnEnvelopeWithoutAnAnswersArray_IsInvalidResponse(string body)
        => Assert.Equal(DecisionErrorKind.InvalidResponse, Failure(Body(body), TestSets.AllKinds()).Kind);

    [Fact]
    public void OneRefusal_IsRefusedWithStatus200()
    {
        var error = Failure(Fixture("refusal.json"), TestSets.WithOtherQuestion());

        Assert.Equal(DecisionErrorKind.Refused, error.Kind);
        Assert.Equal(200, error.StatusCode);
        Assert.Equal("The model refused to answer: other_question.", error.Message);
        Assert.Equal([new QuestionFailure("refusal", "other_question", "The model refused to answer 'other_question'.")], error.Failures);
    }

    [Fact]
    public void TwoRefusals_AreBothFailuresInAnswerOrder()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("a", "x"), QuestionDefinition.Noul("b", "y"), QuestionDefinition.Noul("c", "z"));
        var body = Body("""
            {"answers":[{"type":"refusal","name":"a"},{"type":"predicate","name":"c","probability":0.1},{"name":"b","type":"refusal"}]}
            """);

        var error = Failure(body, definition);

        Assert.Equal(DecisionErrorKind.Refused, error.Kind);
        Assert.Equal("The model refused to answer: a, b.", error.Message);
        Assert.Equal(
            [
                new QuestionFailure("refusal", "a", "The model refused to answer 'a'."),
                new QuestionFailure("refusal", "b", "The model refused to answer 'b'."),
            ],
            error.Failures);
    }

    [Fact]
    public void ARefusal_ComesBeforeTheMissingAnswerCheck()
    {
        var body = Body("""{"answers":[{"type":"refusal","name":"other_question"}]}""");

        var error = Failure(body, TestSets.WithOtherQuestion());

        Assert.Equal(DecisionErrorKind.Refused, error.Kind);
        Assert.Equal(["other_question"], error.Failures.Select(f => f.QuestionKey));
    }

    [Fact]
    public void ARefusalForAnUnknownName_IsSkipped()
    {
        var body = Body($$"""{"answers":[{{Predicate}},{"type":"refusal","name":"not_asked"},{{Choice}},{{Score}}]}""");

        AssertAllKinds(Success(body, TestSets.AllKinds()));
    }

    [Fact]
    public void ChoiceProbabilities_InADifferentOrderFromTheOptions_MapByValue()
    {
        var choice = """{"type":"choice","name":"topic","choice":"other","probabilities":[{"value":"other","probability":0.7},{"value":"billing","probability":0.3}],"confidence":0.4}""";

        var response = Success(Body($$"""{"answers":[{{Predicate}},{{choice}},{{Score}}]}"""), TestSets.AllKinds());

        var topic = response.Answers[1];
        Assert.Equal(1, topic.ChosenIndex);
        Assert.Equal([0.3, 0.7], topic.Probabilities.ToArray());
    }

    [Theory]
    [InlineData("""{"type":"predicate","name":"refund","probability":0.1}""")]
    [InlineData("""{"type":"refusal","name":"refund"}""")]
    public void ASecondAnswerForAnAnsweredQuestion_IsInvalidResponse(string second)
    {
        var error = Failure(Body($$"""{"answers":[{{Predicate}},{{Choice}},{{second}},{{Score}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.EndsWith("The response has more than one answer for 'refund'.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"predicate","name":"refund","probability":0.1}""")]
    [InlineData("""{"type":"refusal","name":"refund"}""")]
    public void ASecondAnswerForARefusedQuestion_IsInvalidResponse(string second)
    {
        var error = Failure(Body($$"""{"answers":[{"type":"refusal","name":"refund"},{{second}},{{Choice}},{{Score}}]}"""), TestSets.AllKinds());

        Assert.Equal(DecisionErrorKind.InvalidResponse, error.Kind);
        Assert.EndsWith("The response has more than one answer for 'refund'.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalException_ThatNamesNoQuestion_HasANeutralMessage()
    {
        Assert.Equal("The model refused to answer.", new RefusalException().Message);
        Assert.Empty(new RefusalException().Keys);
    }

    [Fact]
    public void Usage_InputTokens_IsRead()
    {
        var response = Success(Body($$$"""{"answers":[{{{Predicate}}},{{{Choice}}},{{{Score}}}],"usage":{"input_tokens":296}}"""), TestSets.AllKinds());

        Assert.Equal(296, response.InputTokens);
        Assert.Null(response.OutputTokens);
        Assert.Equal(296, response.Usage!.InputTokens);
        Assert.Null(response.Usage.OutputTokens);
        Assert.Null(response.Usage.Cost);
    }

    [Fact]
    public void Usage_WithOnlyACost_IsTheUsage()
    {
        var response = Success(Body($$$"""{"answers":[{{{Predicate}}},{{{Choice}}},{{{Score}}}],"usage":{"cost":0.5}}"""), TestSets.AllKinds());

        Assert.Null(response.Usage!.InputTokens);
        Assert.Null(response.Usage.OutputTokens);
        Assert.Equal(0.5, response.Usage.Cost);
    }

    [Fact]
    public void Usage_WithBothCounts_IsTheUsage()
    {
        var response = Success(
            Body($$$"""{"answers":[{{{Predicate}}},{{{Choice}}},{{{Score}}}],"usage":{"input_tokens":296,"output_tokens":0}}"""),
            TestSets.AllKinds());

        Assert.Equal(296, response.Usage!.InputTokens);
        Assert.Equal(0, response.Usage.OutputTokens);
        Assert.Null(response.Usage.Cost);
    }

    [Fact]
    public void Usage_WithBothCountsAndACost_ReadsAllThree()
    {
        var response = Success(
            Body($$$"""{"answers":[{{{Predicate}}},{{{Choice}}},{{{Score}}}],"usage":{"input_tokens":296,"output_tokens":0,"cost":0.0000296}}"""),
            TestSets.AllKinds());

        Assert.Equal(296, response.Usage!.InputTokens);
        Assert.Equal(0, response.Usage.OutputTokens);
        Assert.Equal(0.0000296, response.Usage.Cost);
    }

    [Fact]
    public void Usage_Absent_IsTheEmptyUsage()
    {
        var response = Success(Fixture("success-all-kinds.json"), TestSets.AllKinds());

        Assert.Null(response.Usage);
        Assert.Null(response.InputTokens);
        Assert.Null(response.OutputTokens);
        Assert.Null(response.Cost);
    }

    [Fact]
    public void ModelAndId_AreReadWhenPresent()
    {
        var response = Success(
            Body($$"""{"id":"dec_123","model":"gpt-6-luna-2026-10-01","answers":[{{Predicate}},{{Choice}},{{Score}}]}"""),
            TestSets.AllKinds());

        Assert.Equal("gpt-6-luna-2026-10-01", response.Model);
        Assert.Equal("dec_123", response.Id);
        Assert.Null(response.UpstreamProvider);
    }

    [Fact]
    public void ModelAndId_AreNullWhenAbsentOrNotStrings()
    {
        var response = Success(Body($$"""{"id":5,"answers":[{{Predicate}},{{Choice}},{{Score}}]}"""), TestSets.AllKinds());

        Assert.Null(response.Model);
        Assert.Null(response.Id);
    }

    [Fact]
    public void ALeadingByteOrderMark_IsSkipped()
    {
        byte[] body = [0xEF, 0xBB, 0xBF, .. Fixture("success-all-kinds.json")];

        AssertAllKinds(Success(body, TestSets.AllKinds()));
    }

    [Fact]
    public void TrailingContent_IsInvalidResponse()
    {
        byte[] body = [.. Fixture("success-all-kinds.json"), .. "x"u8];

        Assert.Equal(DecisionErrorKind.InvalidResponse, Failure(body, TestSets.AllKinds()).Kind);
    }

    private static void AssertAllKinds(DecisionResponse response)
    {
        var refund = response.Answers[0];
        Assert.Equal("refund", refund.Key);
        Assert.Equal(0.92, refund.Probability);

        var topic = response.Answers[1];
        Assert.Equal(0, topic.ChosenIndex);
        Assert.Equal(0.6, topic.Confidence);
        Assert.Equal([0.8, 0.2], topic.Probabilities.ToArray());

        var urgency = response.Answers[2];
        Assert.Equal(1, urgency.ChosenIndex);
        Assert.Equal(1.1, urgency.Expected);
        Assert.Equal(0.5, urgency.Confidence);
        Assert.Equal([0.1, 0.7, 0.2], urgency.Probabilities.ToArray());
    }

    private static DecisionResponse Success(byte[] body, QuestionSetDefinition definition)
    {
        var result = OpenAIDecisionsProtocol.Instance.ReadResponse(body, definition);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.ToString());
        return result.Value;
    }

    private static DecisionError Failure(byte[] body, QuestionSetDefinition definition)
    {
        var result = OpenAIDecisionsProtocol.Instance.ReadResponse(body, definition);
        Assert.False(result.IsSuccess);
        return result.Error;
    }

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
