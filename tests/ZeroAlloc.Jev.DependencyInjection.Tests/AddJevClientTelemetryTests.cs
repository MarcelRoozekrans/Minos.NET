using Microsoft.Extensions.DependencyInjection;
using static ZeroAlloc.Jev.DependencyInjection.Tests.Registrations;

namespace ZeroAlloc.Jev.DependencyInjection.Tests;

/// <summary>A resolved client's telemetry, which is always on, with no setup beyond a listener on the <c>ZeroAlloc.Jev</c> source.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class AddJevClientTelemetryTests
{
    [Fact]
    public async Task ResolvedClient_EmitsAJevSpan_WithNoTelemetrySetup()
    {
        using var capture = new TelemetryCapture();
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddJevClient(Options("http://default.local/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("evaluate", capture.StartTags().Tag("jev.operation"));
        Assert.Equal("ZeroAlloc.Jev", capture.Span().Source.Name);
    }
}
