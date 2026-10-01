using System.Net;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Benchmarks;

/// <summary>
/// <see cref="ClientBenchmarks.EvaluateAsync"/>'s call through a client <c>AddJevClient</c> registered, against the same
/// call on a hand-built client over an <see cref="HttpClient"/> that <see cref="JevClient.ConfigureHttpClient"/>
/// configured the same way. Both send the User-Agent, which <see cref="ClientBenchmarks"/>' borrowed clients do not.
/// </summary>
[MemoryDiagnoser]
public class DependencyInjectionBenchmarks : IDisposable
{
    private ServiceProvider _provider = null!;
    private IJevClient _resolvedClient = null!;
    private HttpClient _handBuiltHttp = null!;
    private JevClient _handBuiltClient = null!;
    private SystemOneRequest _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services
            .AddJevClient(options =>
            {
                options.ApiKey = "bench";
                options.BaseAddress = new Uri("https://example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new ClientBenchmarks.CannedHandler(HttpStatusCode.OK, ClientBenchmarks.NoulResponseJson));
        _provider = services.BuildServiceProvider();
        _resolvedClient = _provider.GetRequiredService<IJevClient>();

        _handBuiltHttp = new HttpClient(new ClientBenchmarks.CannedHandler(HttpStatusCode.OK, ClientBenchmarks.NoulResponseJson));
        JevClient.ConfigureHttpClient(_handBuiltHttp, new JevClientOptions { BaseAddress = new Uri("https://example.test/api/") });
        _handBuiltClient = new JevClient(_handBuiltHttp, new JevClientOptions { ApiKey = "bench" });

        _request = ClientBenchmarks.Request();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Disposes both clients and the container. BenchmarkDotNet may call it after <see cref="Cleanup"/>; each call is idempotent.</summary>
    public void Dispose()
    {
        // Null-conditional: Dispose can run when Setup never did.
        _handBuiltClient?.Dispose();
        _handBuiltHttp?.Dispose();
        _provider?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The call on a hand-built client over a configured <see cref="HttpClient"/>.</summary>
    [Benchmark(Baseline = true)]
    public ValueTask<Result<SystemOneResponse, JevError>> HandBuiltEvaluateAsync() => _handBuiltClient.EvaluateAsync(_request);

    /// <summary>The same call on the client the container resolves, over the factory's <see cref="HttpClient"/>.</summary>
    [Benchmark]
    public ValueTask<Result<SystemOneResponse, JevError>> ResolvedEvaluateAsync() => _resolvedClient.EvaluateAsync(_request);
}
