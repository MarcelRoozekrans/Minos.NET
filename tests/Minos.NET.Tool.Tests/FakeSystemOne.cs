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

    /// <summary>The model every response names; the request's own model when null.</summary>
    public string? ReportedModel { get; init; }

    /// <summary>The status given to the invalid request, a body with no questions.</summary>
    public HttpStatusCode InvalidStatus { get; init; } = HttpStatusCode.UnprocessableEntity;

    // The first option takes all the probability, so the distribution sums to 1 over exactly the options sent.
    private static JsonObject ChoiceAnswer(JsonNode question)
    {
        var options = question["criteria"]!.AsObject().Select(o => o.Key).ToArray();
        var probabilities = new JsonObject();
        foreach (var option in options)
        {
            probabilities[option] = string.Equals(option, options[0], StringComparison.Ordinal) ? 1.0 : 0.0;
        }

        return new JsonObject { ["type"] = "choice", ["choice"] = options[0], ["probabilities"] = probabilities, ["confidence"] = 0.9 };
    }

    // The score is the level that takes all the probability, so it equals the sum of level times probability.
    private static JsonObject ScoreAnswer(JsonNode question)
    {
        var levels = question["criteria"]!.AsArray().Count;
        var level = Math.Min(1, levels - 1);
        var probabilities = new JsonObject();
        var legend = new JsonObject();
        for (var i = 0; i < levels; i++)
        {
            var key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            probabilities[key] = i == level ? 1.0 : 0.0;
            legend[key] = question["criteria"]![i]!.GetValue<string>();
        }

        return new JsonObject { ["type"] = "score", ["score"] = (double)level, ["confidence"] = 0.9, ["legend"] = legend, ["probabilities"] = probabilities };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("x-request-id", "req_fake");
        return response;
    }

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
            if (request.Headers.Authorization?.Parameter is { } key && key.Contains("invalid-key", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Unauthorized, new JsonObject { ["detail"] = new JsonObject { ["error_type"] = "authentication_error", ["message"] = "bad key" } });
            }

            if (body["questions"]!.AsObject().Count == 0)
            {
                return Json(InvalidStatus, new JsonObject { ["detail"] = new JsonArray(new JsonObject { ["type"] = "too_short", ["msg"] = "questions needs 1 item" }) });
            }

            var answers = new JsonObject();
            foreach (var (name, question) in body["questions"]!.AsObject())
            {
                answers[name] = question!["type"]!.GetValue<string>() switch
                {
                    "noul" => new JsonObject { ["type"] = "noul", ["noul"] = 0.9 },
                    "choice" => ChoiceAnswer(question),
                    "score" => ScoreAnswer(question),
                    _ => throw new NotSupportedException("fake supports noul, choice and score"),
                };
            }

            var response = new JsonObject
            {
                ["model"] = ReportedModel ?? body["model"]?.GetValue<string>() ?? "fake-model",
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
