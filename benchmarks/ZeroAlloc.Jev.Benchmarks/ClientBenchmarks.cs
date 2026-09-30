using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;
using ZeroAlloc.Jev.Shared;

namespace ZeroAlloc.Jev.Benchmarks;

/// <summary>Benchmarks <see cref="JevClient"/>'s hot paths against an in-memory <see cref="HttpMessageHandler"/>,
/// through the public API only, since <c>JevJsonContext</c> is internal.</summary>
[MemoryDiagnoser]
public class ClientBenchmarks
{
    private const string NoulResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string ModelsResponseJson = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";
    internal const string TriageResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    private HttpClient _evaluateHttp = null!;
    private HttpClient _listModelsHttp = null!;
    private HttpClient _typedEvaluateHttp = null!;
    private HttpClient _typedEvaluateNoulHttp = null!;
    private JevClient _evaluateClient = null!;
    private JevClient _listModelsClient = null!;
    private JevClient _typedEvaluateClient = null!;
    private JevClient _typedEvaluateNoulClient = null!;
    private HttpClient _evaluateLoggedHttp = null!;
    private HttpClient _typedEvaluateLoggedHttp = null!;
    private JevClient _evaluateLoggedClient = null!;
    private JevClient _typedEvaluateLoggedClient = null!;
    private HttpClient _evaluateYieldingHttp = null!;
    private HttpClient _evaluateYieldingLoggedHttp = null!;
    private JevClient _evaluateYieldingClient = null!;
    private JevClient _evaluateYieldingLoggedClient = null!;
    private SystemOneRequest _request = null!;
    private string _typedState = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_evaluateHttp, _evaluateClient) = CreateClient(NoulResponseJson);
        (_listModelsHttp, _listModelsClient) = CreateClient(ModelsResponseJson);
        (_typedEvaluateHttp, _typedEvaluateClient) = CreateClient(TriageResponseJson);
        (_typedEvaluateNoulHttp, _typedEvaluateNoulClient) = CreateClient(NoulResponseJson);
        (_evaluateLoggedHttp, _evaluateLoggedClient) = CreateClient(NoulResponseJson, DiscardingLoggerFactory.Instance);
        (_typedEvaluateLoggedHttp, _typedEvaluateLoggedClient) = CreateClient(TriageResponseJson, DiscardingLoggerFactory.Instance);
        (_evaluateYieldingHttp, _evaluateYieldingClient) = CreateClient(new YieldingHandler(HttpStatusCode.OK, NoulResponseJson), loggerFactory: null);
        (_evaluateYieldingLoggedHttp, _evaluateYieldingLoggedClient) = CreateClient(new YieldingHandler(HttpStatusCode.OK, NoulResponseJson), DiscardingLoggerFactory.Instance);
        _request = new SystemOneRequest
        {
            State = "Help! My payouts have been failing for 3 days.",
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        };
        _typedState = "Help! My payouts have been failing for 3 days.";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _evaluateClient.Dispose();
        _listModelsClient.Dispose();
        _typedEvaluateClient.Dispose();
        _typedEvaluateNoulClient.Dispose();
        _evaluateHttp.Dispose();
        _listModelsHttp.Dispose();
        _typedEvaluateHttp.Dispose();
        _typedEvaluateNoulHttp.Dispose();
        _evaluateLoggedClient.Dispose();
        _typedEvaluateLoggedClient.Dispose();
        _evaluateYieldingClient.Dispose();
        _evaluateYieldingLoggedClient.Dispose();
        _evaluateLoggedHttp.Dispose();
        _typedEvaluateLoggedHttp.Dispose();
        _evaluateYieldingHttp.Dispose();
        _evaluateYieldingLoggedHttp.Dispose();
    }

    /// <summary><see cref="JevClient.EvaluateAsync"/> over a fixed Noul response.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync() => _evaluateClient.EvaluateAsync(_request);

    /// <summary><see cref="JevClient.ListModelsAsync"/> over a fixed models response.</summary>
    [Benchmark]
    public ValueTask<Result<ModelList, JevError>> ListModelsAsync() => _listModelsClient.ListModelsAsync();

    /// <summary><see cref="JevClient.EvaluateAsync{T}(string)"/> over a fixed triage (three-answer) response,
    /// through the raw, pooled-buffer path.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, JevError>> TypedEvaluateAsync() => _typedEvaluateClient.EvaluateAsync<BenchTriage>(_typedState);

    /// <summary><see cref="JevClient.EvaluateAsync{T}(string)"/> over the same fixed Noul (one-answer) response as
    /// <see cref="EvaluateAsync"/>, through the raw, pooled-buffer path, so the typed and untyped calls compare
    /// like for like.</summary>
    [Benchmark]
    public ValueTask<Result<BenchUrgency, JevError>> TypedEvaluateNoulAsync() => _typedEvaluateNoulClient.EvaluateAsync<BenchUrgency>(_typedState);

    /// <summary><see cref="EvaluateAsync"/> through a logger enabled at every level that discards everything.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateWithDiscardingLoggerAsync() => _evaluateLoggedClient.EvaluateAsync(_request);

    /// <summary><see cref="TypedEvaluateAsync"/> through a logger enabled at every level that discards everything.</summary>
    [Benchmark]
    public ValueTask<Result<BenchTriage, JevError>> TypedEvaluateWithDiscardingLoggerAsync()
        => _typedEvaluateLoggedClient.EvaluateAsync<BenchTriage>(_typedState);

    /// <summary><see cref="EvaluateAsync"/> over a handler that completes asynchronously, with no logger.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateYieldingAsync() => _evaluateYieldingClient.EvaluateAsync(_request);

    /// <summary><see cref="EvaluateYieldingAsync"/> through a logger enabled at every level that discards everything,
    /// so the logging wrappers' async state machines run.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateYieldingWithDiscardingLoggerAsync() => _evaluateYieldingLoggedClient.EvaluateAsync(_request);

    internal static (HttpClient Http, JevClient Client) CreateClient(string responseJson)
        => CreateClient(responseJson, loggerFactory: null);

    internal static (HttpClient Http, JevClient Client) CreateClient(string responseJson, ILoggerFactory? loggerFactory)
        => CreateClient(new CannedHandler(HttpStatusCode.OK, responseJson), loggerFactory);

    private static (HttpClient Http, JevClient Client) CreateClient(HttpMessageHandler handler, ILoggerFactory? loggerFactory)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        // Default retry options: the handler always returns 200, so no retry ever fires, and
        // the benchmark measures the resilience proxy's per-call overhead that users get by default.
        // A null factory builds exactly the client the two-argument constructor builds.
        var client = new JevClient(http, new JevClientOptions { ApiKey = "bench" }, loggerFactory);
        return (http, client);
    }

    /// <summary>Answers every request with one canned response, so the benchmark needs no network.</summary>
    internal sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
    }

    /// <summary>Answers like <see cref="CannedHandler"/> but yields first, so the call genuinely completes
    /// asynchronously and the logging wrappers' state machines are measured.</summary>
    internal sealed class YieldingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }
}
