using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><c>AddJevClient</c> under Native AOT: a default and a keyed client, registered, resolved and evaluated.</summary>
internal static class DependencyInjectionChecks
{
    /// <summary>Registers the default client over a canned handler. The allocation gates register theirs the same way.</summary>
    public static void RegisterDefaultClient(IServiceCollection services)
        => services
            .AddJevClient(options =>
            {
                options.ApiKey = "smoke-key";
                options.BaseAddress = new Uri("https://example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));

    public static async Task DefaultAndKeyedClientsEvaluate()
    {
        var services = new ServiceCollection();
        RegisterDefaultClient(services);
        services
            .AddJevClient("openrouter", options =>
            {
                options.Provider = JevProvider.OpenRouter;
                options.ApiKey = "smoke-openrouter-key";
                options.BaseAddress = new Uri("https://openrouter.example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IJevClient>();
        var keyed = provider.GetRequiredKeyedService<IJevClient>("openrouter");
        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);
        var keyedResult = await keyed.EvaluateAsync(Program.Request()).ConfigureAwait(false);

        Program.Check(
            ReferenceEquals(client, provider.GetRequiredService<IJevClient>()) && !ReferenceEquals(client, keyed),
            "AddJevClient registers one default client and a separate keyed client under Native AOT");
        Program.Check(
            result.IsSuccess && keyedResult.IsSuccess,
            "the default and the keyed client each evaluate through the factory's HttpClient under Native AOT");
    }
}
