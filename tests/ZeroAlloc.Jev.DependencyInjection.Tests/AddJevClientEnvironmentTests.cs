using Microsoft.Extensions.DependencyInjection;
using static ZeroAlloc.Jev.DependencyInjection.Tests.Registrations;

namespace ZeroAlloc.Jev.DependencyInjection.Tests;

/// <summary><c>AddJevClient</c> with the environment variables the core reads, set for one test at a time.</summary>
[Collection(ProcessEnvironment.Name)]
public sealed class AddJevClientEnvironmentTests
{
    [Fact]
    public async Task TypeSafeBaseUrl_IsTheFactoryClientsBaseAddress()
    {
        using var environment = new EnvironmentVariables(("TYPESAFE_BASE_URL", "http://env.local/jev"));
        var handler = Noul();
        var services = new ServiceCollection();
        services.AddJevClient(options => options.ApiKey = "di-key").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Request());

        Assert.Equal(new Uri("http://env.local/jev/v1/systemone"), OnlyRequest(handler).Uri);
    }

    [Fact]
    public void MissingApiKey_Throws_OnTheFirstResolve()
    {
        using var environment = new EnvironmentVariables(("TYPESAFE_API_KEY", null));
        var services = new ServiceCollection();
        services.AddJevClient(options => options.BaseAddress = new Uri("http://default.local/"));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IJevClient>());

        Assert.Equal(
            "No API key is configured. Set JevClientOptions.ApiKey or the TYPESAFE_API_KEY environment variable.",
            exception.Message);
    }
}
