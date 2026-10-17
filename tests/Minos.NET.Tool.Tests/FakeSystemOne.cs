using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Minos.Tool.Tests;

/// <summary>Answers POST /v1/systemone from the request's own questions, and records what it saw.</summary>
internal sealed class FakeSystemOne : HttpMessageHandler
{
    private int _inFlight;
    private int _peak;
    private int _count;

    public ConcurrentQueue<JsonNode> Bodies { get; } = new();

    public int Requests => Volatile.Read(ref _count);

    public int PeakConcurrency => Volatile.Read(ref _peak);

    /// <summary>Awaited inside each request while it counts as in flight, so concurrency can build up.</summary>
    public Func<Task>? Gate { get; init; }

    /// <summary>Throws from the Nth request on (1-based), the way a faulty client does.</summary>
    public int? ThrowFromRequest { get; init; }

    /// <summary>The message of the exception thrown from <see cref="ThrowFromRequest"/>.</summary>
    public string FaultMessage { get; init; } = "the fake broke";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var n = Interlocked.Increment(ref _count);
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, seen) != seen)
        {
        }

        try
        {
            var text = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var body = JsonNode.Parse(text)!;
            Bodies.Enqueue(body);
            if (Gate is not null)
            {
                await Gate().ConfigureAwait(false);
            }

            if (ThrowFromRequest is { } from && n >= from)
            {
                throw new InvalidOperationException(FaultMessage);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/systemone", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var answers = new JsonObject();
            foreach (var (name, question) in body["questions"]!.AsObject())
            {
                answers[name] = question!["type"]!.GetValue<string>() switch
                {
                    "noul" => new JsonObject { ["type"] = "noul", ["noul"] = 0.9 },
                    "choice" => new JsonObject
                    {
                        ["type"] = "choice",
                        ["choice"] = question["criteria"]!.AsObject().First().Key,
                        ["probabilities"] = new JsonObject { [question["criteria"]!.AsObject().First().Key] = 1.0 },
                        ["confidence"] = 0.9,
                    },
                    _ => throw new NotSupportedException("fake supports noul and choice"),
                };
            }

            var response = new JsonObject
            {
                ["model"] = body["model"]?.GetValue<string>() ?? "fake-model",
                ["answers"] = answers,
                ["usage"] = new JsonObject { ["input_tokens"] = 1000, ["output_tokens"] = 100 },
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
