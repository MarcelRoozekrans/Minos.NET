using ZeroAlloc.Jev.Telemetry;

namespace ZeroAlloc.Jev.Tests;

/// <summary><see cref="Evaluated{T}"/>'s deferred reads from the response bytes.</summary>
public sealed class EvaluatedTests
{
    private const string Choice = TelemetryBodies.ChoiceJson;
    private const string Score = TelemetryBodies.ScoreJson;
    private const string Noul = TelemetryBodies.NoulJson;

    [Theory]
    [InlineData("""{"model":"m-1","usage":{"input_tokens":7,"output_tokens":3},"answers":{"a":""" + Choice + "}}")]
    [InlineData("""{"answers":{"a":""" + Choice + """},"model":"m-1","usage":{"input_tokens":7,"output_tokens":3}}""")]
    [InlineData("""{"model":"m-1","answers":{"a":""" + Choice + """},"usage":{"output_tokens":3,"input_tokens":7}}""")]
    public void ModelAndUsage_AreReadWhereverTheyAre(string json)
    {
        using var evaluated = Evaluate(json);

        Assert.Equal("m-1", evaluated.ResponseModel);
        Assert.Equal(7, evaluated.InputTokens);
        Assert.Equal(3, evaluated.OutputTokens);
        Assert.Equal([0.81], Read(evaluated.Confidences));
    }

    [Fact]
    public void MissingUsage_IsNull()
    {
        using var evaluated = Evaluate(Fixture.Text("response-missing-usage.json"));

        Assert.Equal("jev-1.13.0", evaluated.ResponseModel);
        Assert.Null(evaluated.InputTokens);
        Assert.Null(evaluated.OutputTokens);
    }

    [Fact]
    public void NonNumericOrNullUsage_IsNull()
    {
        using var evaluated = Evaluate("""{"usage":{"input_tokens":"7","output_tokens":null},"answers":{}}""");

        Assert.Null(evaluated.ResponseModel);
        Assert.Null(evaluated.InputTokens);
        Assert.Null(evaluated.OutputTokens);
    }

    [Fact]
    public void NoulOnlySet_HasNoConfidences()
    {
        using var evaluated = Evaluate(Fixture.Text("response-noul.json"));

        Assert.Empty(Read(evaluated.Confidences));
    }

    [Fact]
    public void Confidences_AreEachChoiceAndScore_InResponseOrder_SkippingNoul()
    {
        using var evaluated = Evaluate("""{"answers":{"a":""" + Score + ""","b":""" + Noul + ""","c":""" + Choice + "}}");

        Assert.Equal([0.92, 0.81], Read(evaluated.Confidences));
    }

    [Fact]
    public void Confidences_IgnoreAConfidenceKeyNestedInsideAnAnswer()
    {
        const string Nested = """{"type":"choice","choice":"confidence","probabilities":{"confidence":0.3,"other":0.7},"confidence":0.6}""";
        using var evaluated = Evaluate("""{"answers":{"a":""" + Nested + "}}");

        Assert.Equal([0.6], Read(evaluated.Confidences));
    }

    [Fact]
    public void Confidences_CanBeEnumeratedTwice()
    {
        using var evaluated = Evaluate("""{"answers":{"a":""" + Choice + ""","b":""" + Score + "}}");

        Assert.Equal(Read(evaluated.Confidences), Read(evaluated.Confidences));
    }

    [Fact]
    public void LeadingBom_IsSkipped()
    {
        using var evaluated = Evaluate("\uFEFF" + """{"model":"m-1","answers":{"a":""" + Choice + """},"usage":{"input_tokens":7,"output_tokens":3}}""");

        Assert.Equal("m-1", evaluated.ResponseModel);
        Assert.Equal(7, evaluated.InputTokens);
        Assert.Equal([0.81], Read(evaluated.Confidences));
    }

    [Fact]
    public void ResponseModel_IsReadFromTheBytesOnEveryAccess()
    {
        using var evaluated = Evaluate("""{"model":"m-1","answers":{}}""");

        // Rule 2: no cache, so each read builds its own string from the response bytes.
        var first = evaluated.ResponseModel;
        var second = evaluated.ResponseModel;

        Assert.Equal("m-1", first);
        Assert.Equal("m-1", second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Dispose_ReturnsTheResponse_AndAReadAfterwardsThrows()
    {
        var evaluated = Evaluate(Fixture.Text("response-noul.json"));
        Assert.Equal("jev-1.13.0", evaluated.ResponseModel);

        evaluated.Dispose();

        Assert.Throws<ObjectDisposedException>(() => evaluated.ResponseModel);
        Assert.Throws<ObjectDisposedException>(() => evaluated.InputTokens);
        Assert.Throws<ObjectDisposedException>(() => evaluated.Confidences);
    }

    [Fact]
    public async Task Unwrap_OfASynchronousSuccess_DisposesAndReturnsTheAnswers()
    {
        var evaluated = Evaluate(Fixture.Text("response-noul.json"));

        var call = Evaluated.Unwrap(new ValueTask<ZeroAlloc.Results.Result<Evaluated<string>, JevError>>(
            ZeroAlloc.Results.Result<Evaluated<string>, JevError>.Success(evaluated)));

        Assert.True(call.IsCompletedSuccessfully);
        Assert.Equal("answers", (await call).Value);
        Assert.Throws<ObjectDisposedException>(() => evaluated.ResponseModel);
    }

    [Fact]
    public void UsageOutsideTheInt32Range_IsNull()
    {
        using var evaluated = Evaluate("""{"usage":{"input_tokens":3000000000,"output_tokens":3000000000},"answers":{}}""");

        Assert.Null(evaluated.InputTokens);
        Assert.Null(evaluated.OutputTokens);
    }

    [Fact]
    public async Task Unwrap_OfAnAsynchronousSuccess_DisposesAndReturnsTheAnswers()
    {
        var evaluated = Evaluate(Fixture.Text("response-noul.json"));
        var source = new TaskCompletionSource<ZeroAlloc.Results.Result<Evaluated<string>, JevError>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var call = Evaluated.Unwrap(new ValueTask<ZeroAlloc.Results.Result<Evaluated<string>, JevError>>(source.Task));

        Assert.False(call.IsCompleted);
        Assert.Equal("jev-1.13.0", evaluated.ResponseModel);

        source.SetResult(ZeroAlloc.Results.Result<Evaluated<string>, JevError>.Success(evaluated));

        Assert.Equal("answers", (await call).Value);
        Assert.Throws<ObjectDisposedException>(() => evaluated.ResponseModel);
    }

    [Fact]
    public async Task Unwrap_OfAFailure_KeepsTheError()
    {
        var error = new JevError(JevErrorKind.Server, "boom");

        var result = await Evaluated.Unwrap(new ValueTask<ZeroAlloc.Results.Result<Evaluated<string>, JevError>>(
            ZeroAlloc.Results.Result<Evaluated<string>, JevError>.Failure(error)));

        Assert.Same(error, result.Error);
    }

    private static Evaluated<string> Evaluate(string json) => new("answers", TelemetryBodies.RawJsonOf(json));

    private static List<double> Read(ConfidenceValues values)
    {
        var read = new List<double>();
        foreach (var value in values)
        {
            read.Add(value);
        }

        return read;
    }
}
