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
        var recordings = File(new RecordedResponse(RequestHash.Of(request), Body));
        using var http = new HttpClient(new ReplayHandler(recordings, "ZeroAlloc.Jev.Samples.Example"));

        using var response = await http.PostAsync(new Uri("https://example.test/v1"), new ByteArrayContent(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static RecordingsFile File(params RecordedResponse[] entries)
        => new() { Provider = "OpenRouter", Model = "jev-1.13.0", Recorded = "2026-10-02", Entries = [.. entries] };

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responses[_next++]);
    }
}
