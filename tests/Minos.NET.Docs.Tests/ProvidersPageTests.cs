using Minos.SystemOne;

namespace Minos.Docs.Tests;

// Keeps the tables of the Providers page in step with the code.
// Serialized with the other test classes that set or read the key environment variables.
[Collection("key environment")]
public sealed class ProvidersPageTests
{
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
}
