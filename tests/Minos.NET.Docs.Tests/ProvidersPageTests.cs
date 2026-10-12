using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minos.OpenAI;
using Minos.Samples;
using Minos.Samples.ProviderSwitch;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

// Keeps the Providers page in step with the code. The matrix's columns are the presets and OpenAI, its capability rows
// are what each client resolves, and its MIN ids are the provider rules. It reads no other cell's wording, so a
// difference row can be reworded without breaking it.
// Serialized with the other test classes that set or read the key environment variables.
[Collection("key environment")]
public sealed partial class ProvidersPageTests
{
    private const string Page = "providers.md";

    private const string Matrix = "## At a glance";

    private static readonly string[] CapabilityRows = ["Question kinds", "Most questions", "Most Choice options", "Images", "Model listing"];

    [Fact]
    public async Task TheSideBySideTable_MatchesTheRecordings()
    {
        string[] providers = ["TypeSafe", "OpenRouter", "Local"];
        var directory = SampleHost.SampleDirectory(ProviderSwitchApp.SampleName);
        var reports = new List<ProviderSwitchReport>();
        foreach (var provider in providers)
        {
            await using var services = SampleHost.BuildProviderReplayProvider(directory, ProviderSwitchApp.SampleName, provider);
            reports.Add(await ProviderSwitchSample.RunAsync(services.GetRequiredService<IDecisionClient>(), provider, CancellationToken.None));
        }

        var expected = new List<string>
        {
            "| Ticket | " + string.Join(" | ", providers) + " |",
            "| --- | --- | --- | --- |",
        };
        for (var i = 0; i < reports[0].Tickets.Count; i++)
        {
            expected.Add("| " + reports[0].Tickets[i].Id + " | " + string.Join(" | ", Cells(reports, i)) + " |");
        }

        expected.Add("| Model | " + string.Join(" | ", reports.Select(r => "`" + r.Model + "`")) + " |");

        var table = SectionLines("## Switching providers").SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|'));
        Assert.Equal(string.Join('\n', expected), string.Join('\n', table));
    }

