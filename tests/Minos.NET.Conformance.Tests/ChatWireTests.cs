using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZeroAlloc.Results;
using static Minos.Conformance.Tests.Outcomes;

namespace Minos.Conformance.Tests;

/// <summary>
/// The chat-completions wire: one exchange per question, replay matched by body, record keeping every exchange, and
/// the checks reading what Minos read. A small test client stands in for the bridge: one POST per question, in parallel.
/// </summary>
public sealed class ChatWireTests : IDisposable
{
    private const string NoAnswerBody = "A chat completion carries no answer body; the answers are checked as Minos read them.";

    private static readonly ConformanceTarget ChatTarget = Target(ConformanceWire.ChatCompletions, "gpt-test", modelRule: null);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "minos-chat-wire-" + Guid.NewGuid().ToString("N"));

    public ChatWireTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Replay_ServesEachRequest_TheExchangeWhoseBodyMatches_InAnyOrder()
    {
        var fixture = new ConformanceFixture
        {
            Target = "chat",
            Source = ConformanceFixture.TestsSource,
            Exchanges = [Exchange("mixed", "is_urgent", 0, "[0.9]"), Exchange("mixed", "department", 2, "[0.7,0.3]")],
        };
        using var capture = new ExchangeCapture(new FixtureHandler(fixture, ConformanceWire.ChatCompletions));
        using var http = new HttpClient(capture);
        capture.Begin(ConformanceProbe.Mixed);

        // Sent in reverse order, with the properties in another order too: the bodies are compared as parsed JSON.
        var department = await Content(http, """{"options":2,"question":"department","model":"gpt-test"}""");
        var urgent = await Content(http, Body("is_urgent", 0));

        Assert.Equal("[0.7,0.3]", department);
        Assert.Equal("[0.9]", urgent);
        Assert.Equal(2, capture.End().Count);
    }

    [Fact]
    public async Task Replay_ABodyMatchingNoExchange_Throws_NamingTheProbe()
    {
        var fixture = new ConformanceFixture
        {
            Target = "chat",
            Source = ConformanceFixture.TestsSource,
            Exchanges = [Exchange("mixed", "is_urgent", 0, "[0.9]")],
        };
        using var capture = new ExchangeCapture(new FixtureHandler(fixture, ConformanceWire.ChatCompletions));
        using var http = new HttpClient(capture);
        capture.Begin(ConformanceProbe.Mixed);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Content(http, Body("frustration", 3)));

        Assert.Contains("mixed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("frustration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_AnExchangeIsServedOnce()
    {
        var fixture = new ConformanceFixture
        {
            Target = "chat",
            Source = ConformanceFixture.TestsSource,
            Exchanges = [Exchange("mixed", "is_urgent", 0, "[0.9]")],
        };
        using var capture = new ExchangeCapture(new FixtureHandler(fixture, ConformanceWire.ChatCompletions));
        using var http = new HttpClient(capture);
        capture.Begin(ConformanceProbe.Mixed);

        await Content(http, Body("is_urgent", 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Content(http, Body("is_urgent", 0)));
    }

    [Fact]
    public async Task Record_KeepsEveryExchange_OnePerQuestion()
    {
        var path = Path.Combine(_directory, "chat.json");

        var report = await Runner().RunAsync(Chat(), ConformanceMode.Record, path);

        var recorded = ConformanceFixture.Load(path);
        Assert.True(report.Failed == 0, report.ToMarkdown());
        Assert.Equal(3, recorded.FindAll("mixed").Count);
        Assert.Equal(
            ["department", "frustration", "is_urgent"],
            recorded.FindAll("mixed").Select(exchange => exchange.Request!.Value.GetProperty("question").GetString()).Order(StringComparer.Ordinal));
        Assert.Single(recorded.FindAll("bad-key"));

        // The invalid probe is answered with Validation before anything is sent, so on this wire it has no exchange.
        Assert.Empty(recorded.FindAll("invalid"));
    }

    [Fact]
    public async Task ARecordedChatFixture_Replays_WithRepeatedProbeIds_AndFailsNoCheck()
    {
        var path = Path.Combine(_directory, "chat.json");
        await Runner().RunAsync(Chat(), ConformanceMode.Record, path);

        var report = await new ConformanceRunner().RunAsync(Chat(), ConformanceMode.Replay, path);

        Assert.True(report.Failed == 0, report.ToMarkdown());
        Assert.DoesNotContain("mixed:D2", report.SkippedChecks);
        Assert.Contains("mixed:S1", report.SkippedChecks);
        Assert.DoesNotContain("invalid:E1", report.SkippedChecks);
    }

    [Fact]
    public void S1AndS2_Skip_WithTheReason()
    {
        var outcome = ChatAnswered(ConformanceProbe.Mixed, Mixed(0.95, 0, 1.05));

        var s1 = ConformanceChecks.S1(outcome, ConformanceWire.ChatCompletions);
        var s2 = ConformanceChecks.S2(outcome, ConformanceWire.ChatCompletions);

        Assert.Equal(CheckResult.Skip("S1", NoAnswerBody), s1);
        Assert.Equal(CheckResult.Skip("S2", NoAnswerBody), s2);
    }

    [Fact]
    public void TheDistributionChecks_PassWhatMinosRead()
    {
        var outcome = ChatAnswered(ConformanceProbe.Mixed, Mixed(0.95, 0, 1.05));

        Assert.All(
            ConformanceChecks.Run(outcome, ChatTarget).Where(check => check.Id is "D1" or "D2" or "D3" or "D4" or "M1" or "R1" or "R3"),
            check => Assert.True(check.Outcome == CheckOutcome.Pass, check.Id + ": " + check.Detail));
    }

    [Fact]
    public void D2_FailsAChoiceWhoseProbabilitiesSumTo0_9()
    {
        var outcome = ChatAnswered(ConformanceProbe.Choice, Choice(0, [0.5, 0.4]));

        var result = ConformanceChecks.D2(outcome, ConformanceWire.ChatCompletions);

        Assert.Equal(CheckResult.Failed("D2", "'department' sums to 0.9."), result);
    }

    [Fact]
    public void D3_FailsAScoreWhoseExpectedLevelIsOff()
    {
        var outcome = ChatAnswered(ConformanceProbe.Score, Score(1, 1.5, [0.0, 0.95, 0.05]));

        var result = ConformanceChecks.D3(outcome, ConformanceWire.ChatCompletions);

        Assert.Equal(CheckResult.Failed("D3", "'frustration' expects 1.5, but its probabilities give 1.05."), result);
    }

    [Fact]
    public void TheDistributionChecks_SkipWhenMinosReturnedAnError()
    {
        var outcome = new ProbeOutcome(ConformanceProbe.Mixed, [ChatExchange(ConformanceProbe.Mixed, 500, """{"error":{}}""")], null, new DecisionError(DecisionErrorKind.Server, "Down."));

        foreach (var id in new[] { "D1", "D2", "D3", "D4", "M1" })
        {
            var check = Assert.Single(ConformanceChecks.Run(outcome, ChatTarget), result => string.Equals(result.Id, id, StringComparison.Ordinal));
            Assert.Equal(CheckResult.Skip(id, "Minos returned an error; S3 reports it."), check);
        }
    }

    [Fact]
    public void E1_PassesAValidationErrorWithNothingSent()
    {
        var outcome = new ProbeOutcome(ConformanceProbe.Invalid, [], null, new DecisionError(DecisionErrorKind.Validation, "The request has no questions."));

        var result = ConformanceChecks.E1(outcome, ConformanceWire.ChatCompletions);

        Assert.Equal(CheckResult.Passed("E1", "No request sent; Minos returned Validation: The request has no questions."), result);
    }

    [Fact]
    public void E1_FailsWhenARequestWasSent()
    {
        var outcome = new ProbeOutcome(
            ConformanceProbe.Invalid, [ChatExchange(ConformanceProbe.Invalid, 400, """{"error":{}}""")], null, new DecisionError(DecisionErrorKind.Validation, "Bad."));

        var result = ConformanceChecks.E1(outcome, ConformanceWire.ChatCompletions);

        Assert.Equal(CheckResult.Failed("E1", "1 request was sent; a request with no questions is rejected before anything is sent."), result);
    }

    [Fact]
    public void E1_FailsAnotherErrorKind()
    {
        var outcome = new ProbeOutcome(ConformanceProbe.Invalid, [], null, new DecisionError(DecisionErrorKind.InvalidQuestions, "No."));

        Assert.Equal(
            CheckResult.Failed("E1", "Minos returned InvalidQuestions, not Validation."),
            ConformanceChecks.E1(outcome, ConformanceWire.ChatCompletions));
    }

    [Fact]
    public void R1_FailsWhenOneExchangeNamesAnotherModel()
    {
        var outcome = new ProbeOutcome(
            ConformanceProbe.Mixed,
            [ChatExchange(ConformanceProbe.Mixed, 200, Completion("gpt-test-1", "[0.9]")), ChatExchange(ConformanceProbe.Mixed, 200, Completion("gpt-test-2", "[0.9]"))],
            Mixed(0.95, 0, 1.05),
            null);

        var result = ConformanceChecks.R1(outcome, ChatTarget);

        Assert.Equal(CheckResult.Failed("R1", "A response names gpt-test-1, but DecisionResponse.Model is jev-1.13.0."), result);
    }

    [Fact]
    public void R3_ReadsTheTokenCountsOfEveryExchange()
    {
        var outcome = new ProbeOutcome(
            ConformanceProbe.Mixed,
            [
                ChatExchange(ConformanceProbe.Mixed, 200, Completion("jev-1.13.0", "[0.9]")),
                ChatExchange(ConformanceProbe.Mixed, 200, """{"model":"jev-1.13.0","choices":[],"usage":{"prompt_tokens":-1,"completion_tokens":1}}"""),
            ],
            Mixed(0.95, 0, 1.05),
            null);

        var result = ConformanceChecks.R3(outcome, ConformanceWire.ChatCompletions);

        Assert.Equal(CheckResult.Failed("R3", "Usage: prompt_tokens is -1."), result);
    }

    [Fact]
    public async Task OtherWires_StillRejectRepeatedProbeIds()
    {
        var fixture = ConformanceFixture.Load(Fixtures.Path("typesafe"));
        var path = Path.Combine(_directory, "typesafe.json");
        new ConformanceFixture
        {
            Target = fixture.Target,
            Source = fixture.Source,
            Exchanges = [.. fixture.Exchanges, fixture.Find("mixed")!],
        }.Save(path);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ConformanceRunner().RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Replay, path));

        Assert.Equal("The fixture names these probes more than once: mixed.", error.Message);
    }

    private static ConformanceRunner Runner() => new(static () => new ChatNetwork(), TimeProvider.System);

    private static ConformanceTarget Chat()
        => new("chat", ConformanceWire.ChatCompletions, "gpt-test", modelRule: null, static http => new ChatTestClient(http, "test-key"));

    private static ProbeOutcome ChatAnswered(ConformanceProbe probe, DecisionResponse response)
        => new(probe, [ChatExchange(probe, 200, Completion(response.Model!, "[0.9]"))], response, null);

    private static ConformanceExchange ChatExchange(ConformanceProbe probe, int status, string body)
        => ConformanceExchange.FromHttp(probe.Id, requestBody: null, status, "application/json", headers: null, body);

    private static ConformanceExchange Exchange(string probe, string question, int options, string content)
        => ConformanceExchange.FromHttp(probe, Body(question, options), 200, "application/json", null, Completion("gpt-test", content));

    private static string Body(string question, int options)
        => string.Create(CultureInfo.InvariantCulture, $$"""{"model":"gpt-test","question":"{{question}}","options":{{options}}}""");

    private static string Completion(string model, string content)
        => $$$"""{"id":"chatcmpl-1","object":"chat.completion","model":"{{{model}}}","choices":[{"index":0,"message":{"role":"assistant","content":"{{{content}}}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":40,"completion_tokens":1,"total_tokens":41}}""";

    private static async Task<string> Content(HttpClient http, string body)
    {
        using var response = await http.PostAsync(new Uri("https://chat.test/v1/chat/completions"), new StringContent(body, Encoding.UTF8, "application/json"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
    }

    /// <summary>Answers each question with a chat completion whose content is a JSON array of probabilities, and an invalid key with 401.</summary>
    private sealed class ChatNetwork : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (string.Equals(request.Headers.Authorization?.Parameter, ExchangeCapture.InvalidKey, StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid key."}}""");
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var options = body.RootElement.GetProperty("options").GetInt32();
            var content = options == 0
                ? "[0.9]"
                : "[0.7" + string.Concat(Enumerable.Repeat("," + (0.3 / (options - 1)).ToString("R", CultureInfo.InvariantCulture), options - 1)) + "]";
            return Json(HttpStatusCode.OK, Completion("gpt-test-1", content));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>A stand-in for the chat bridge: one POST per question, all in parallel, read into a <see cref="DecisionResponse"/>.</summary>
    private sealed class ChatTestClient(HttpClient http, string apiKey) : IDecisionClient
    {
        public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            var questions = request.Definition.Questions;
            if (questions.Count == 0)
            {
                return Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Validation, "The request has no questions."));
            }

            var replies = await Task.WhenAll(questions.Select(question => AskAsync(request.Model, question, cancellationToken)));
            foreach (var (_, _, error) in replies)
            {
                if (error is not null)
                {
                    return Result<DecisionResponse, DecisionError>.Failure(error);
                }
            }

            var answers = new List<QuestionAnswer>();
            for (var i = 0; i < questions.Count; i++)
            {
                answers.Add(Answer(questions[i].Kind, replies[i].Probabilities!));
            }

            return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, answers, replies[0].Model));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        private static QuestionAnswer Answer(QuestionKind kind, double[] p)
        {
            var picked = 0;
            var expected = 0.0;
            for (var level = 0; level < p.Length; level++)
            {
                picked = p[level] > p[picked] ? level : picked;
                expected += level * p[level];
            }

            return kind switch
            {
                QuestionKind.Noul => QuestionAnswer.Noul(p[0]),
                QuestionKind.Choice => QuestionAnswer.Choice(picked, p[picked], p),
                _ => QuestionAnswer.Score(picked, expected, p[picked], p),
            };
        }

        public void Dispose()
        {
        }

        private async Task<(double[]? Probabilities, string? Model, DecisionError? Error)> AskAsync(string? model, QuestionDefinition question, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://chat.test/v1/chat/completions"))
            {
                Content = new StringContent(
                    string.Create(CultureInfo.InvariantCulture, $$"""{"model":"{{model}}","question":"{{question.Key}}","options":{{question.Options.Count}}}"""),
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return (null, null, new DecisionError(DecisionErrorKind.Unauthorized, "The API returned HTTP 401.") { StatusCode = 401 });
            }

            using var body = JsonDocument.Parse(text);
            var content = body.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
            using var probabilities = JsonDocument.Parse(content);
            return ([.. probabilities.RootElement.EnumerateArray().Select(value => value.GetDouble())], body.RootElement.GetProperty("model").GetString(), null);
        }
    }
}
