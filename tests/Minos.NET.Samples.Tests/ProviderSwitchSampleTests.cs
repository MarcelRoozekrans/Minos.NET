using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minos.Samples.ProviderSwitch;
using Minos.SystemOne;
using ZeroAlloc.TestHelpers;

namespace Minos.Samples.Tests;

public sealed class ProviderSwitchSampleTests
{
    private const string Sample = ProviderSwitchApp.SampleName;

    [Fact]
    public async Task EveryTicket_IsRoutedToTheChosenTeam_WithItsConfidence()
    {
        var report = await RunCanned();

        Assert.Equal(
            Tickets.All.Select(t => (t.Id, CannedTickets.Answers[t.Id].Team, CannedTickets.Answers[t.Id].Confidence)),
            report.Tickets.Select(t => (t.Id, t.Team, t.Confidence)));
        Assert.Equal(Tickets.All.Select(t => t.Text), report.Tickets.Select(t => t.Text));
    }

    [Fact]
    public async Task TheReport_EndsWithTheProviderAndTheModelThatAnswered()
    {
        var report = await RunCanned("OpenRouter", "typesafe/jev-1.13-20260917");

        Assert.Equal("OpenRouter", report.Provider);
        Assert.Equal("typesafe/jev-1.13-20260917", report.Model);
        Assert.EndsWith("\nAnswered by OpenRouter with typesafe/jev-1.13-20260917\n", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PrintsOneLinePerTicket_ThenTheProviderAndModel()
    {
        var report = new ProviderSwitchReport(
            "Local",
            "local-model",
            [new RoutedTicket("s01", "x", "billing", 0.5), new RoutedTicket("s02", "y", "technical", 0.126)]);

        Assert.Equal("s01  billing    0.50\ns02  technical  0.13\nAnswered by Local with local-model\n", report.Render());
    }

    [Fact]
    public async Task EachTicket_IsOneRequest()
    {
        var handler = new CannedTickets("canned-model");

        _ = await RunCanned(handler);

        Assert.Equal(Tickets.All.Count, handler.Requests);
        Assert.Equal(8, Tickets.All.Count);
        Assert.Equal(handler.Hashes.Count, handler.Hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task AFailedCall_NamesTheTicket()
    {
        using var http = new HttpClient(new Failing()) { BaseAddress = new Uri("https://canned.example/api/") };
        using var client = new SystemOneClient(http, new SystemOneClientOptions { ApiKey = "canned-key", MaxRetries = 0 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ProviderSwitchSample.RunAsync(client, "TypeSafe", CancellationToken.None));

        Assert.StartsWith("s01: ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_OnCannedAnswers_MatchesTheSnapshot() => TextSnapshot.VerifyText((await RunCanned()).Render());

    // TypeSafe and OpenRouter both ask jev-latest, so their request bodies are byte-identical and the replay handler,
    // which keys answers by the body's hash, could not tell them apart in one file. The local server names its model.
    [Fact]
    public async Task TheRequests_ToTypeSafeAndOpenRouter_AreByteIdentical_AndTheLocalServersDiffer()
    {
        var typeSafe = await RequestHashesFor("TypeSafe");
        var openRouter = await RequestHashesFor("OpenRouter");
        var local = await RequestHashesFor("Local");

        Assert.Equal(8, typeSafe.Count);
        Assert.Equal(typeSafe, openRouter);
        Assert.Empty(typeSafe.Intersect(local, StringComparer.Ordinal));
    }

    [Fact]
    public void TheProviderSections_DifferOnlyInProviderSettings()
    {
        var configuration = Appsettings();
        var names = SampleProviders.Names(configuration);
        string[] providerSettings = ["ApiKey", "BaseAddress", "Model", "Provider"];

        var shared = names
            .Select(name => Keys(Section(configuration, name)).Except(providerSettings, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            .ToList();

        Assert.Equal(["Local", "OpenRouter", "TypeSafe"], names);
        Assert.NotEmpty(shared[0]);
        Assert.All(shared, keys => Assert.Equal(shared[0], keys));
        Assert.Contains(configuration[SampleProviders.ActiveKey], names);
    }

    [Fact]
    public void TheProviderSettings_AreTheSpecs()
    {
        var configuration = Appsettings();

        Assert.Equal("TypeSafe", Section(configuration, "TypeSafe")["Provider"]);
        Assert.Equal("OpenRouter", Section(configuration, "OpenRouter")["Provider"]);
        Assert.Equal("Custom", Section(configuration, "Local")["Provider"]);
        Assert.Equal("http://localhost:8000/", Section(configuration, "Local")["BaseAddress"]);
        Assert.Equal("Mapika/decider-2b-GGUF:Q4_K_M", Section(configuration, "Local")["Model"]);

        // No key is ever in the file: the hosted presets read their environment variables, and the local server needs none.
        Assert.All(SampleProviders.Names(configuration), name => Assert.Null(Section(configuration, name)["ApiKey"]));
    }

    [Fact]
    public void TheSamplesPage_CountsOneRequestPerTicketPerProvider()
    {
        var page = File.ReadAllLines(Path.Combine(Repository.Root, "docs", "samples.md"));

        var row = Assert.Single(page, line => line.StartsWith("| [Provider switch]", StringComparison.Ordinal));

        Assert.EndsWith($"| {Tickets.All.Count.ToString(CultureInfo.InvariantCulture)} per provider |", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownProvider_ExitsWithTwo_AndNamesTheConfiguredOnes()
    {
        var (code, output, error) = await RunApp(["--provider", "openai"], SampleHost.SampleDirectory(Sample));

        Assert.Equal(2, code);
        Assert.Contains("Unknown provider openai", error, StringComparison.Ordinal);
        Assert.Contains("Local, OpenRouter, TypeSafe", error, StringComparison.Ordinal);
        Assert.Empty(output);
    }

    [Fact]
    public async Task NoActiveProvider_ExitsWithTwo_AndNamesTheConfiguredOnes()
    {
        var directory = Directory.CreateTempSubdirectory("minos-provider-switch-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """{ "Minos": { "Providers": { "TypeSafe": { "Provider": "TypeSafe" }, "Local": { "Provider": "Custom" } } } }""");

            var (code, output, error) = await RunApp([], directory);

            Assert.Equal(2, code);
            Assert.Contains("No provider is active", error, StringComparison.Ordinal);
            Assert.Contains("Local, TypeSafe", error, StringComparison.Ordinal);
            Assert.Empty(output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ABadArgument_ExitsWithTwo_AndPrintsTheUsage()
    {
        var (code, _, error) = await RunApp(["--fast"], SampleHost.SampleDirectory(Sample));

        Assert.Equal(2, code);
        Assert.Contains("Unknown argument --fast", error, StringComparison.Ordinal);
        Assert.Contains(ProviderSwitchApp.Usage, error, StringComparison.Ordinal);
    }

    // A provider no one ever records, so the test does not depend on which recordings are checked in.
    [Fact]
    public async Task ReplayWithoutTheProvidersRecordings_ExitsWithOne_AndPrintsTheRecordCommand()
    {
        var directory = Directory.CreateTempSubdirectory("minos-provider-switch-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """{ "Minos": { "Active": "Nowhere", "Providers": { "Nowhere": { "Provider": "Custom", "BaseAddress": "http://localhost:1/", "Model": "none" } } } }""");

            var (code, output, error) = await RunApp([], directory);

            Assert.Equal(1, code);
            Assert.Contains("dotnet run --project samples/" + Sample + " -- --provider nowhere --record", error, StringComparison.Ordinal);
            Assert.Empty(output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task<ProviderSwitchReport> RunCanned(string provider = "TypeSafe", string model = "canned-model")
        => RunCanned(new CannedTickets(model), provider);

    private static async Task<ProviderSwitchReport> RunCanned(CannedTickets handler, string provider = "TypeSafe")
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://canned.example/api/") };
        using var client = new SystemOneClient(http, new SystemOneClientOptions { ApiKey = "canned-key", MaxRetries = 0 });
        return await ProviderSwitchSample.RunAsync(client, provider, CancellationToken.None);
    }

    // Sends every ticket through a client bound from the provider's own section, as the sample does, and keeps the hashes.
    private static async Task<List<string>> RequestHashesFor(string provider)
    {
        var canned = new CannedTickets("canned-model");
        var services = new ServiceCollection();
        services.AddSystemOneClient(Section(Appsettings(), provider)).HttpClient.ConfigurePrimaryHttpMessageHandler(() => canned);
        services.AddSystemOneClient(options => options.ApiKey ??= "capture-key");
        await using var serviceProvider = services.BuildServiceProvider();

        _ = await ProviderSwitchSample.RunAsync(serviceProvider.GetRequiredService<IDecisionClient>(), provider, CancellationToken.None);

        return [.. canned.Hashes];
    }

    private static async Task<(int Code, string Output, string Error)> RunApp(string[] args, string contentRoot)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = await ProviderSwitchApp.RunAsync(args, contentRoot, output, error, CancellationToken.None);
        return (code, output.ToString(), error.ToString());
    }

    private static IConfiguration Appsettings()
        => new ConfigurationBuilder().AddJsonFile(Path.Combine(SampleHost.SampleDirectory(Sample), "appsettings.json")).Build();

    private static IConfigurationSection Section(IConfiguration configuration, string provider)
        => configuration.GetSection(SampleProviders.ProvidersKey).GetSection(provider);

    // Every setting under a section, nested ones included, by its path relative to the section.
    private static IEnumerable<string> Keys(IConfigurationSection section)
        => section.AsEnumerable(makePathsRelative: true).Where(pair => pair.Value is not null).Select(pair => pair.Key);

    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