    private static IEnumerable<string> Cells(List<ProviderSwitchReport> reports, int ticket)
        => reports.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Tickets[ticket].Team} {r.Tickets[ticket].Confidence:0.00}"));

    [Fact]
    public void ThePresetsTable_MatchesWhatEachPresetDoes()
    {
        using var environment = new KeyEnvironment(null, null);
        var rows = PageTables.Rows("providers.md", "The presets");

        Assert.Equal(
            Enum.GetNames<SystemOneProvider>().Order(StringComparer.Ordinal),
            rows.Select(row => PageTables.Code(row[0])).Order(StringComparer.Ordinal));
        foreach (var row in rows)
        {
            var provider = Enum.Parse<SystemOneProvider>(PageTables.Code(row[0]));
            var hosted = provider is SystemOneProvider.TypeSafe or SystemOneProvider.OpenRouter;
            var keyVariable = hosted ? PageTables.Code(row[3]) : null;
            var modelCodes = PageTables.AllCode(row[4]);
            var model = modelCodes.Length > 0 ? modelCodes[0] : null;
            var options = new SystemOneClientOptions
            {
                Provider = provider,
                BaseAddress = hosted ? null : new Uri("http://localhost:8080/"),
                Model = model is null ? "my-model" : null,
            };

            // Without a key a hosted preset fails and names the variable on the page; the others need none.
            if (keyVariable is null)
            {
                Assert.Null(ClientOptions.Problem(options));
            }
            else
            {
                Assert.Contains(keyVariable, ClientOptions.Problem(options), StringComparison.Ordinal);
                options.ApiKey = "key";
                Assert.Null(ClientOptions.Problem(options));
            }

            // The default model is the one on the page, or there is none and the model must be set.
            options.Model = null;
            Assert.Equal(model is null, ClientOptions.Problem(options)?.Contains("no default model", StringComparison.Ordinal) ?? false);
            if (model is not null)
            {
                Assert.Equal(SystemOneDefaults.Model, model);
            }

            // The base address and the path.
            options.Model = model ?? "my-model";
            using var http = new HttpClient();
            SystemOneClient.ConfigureHttpClient(http, options);
            var expected = hosted
                ? PageTables.Code(row[1])
                : "http://localhost:8080/";
            Assert.Equal(expected, http.BaseAddress!.AbsoluteUri);
            Assert.Equal("v1/systemone", PageTables.AllCode(row[2])[0]);
        }

        Assert.Equal(SystemOneDefaults.TypeSafeBaseAddress.AbsoluteUri, PageTables.Code(rows[0][1]));
        Assert.Equal(SystemOneDefaults.OpenRouterBaseAddress.AbsoluteUri, PageTables.Code(rows[1][1]));
        Assert.Equal(SystemOneDefaults.ApiKeyEnvironmentVariable, PageTables.Code(rows[0][3]));
        Assert.Equal(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable, PageTables.Code(rows[1][3]));
    }

    [Fact]
    public void TheMatrixColumns_AreThePresets_ThenOpenAI()
    {
        var (header, _) = Table(Matrix);

        Assert.Equal([.. Enum.GetNames<SystemOneProvider>(), "OpenAI"], header.Skip(1).Select(Unquote));
    }

    [Fact]
    public void TheMatrix_HasFifteenRows_AndNoBlankCell()
    {
        var (header, rows) = Table(Matrix);

        Assert.Equal(15, rows.Count);
        Assert.All(rows, row => Assert.Equal(header.Length, row.Length));
        Assert.All(rows, row => Assert.All(row, cell => Assert.False(string.IsNullOrWhiteSpace(cell))));
    }

    [Fact]
    public void TheCapabilityRows_ComeFirst_AndMatchWhatEachClientResolves()
    {
        var (header, rows) = Table(Matrix);

        Assert.Equal(CapabilityRows, rows.Take(CapabilityRows.Length).Select(row => row[0]));
        for (var column = 1; column < header.Length; column++)
        {
            using var http = new HttpClient();
            using var client = Client(http, Unquote(header[column]));
            var capabilities = Assert.IsType<DecisionCapabilities>(client.GetService(typeof(DecisionCapabilities)));

            Assert.Equal(QuestionKinds.All, capabilities.QuestionKinds);
            Assert.Equal("every kind", rows[0][column]);
            Assert.Equal(Limit(rows[1][column]), capabilities.MaxQuestions);
            Assert.Equal(Limit(rows[2][column]), capabilities.MaxChoiceOptions);
            Assert.Equal(Yes(rows[3][column]), capabilities.Images);
            Assert.Equal(Yes(rows[4][column]), capabilities.ModelListing);
        }
    }

    [Fact]
    public void EveryProviderRuleInTheMatrix_IsADiagnosticId()
    {
        var ids = DiagnosticIdSource.ProviderRules();
        var (_, rows) = Table(Matrix);
        var used = rows.SelectMany(row => row).SelectMany(cell => MinId().Matches(cell).Select(match => match.Value)).ToList();

        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void EveryProviderRule_AppearsInTheSection()
    {
        var ids = DiagnosticIdSource.ProviderRules();
        var section = Section(Matrix);

        Assert.Equal(["MIN201", "MIN202", "MIN203", "MIN204", "MIN205", "MIN206"], ids);
        Assert.All(ids, id => Assert.Contains(id, section, StringComparison.Ordinal));
    }

    [Fact]
    public void TheSwitchingExample_IsTheSamplesAppsettings()
    {
        var sample = JsonNode.Parse(File.ReadAllText(
            Path.Combine(PublishedPages.Root, "samples", "Minos.NET.Samples.ProviderSwitch", "appsettings.json")));

        Assert.True(JsonNode.DeepEquals(sample, JsonNode.Parse(JsonBlock(0))), "The page's first json block is the sample's appsettings.json.");
    }

    [Theory]
    [InlineData("TypeSafe", SystemOneProvider.TypeSafe)]
    [InlineData("OpenRouter", SystemOneProvider.OpenRouter)]
    [InlineData("local", SystemOneProvider.Custom)]
    public void TheActiveProvider_IsBoundFromItsOwnSection(string active, SystemOneProvider expected)
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(JsonBlock(0)));
        var builder = Builder(json, active);
        ProviderSwitching.AddActiveProvider(builder);
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptionsMonitor<SystemOneClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal(expected, options.Provider);
        Assert.Equal(2, options.MaxRetries);
        if (expected == SystemOneProvider.Custom)
        {
            Assert.Equal(new Uri("http://localhost:8000/"), options.BaseAddress);
            Assert.Equal("Mapika/decider-2b-GGUF:Q4_K_M", options.Model);
            Assert.Null(options.ApiKey);
        }
    }

    [Fact]
    public void AnUnknownActiveProvider_FailsNamingIt()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(JsonBlock(0)));
        var builder = Builder(json, "Elsewhere");

        var exception = Assert.Throws<InvalidOperationException>(() => ProviderSwitching.AddActiveProvider(builder));

        Assert.Contains("Elsewhere", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLocalServerCommands_AreTheOnesCiRuns()
    {
        var workflow = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "local-systemone.yml"));
        var section = Section("## Running a local server");
        string[] commands =
        [
            "pip install --extra-index-url https://download.pytorch.org/whl/cpu ollajev==0.4.0",
            "ollajev pull Mapika/decider-2b-GGUF:Q4_K_M",
            "ollajev serve Mapika/decider-2b-GGUF:Q4_K_M --port 8000 --no-browser",
        ];

        Assert.All(commands, command => Assert.Contains(command, workflow, StringComparison.Ordinal));
        Assert.All(commands, command => Assert.Contains(command, section, StringComparison.Ordinal));
        Assert.Contains("OLLAJEV_DEVICE: cpu", workflow, StringComparison.Ordinal);
        Assert.Contains("OLLAJEV_DEVICE=cpu", section, StringComparison.Ordinal);
        Assert.Contains("http://localhost:8000/", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpenAISection_IsBoundThroughAddOpenAIDecisionClient()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(JsonBlock(1)));
        var builder = Builder(json, "OpenAI");
        ProviderSwitching.AddActiveClient(builder);
        using var host = builder.Build();

        var client = host.Services.GetRequiredService<IDecisionClient>();
        var options = host.Services.GetRequiredService<IOptionsMonitor<OpenAIDecisionClientOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Equal("openai", client.GetService<DecisionClientMetadata>()!.ProviderName);
        Assert.Equal("gpt-6-luna", options.Model);
        Assert.Equal(2, options.MaxRetries);
    }

    [Fact]
    public void AddActiveClient_StillBindsASystemOneSection()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(JsonBlock(0)));
        var builder = Builder(json, "OpenRouter");
        ProviderSwitching.AddActiveClient(builder);
        using var host = builder.Build();

        Assert.Equal("openrouter", host.Services.GetRequiredService<IDecisionClient>().GetService<DecisionClientMetadata>()!.ProviderName);
    }

    [Fact]
    public void AddingOpenAI_SaysTheSampleHasNoOpenAIRecording_AndLinksTheIssue()
    {
        var section = Section("## Adding OpenAI");

        Assert.Contains("The provider-switch sample has no OpenAI section, because", section, StringComparison.Ordinal);
        Assert.Contains("https://github.com/MarcelRoozekrans/Minos.NET/issues/161", section, StringComparison.Ordinal);
    }

    // A host with only the page's JSON, the active provider, and a key for each hosted provider, so no environment
    // variable is read.
    private static HostApplicationBuilder Builder(Stream json, string active)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddJsonStream(json);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Minos:Active"] = active,
            ["Minos:Providers:TypeSafe:ApiKey"] = "typesafe-key",
            ["Minos:Providers:OpenRouter:ApiKey"] = "openrouter-key",
            ["Minos:Providers:OpenAI:ApiKey"] = "openai-key",
        });
        return builder;
    }

    // The body of the page's json code block number index, counting from 0.
    private static string JsonBlock(int index)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", Page));
        var start = -1;
        for (var found = 0; found <= index; found++)
        {
            start = Array.FindIndex(lines, start + 1, line => string.Equals(line, "```json", StringComparison.Ordinal));
            Assert.True(start >= 0, $"{Page} has {index + 1} json blocks.");
        }

        var end = Array.FindIndex(lines, start + 1, line => string.Equals(line, "```", StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..end]);
    }

    private static IDecisionClient Client(HttpClient http, string column)
        => string.Equals(column, "OpenAI", StringComparison.Ordinal)
            ? new OpenAIDecisionClient(http, new OpenAIDecisionClientOptions { ApiKey = "key" })
            : new SystemOneClient(http, new SystemOneClientOptions
            {
                Provider = Enum.Parse<SystemOneProvider>(column),
                ApiKey = "key",
                BaseAddress = new Uri("http://localhost:8080/"),
                Model = "my-model",
            });

    // The lines under a heading, up to the next heading of the same or a higher level outside a code block.
    private static List<string> SectionLines(string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", Page));
        var start = Array.FindIndex(lines, line => string.Equals(line.TrimEnd(), heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{Page} has a heading '{heading}'.");

        var level = heading.IndexOf(' ', StringComparison.Ordinal);
        var section = new List<string>();
        var fenced = false;
        foreach (var line in lines[(start + 1)..])
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }

            if (!fenced && Level().Match(line) is { Success: true } match && match.Groups["hashes"].Length <= level)
            {
                break;
            }

            section.Add(line);
        }

        return section;
    }

    private static string Section(string heading) => string.Join('\n', SectionLines(heading));

    // The first table of a section: its header cells and its body rows.
    private static (string[] Header, List<string[]> Rows) Table(string heading)
    {
        var table = SectionLines(heading).SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|')).ToList();
        Assert.True(table.Count > 2, $"The section '{heading}' has a table.");
        return (Cells(table[0]), [.. table.Skip(2).Select(Cells)]);
    }

    private static string[] Cells(string line)
        => Array.ConvertAll(line.Trim().Trim('|').Split('|'), cell => cell.Trim());

    private static string Unquote(string cell) => cell.Trim('`');

    // A capability cell: "none" for no limit, or a number.
    private static int? Limit(string cell)
        => string.Equals(cell, "none", StringComparison.Ordinal) ? null : int.Parse(cell, CultureInfo.InvariantCulture);

    // A capability cell: "yes" or "no".
    private static bool Yes(string cell)
    {
        Assert.True(cell is "yes" or "no", $"{cell} is yes or no.");
        return string.Equals(cell, "yes", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\bMIN2\d\d\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MinId();

    [GeneratedRegex(@"^(?<hashes>#{1,6})\s", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Level();
}
