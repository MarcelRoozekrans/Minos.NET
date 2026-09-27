using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Benchmarks;

/// <summary>Benchmarks <see cref="JevClient"/>'s hot paths against an in-memory <see cref="HttpMessageHandler"/>,
/// through the public API only, since <c>JevJsonContext</c> is internal.</summary>
[MemoryDiagnoser]
public class ClientBenchmarks
{
    private const string NoulResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string ModelsResponseJson = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";

    private HttpClient _evaluateHttp = null!;
    private HttpClient _listModelsHttp = null!;
    private JevClient _evaluateClient = null!;
    private JevClient _listModelsClient = null!;
    private SystemOneRequest _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_evaluateHttp, _evaluateClient) = CreateClient(NoulResponseJson);
        (_listModelsHttp, _listModelsClient) = CreateClient(ModelsResponseJson);
        _request = new SystemOneRequest
        {
            State = "Help! My payouts have been failing for 3 days.",
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _evaluateClient.Dispose();
        _listModelsClient.Dispose();
        _evaluateHttp.Dispose();
        _listModelsHttp.Dispose();
    }

    /// <summary><see cref="JevClient.EvaluateAsync"/> over a fixed Noul response.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync() => _evaluateClient.EvaluateAsync(_request);

    /// <summary><see cref="JevClient.ListModelsAsync"/> over a fixed models response.</summary>
    [Benchmark]
    public ValueTask<Result<ModelList, JevError>> ListModelsAsync() => _listModelsClient.ListModelsAsync();

    private static (HttpClient Http, JevClient Client) CreateClient(string responseJson)
    {
        var handler = new CannedHandler(HttpStatusCode.OK, responseJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        var client = new JevClient(http, new JevClientOptions { ApiKey = "bench", MaxRetries = 0 });
        return (http, client);
    }

    /// <summary>Answers every request with one canned response, so the benchmark needs no network.</summary>
    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
    }
}
