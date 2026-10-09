using System.Net;

namespace Minos.DependencyInjection.Tests;

/// <summary>What the registration tests share: options, a canned handler, a request and request checks.</summary>
internal static class Registrations
{
    /// <summary>The User-Agent <see cref="DecisionClient"/> sends: one product token with no build metadata.</summary>
    public const string UserAgentPattern = "^Minos\\.NET/[^ +]+$";

    /// <summary>Configures a TypeSafe client with a key, a base address and no retries.</summary>
    public static Action<DecisionClientOptions> Options(string baseAddress, string apiKey = "di-key")
        => options =>
        {
            options.ApiKey = apiKey;
            options.BaseAddress = new Uri(baseAddress);
            options.MaxRetries = 0;
        };

    /// <summary>A handler that answers every request with the <c>response-noul.json</c> fixture.</summary>
    public static StubHandler Noul() => StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));

    /// <summary>A one-question Noul request.</summary>
    public static SystemOneRequest Request() => new()
    {
        State = "Help! My payouts have been failing for 3 days.",
        Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
        },
    };

    /// <summary>
    /// Gets the one request <paramref name="handler"/> received. Used instead of xUnit's <c>Assert.Single</c>, which
    /// HLQ005 reports.
    /// </summary>
    public static StubHandler.Captured OnlyRequest(StubHandler handler)
        => handler.Requests is [var only]
            ? only
            : throw new InvalidOperationException($"Expected one request, found {handler.Requests.Count}.");

    /// <summary>Gets <paramref name="first"/> and each handler inside it, ending with the primary handler.</summary>
    public static List<HttpMessageHandler> HandlerChain(HttpMessageHandler first)
    {
        List<HttpMessageHandler> chain = [first];
        for (var current = first; current is DelegatingHandler { InnerHandler: { } inner }; current = inner)
        {
            chain.Add(inner);
        }

        return chain;
    }
}
