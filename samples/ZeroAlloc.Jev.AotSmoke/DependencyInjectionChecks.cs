using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><c>AddJevClient</c> under Native AOT: default and keyed clients, configured by delegates or bound from configuration, registered, resolved and evaluated.</summary>
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

    [Covers("static Microsoft.Extensions.DependencyInjection.JevServiceCollectionExtensions.AddJevClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, System.Action<ZeroAlloc.Jev.JevClientOptions!>! configure) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.JevServiceCollectionExtensions.AddJevClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, System.Action<ZeroAlloc.Jev.JevClientOptions!>! configure) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync(ZeroAlloc.Jev.SystemOneRequest! request) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<ZeroAlloc.Jev.SystemOneResponse!, ZeroAlloc.Jev.JevError!>>")]
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

    /// <summary>The smoke app's configuration: a default client and an OpenRouter client, each in its own section.</summary>
    public static IConfigurationRoot BoundConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jev:ApiKey"] = "smoke-key",
                ["Jev:BaseAddress"] = "https://example.test/api/",
                ["Jev:Timeout"] = "00:00:30",
                ["Jev:MaxRetries"] = "3",
                ["Jev:InitialBackoff"] = "00:00:00.250",
                ["Jev:MaxRetryDelay"] = "00:00:10",
                ["Jev:Jitter"] = "false",
                ["OpenRouter:Provider"] = "OpenRouter",
                ["OpenRouter:ApiKey"] = "smoke-openrouter-key",
                ["OpenRouter:BaseAddress"] = "https://openrouter.example.test/api/",
                ["OpenRouter:Model"] = "jev-1.13.0",
            })
            .Build();

    /// <summary>Registers the default client bound from <see cref="BoundConfiguration"/>, over a canned handler.</summary>
    public static void RegisterBoundDefaultClient(IServiceCollection services)
        => services
            .AddJevClient(BoundConfiguration().GetSection("Jev"))
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));

    [Covers("static Microsoft.Extensions.DependencyInjection.JevServiceCollectionExtensions.AddJevClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, Microsoft.Extensions.Configuration.IConfiguration! configuration) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.JevServiceCollectionExtensions.AddJevClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, Microsoft.Extensions.Configuration.IConfiguration! configuration) -> Microsoft.Extensions.DependencyInjection.IHttpClientBuilder!")]
    public static async Task ClientsBoundFromConfigurationEvaluate()
    {
        var services = new ServiceCollection();
        RegisterBoundDefaultClient(services);
        services
            .AddJevClient("openrouter", BoundConfiguration().GetSection("OpenRouter"))
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<JevClientOptions>>();
        var defaults = monitor.Get(Options.DefaultName);
        var keyed = monitor.Get("openrouter");

        Program.Check(
            defaults is { ApiKey: "smoke-key", MaxRetries: 3, Jitter: false }
                && defaults.Timeout == TimeSpan.FromSeconds(30)
                && defaults.InitialBackoff == TimeSpan.FromMilliseconds(250)
                && defaults.MaxRetryDelay == TimeSpan.FromSeconds(10)
                && defaults.BaseAddress == new Uri("https://example.test/api/"),
            "AddJevClient binds every default client setting from configuration under Native AOT");
        Program.Check(
            keyed is { Provider: JevProvider.OpenRouter, ApiKey: "smoke-openrouter-key", Model: "jev-1.13.0" },
            "AddJevClient binds a keyed client's own section under Native AOT");

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Program.Request()).ConfigureAwait(false);
        var keyedResult = await provider.GetRequiredKeyedService<IJevClient>("openrouter")
            .EvaluateAsync(Program.Request()).ConfigureAwait(false);
        Program.Check(
            result.IsSuccess && keyedResult.IsSuccess,
            "the default and the keyed client bound from configuration each evaluate under Native AOT");
    }

    public static void InvalidConfigurationFailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = "smoke-key",
                ["BaseAddress"] = "https://example.test/api/",
                ["MaxRetries"] = "11",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddJevClient(configuration);
        using var provider = services.BuildServiceProvider();

        string? failure = null;
        try
        {
            _ = provider.GetRequiredService<IJevClient>();
        }
        catch (OptionsValidationException exception)
        {
            failure = exception.Message;
        }

        Program.Check(
            string.Equals(failure, "MaxRetries must be between 0 and 10. (Parameter 'options')", StringComparison.Ordinal),
            "an invalid bound value fails options validation with the core's message under Native AOT");
    }
}
