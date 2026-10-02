using System.Net;
using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.Samples.Tests;

public sealed class SharedTests
{
    private const string Body = """{"model":"jev-1.13.0","answers":{},"usage":{"input_tokens":1,"output_tokens":1}}""";

    [Theory]
    [InlineData(new[] { "--replay" }, SampleMode.Replay)]
    [InlineData(new[] { "--live" }, SampleMode.Live)]
    [InlineData(new[] { "--record" }, SampleMode.Record)]
    public void Parse_ReadsTheMode(string[] args, SampleMode expected)
        => Assert.Equal(expected, SampleModes.Parse(args));

    [Fact]
    public void Parse_NoArguments_IsReplay()
        => Assert.Equal(SampleMode.Replay, SampleModes.Parse(Array.Empty<string>()));

    [Fact]
    public void Parse_RejectsAnythingElse()
    {
        Assert.Throws<ArgumentException>(() => SampleModes.Parse(["--fast"]));
        Assert.Throws<ArgumentException>(() => SampleModes.Parse(["--live", "--record"]));
    }

    [Fact]
    public void RequestHash_IsTheLowercaseHexSha256()
        => Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            RequestHash.Of(Encoding.ASCII.GetBytes("abc")));

    [Fact]
    public async Task Replay_AnswersARecordedRequest()
    {
        var request = Encoding.UTF8.GetBytes("""{"q":1}""");
        var recordings = File(new RecordedResponse(RequestHash.Of(request), JsonElement.Parse(Body)));
        using var http = new HttpClient(new ReplayHandler(recordings, "ZeroAlloc.Jev.Samples.Example"));

        using var sent = new HttpRequestMessage(HttpMethod.Post, "https://example.test/v1") { Content = new ByteArrayContent(request) };
        using var response = await http.SendAsync(sent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(sent, response.RequestMessage);
        Assert.Equal(Body, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Replay_NamesTheReRecordCommand_ForAnUnknownRequest()
    {
        using var http = new HttpClient(new ReplayHandler(File(), "ZeroAlloc.Jev.Samples.Example"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => http.PostAsync(new Uri("https://example.test/v1"), new StringContent("{}")));

        Assert.Contains("dotnet run --project samples/ZeroAlloc.Jev.Samples.Example -- --record", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_KeepsSuccessfulBodiesOnly_AndNoHeaders()
    {
        var session = new RecordingSession();
        var inner = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        using var http = new HttpClient(new RecordingHandler(session) { InnerHandler = inner });
        var failed = Encoding.UTF8.GetBytes("""{"q":3}""");
        var request = Encoding.UTF8.GetBytes("""{"q":2}""");

        using (var first = new HttpRequestMessage(HttpMethod.Post, "https://example.test/v1") { Content = new ByteArrayContent(failed) })
        {
            first.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-secret");
            (await http.SendAsync(first)).Dispose();
        }

        using (var second = new HttpRequestMessage(HttpMethod.Post, "https://example.test/v1") { Content = new ByteArrayContent(request) })
        {
            second.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-secret");
            (await http.SendAsync(second)).Dispose();
        }

        var file = session.ToFile("OpenRouter", new DateOnly(2026, 10, 2));
        var path = Path.GetTempFileName();
        try
        {
            file.Save(path);
            var text = System.IO.File.ReadAllText(path);
            using var json = JsonDocument.Parse(text);

            Assert.Collection(file.Entries, entry => Assert.Equal(RequestHash.Of(request), entry.RequestHash));
            Assert.Equal(1, session.Count);
            Assert.Equal(RequestHash.Of(request), file.Entries[0].RequestHash);
            Assert.Equal("jev-1.13.0", file.Model);
            Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("entries")[0].GetProperty("responseBody").ValueKind);
            Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
            Assert.Equal(
                ["entries", "model", "provider", "recorded"],
                json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    // Guards Windows, where Environment.NewLine is CRLF; the serializer already writes LF elsewhere.
    public void Save_WritesLineFeedsOnly()
    {
        var path = Path.GetTempFileName();
        try
        {
            File(new RecordedResponse("abc123", JsonElement.Parse(Body))).Save(path);
            var text = System.IO.File.ReadAllText(path);

            Assert.DoesNotContain('\r', text);
            Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public async Task Save_StoresTheBodyAsJson_AndReplayKeepsItsExactText()
    {
        const string body = """{"model":"jev-1.13.0","answers":{"q":{"type":"noul","noul":0.5700000000000001}},"note":"someone's <safety> & \"quoted\""}""";
        var request = Encoding.UTF8.GetBytes("""{"q":4}""");
        var path = Path.GetTempFileName();
        try
        {
            File(new RecordedResponse(RequestHash.Of(request), JsonElement.Parse(body))).Save(path);
            var text = System.IO.File.ReadAllText(path);
            using var http = new HttpClient(new ReplayHandler(RecordingsFile.Load(path), "ZeroAlloc.Jev.Samples.Example"));
            using var sent = new HttpRequestMessage(HttpMethod.Post, "https://example.test/v1") { Content = new ByteArrayContent(request) };
            using var response = await http.SendAsync(sent);
            using var replayed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Nested JSON that reads as written: the number keeps its text, and quotes and apostrophes are not escaped as \u00XX.
            Assert.Contains("\"noul\": 0.5700000000000001", text, StringComparison.Ordinal);
            Assert.Contains("someone's <safety> & ", text, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\u0022", text, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\u0027", text, StringComparison.Ordinal);
            Assert.Equal("0.5700000000000001", replayed.RootElement.GetProperty("answers").GetProperty("q").GetProperty("noul").GetRawText());
            Assert.Equal("someone's <safety> & \"quoted\"", replayed.RootElement.GetProperty("note").GetString());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ToFile_RejectsResponsesFromDifferentModels()
    {
        var session = new RecordingSession();
        session.Add("a", """{"model":"jev-1.13.0"}""");
        session.Add("b", """{"model":"jev-1.14.0"}""");

        var exception = Assert.Throws<InvalidOperationException>(() => session.ToFile("OpenRouter", new DateOnly(2026, 10, 2)));

        Assert.Contains("jev-1.13.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("jev-1.14.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToFile_ReadsTheModelWhenEveryResponseAgrees()
    {
        var session = new RecordingSession();
        session.Add("a", """{"model":"jev-1.13.0"}""");
        session.Add("b", """{"model":"jev-1.13.0"}""");

        Assert.Equal("jev-1.13.0", session.ToFile("OpenRouter", new DateOnly(2026, 10, 2)).Model);
    }

    [Fact]
    public void Replay_RejectsADuplicateRequestHash_NamingItAndTheSample()
    {
        var recordings = File(new RecordedResponse("abc123", JsonElement.Parse(Body)), new RecordedResponse("abc123", JsonElement.Parse(Body)));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ReplayHandler(recordings, "ZeroAlloc.Jev.Samples.Example"));

        Assert.Contains("abc123", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ZeroAlloc.Jev.Samples.Example", exception.Message, StringComparison.Ordinal);
    }

    private static RecordingsFile File(params RecordedResponse[] entries)
        => new() { Provider = "OpenRouter", Model = "jev-1.13.0", Recorded = "2026-10-02", Entries = [.. entries] };

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responses[_next++]);
    }
}
