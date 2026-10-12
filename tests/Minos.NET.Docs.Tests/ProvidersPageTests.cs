using System.Globalization;
using System.Text.RegularExpressions;
using Minos.OpenAI;
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