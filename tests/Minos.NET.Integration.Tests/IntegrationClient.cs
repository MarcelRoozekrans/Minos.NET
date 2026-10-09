using System.Collections.Concurrent;
using System.Diagnostics;

namespace Minos.Integration.Tests;

/// <summary>Builds a <see cref="JevClient"/> configured for the WireMock fixtures: a fixed key, no jitter and short backoffs.</summary>
/// <remarks>
/// Only a test that sets <c>timeout</c> gets a per-attempt budget of its own; everything else keeps the library's
/// default. A fixture-wide budget of a few seconds is not something these tests check, and on a loaded machine a
/// thread in the test host can wait seconds for a CPU, which would turn a slow success into a spurious time-out.
/// </remarks>
internal static class IntegrationClient
{
    public static JevClient Create(
        Uri baseAddress,
        int maxRetries = 0,
        TimeSpan? timeout = null,
        TimeSpan? initialBackoff = null,
        JevProvider provider = JevProvider.TypeSafe)
    {
        var options = Options(maxRetries, initialBackoff, provider);
        options.BaseAddress = baseAddress;
        if (timeout is { } perAttempt)
        {
            options.Timeout = perAttempt;
        }

        return new JevClient(options);
    }

    /// <summary>
    /// Builds a client over <paramref name="attempts"/>' <see cref="HttpClient"/>, so the test observes each attempt
    /// on the client side; its base address and per-attempt time-out come from that <see cref="HttpClient"/>.
    /// </summary>
    public static JevClient Create(AttemptRecorder attempts, int maxRetries = 0, TimeSpan? initialBackoff = null)
        => new(attempts.HttpClient, Options(maxRetries, initialBackoff, JevProvider.TypeSafe));

    /// <summary>
    /// Applies the fixtures' configuration to <paramref name="options"/>: the fixed key, no jitter, a 5 second maximum
    /// retry delay and a short backoff. The hand-built clients and the ones from <c>AddJevClient</c> both configure
    /// through this.
    /// </summary>
    public static void Configure(JevClientOptions options, int maxRetries, TimeSpan? initialBackoff = null)
    {
        options.ApiKey = "integration-key";
        options.MaxRetries = maxRetries;
        options.InitialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(10);
        options.MaxRetryDelay = TimeSpan.FromSeconds(5);
        options.Jitter = false;
    }

    private static JevClientOptions Options(int maxRetries, TimeSpan? initialBackoff, JevProvider provider)
    {
        var options = new JevClientOptions { Provider = provider };
        Configure(options, maxRetries, initialBackoff);
        return options;
    }
}

/// <summary>
/// Records, on the client side, when each HTTP attempt starts. WireMock can only count or time a request once it has
/// read it, and on a loaded machine that can be seconds after the client sent it, or never, when the client gives up
/// on the attempt first. The client's own view of its attempts has neither problem.
/// </summary>
internal sealed class AttemptRecorder : IDisposable
{
    private readonly ConcurrentQueue<long> _starts = new();

    /// <param name="baseAddress">The server to send to.</param>
    /// <param name="timeout">The per-attempt time-out; <see langword="null"/> keeps <see cref="HttpClient"/>'s default.</param>
    public AttemptRecorder(Uri baseAddress, TimeSpan? timeout = null)
    {
        HttpClient = new HttpClient(new RecordingHandler(_starts)) { BaseAddress = baseAddress };
        if (timeout is { } perAttempt)
        {
            HttpClient.Timeout = perAttempt;
        }
    }

    /// <summary>Gets the client that sends through this recorder.</summary>
    public HttpClient HttpClient { get; }

    /// <summary>Gets how many attempts have started.</summary>
    public int Count => _starts.Count;

    /// <summary>Gets the time between the start of attempt <paramref name="index"/> and the start of the next one.</summary>
    public TimeSpan Gap(int index)
    {
        var starts = _starts.ToArray();
        return Stopwatch.GetElapsedTime(starts[index], starts[index + 1]);
    }

    public void Dispose() => HttpClient.Dispose();

    private sealed class RecordingHandler(ConcurrentQueue<long> starts) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            starts.Enqueue(Stopwatch.GetTimestamp());
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>Integration-only request builders; fixture text comes from the linked <see cref="Fixture"/>.</summary>
internal static class Fixtures
{
    /// <summary>
    /// Builds, from public types only, the request that serializes to <c>request-noul.json</c>; see
    /// <c>RequestSerializationTests.Noul_WithCriteria_WritesTrueAndFalse</c> for the same fixture built the
    /// same way.
    /// </summary>
    public static SystemOneRequest NoulRequest()
        => new()
        {
            State = "Help! My payouts have been failing for 3 days.",
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion
                {
                    Instructions = "Does this convey urgency?",
                    Criteria = new NoulCriteria
                    {
                        WhenTrue = "Explicitly time-sensitive",
                        WhenFalse = "No urgency expressed",
                    },
                },
            },
        };
}
