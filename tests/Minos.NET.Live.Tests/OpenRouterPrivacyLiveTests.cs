using System.Net.Http.Headers;
using System.Text;
using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

/// <summary>
/// Proves that OpenRouter's <c>/v1/systemone</c> reads the <c>provider</c> object that
/// <see cref="SystemOneDecisionClientOptions.ProviderPreferences"/> sends, and records what each preference does to Jev.
/// Every test pins <see cref="SystemOneDefaults.Model"/>, ignoring <c>MINOS_LIVE_OPENROUTER_MODEL</c>, because another model's
/// retention policy would fail them for reasons that have nothing to do with the preferences.
/// </summary>
public sealed class OpenRouterPrivacyLiveTests
{
    private readonly ITestOutputHelper _output;

    public OpenRouterPrivacyLiveTests(ITestOutputHelper output) => _output = output;

    // A provider object no endpoint can satisfy is refused, while the same body without it is answered. So the endpoint
    // reads the object, rather than ignoring it.
    [LiveFact(SystemOneProvider.OpenRouter)]
    public async Task Endpoint_ReadsTheProviderObject()
    {
        using var http = new HttpClient { BaseAddress = SystemOneDefaults.OpenRouterBaseAddress };
        var key = Environment.GetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable);

        var plain = await PostAsync(http, key, provider: null);
        var impossible = await PostAsync(http, key, provider: """{"only":["no-such-provider"]}""");

        Assert.True(plain.IsSuccessStatusCode, "The body without a provider object must be answered.");
        Assert.False(impossible.IsSuccessStatusCode, "A provider object no endpoint satisfies must be refused.");
    }

    [LiveFact(SystemOneProvider.OpenRouter)]
    public async Task ZeroDataRetention_Answers()
    {
        using var client = Client(new OpenRouterProviderPreferences { ZeroDataRetention = true });

        var result = await client.EvaluateAsync(Live.Request(SystemOneDefaults.Model));

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Live.AssertAnsweredEveryQuestion(result.Value);
    }

    // Jev is on OpenRouter's zero-data-retention list, but whether it also passes the data-collection filter is not
    // published. Either outcome passes; the output records which one, for the Providers page.
    [LiveFact(SystemOneProvider.OpenRouter)]
    public async Task DenyDataCollection_AnswersOrRefuses()
    {
        using var client = Client(new OpenRouterProviderPreferences { DenyDataCollection = true });

        var result = await client.EvaluateAsync(Live.Request(SystemOneDefaults.Model));

        if (result.IsSuccess)
        {
            _output.WriteLine("DenyDataCollection: answered.");
            Live.AssertAnsweredEveryQuestion(result.Value);
            return;
        }

        _output.WriteLine("DenyDataCollection: refused.");
        Live.LogError(_output, result.Error);
        Assert.Contains(result.Error.StatusCode, new int?[] { 400, 404 });
    }

    private static SystemOneDecisionClient Client(OpenRouterProviderPreferences preferences)
        => new(new SystemOneDecisionClientOptions
        {
            Provider = SystemOneProvider.OpenRouter,
            MaxRetries = 0,
            Model = SystemOneDefaults.Model,
            ProviderPreferences = preferences,
        });

    private async Task<HttpResponseMessage> PostAsync(HttpClient http, string? key, string? provider)
    {
        var body = """{"state":"Our checkout is down for every customer.","model":""" + "\"" + SystemOneDefaults.Model + "\""
            + ""","questions":{"is_urgent":{"type":"noul","instructions":"Does this convey urgency?"}}"""
            + (provider is null ? string.Empty : ",\"provider\":" + provider)
            + "}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/systemone")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var response = await http.SendAsync(request);
        _output.WriteLine((provider ?? "(no provider object)") + " -> " + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " " + await response.Content.ReadAsStringAsync());
        return response;
    }
}
