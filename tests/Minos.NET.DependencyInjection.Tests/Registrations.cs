using System.Net;
using Microsoft.Extensions.Time.Testing;
using Minos.SystemOne;

namespace Minos.DependencyInjection.Tests;

/// <summary>What the registration tests share: options, a canned handler, a request and request checks.</summary>
internal static class Registrations
{
    /// <summary>The User-Agent <see cref="SystemOneDecisionClient"/> sends: one product token with no build metadata.</summary>
    public const string UserAgentPattern = "^Minos\\.NET/[^ +]+$";

    /// <summary>Configures a TypeSafe client with a key, a base address and no retries.</summary>
    public static Action<SystemOneDecisionClientOptions> Options(string baseAddress, string apiKey = "di-key")
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

    /// <summary>A handler that answers 429 with a 30 second <c>Retry-After</c> once, then <paramref name="body"/>; <paramref name="arrivals"/> gets the fake time each request reached the handler.</summary>
    public static StubHandler RateLimitedOnce(string body, FakeTimeProvider time, List<DateTimeOffset> arrivals)
        => StubHandler.Sequence(
            () =>
            {
                arrivals.Add(time.GetUtcNow());
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return response;
            },
            () =>
            {
                arrivals.Add(time.GetUtcNow());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            });

    /// <summary>
    /// Drives <paramref name="pending"/> to completion by stepping <paramref name="time"/>. A client that waited 30 seconds on the
    /// system clock would not finish inside the test's real-time limit; one that waits on <paramref name="time"/> finishes at once.
    /// </summary>
    public static async Task<T> CompleteOnFakeTime<T>(Task<T> pending, FakeTimeProvider time)
    {
        var finished = false;
        var stepping = Task.Run(async () =>
        {
            while (!Volatile.Read(ref finished))
            {
                time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(5).ConfigureAwait(false);
            }
        });
        try
        {
            return await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Volatile.Write(ref finished, true);
            await stepping;
        }
    }

    /// <summary>
    /// Gets the one request <paramref name="handler"/> received.
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

/// <summary>A minimal <see cref="IDecisionClient"/> for the factory registrations: every call fails as unsupported.</summary>
internal sealed class FakeDecisionClient : IDecisionClient
{
    public ValueTask<ZeroAlloc.Results.Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        => new(new DecisionError(DecisionErrorKind.Unsupported, "The fake client answers nothing."));

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
