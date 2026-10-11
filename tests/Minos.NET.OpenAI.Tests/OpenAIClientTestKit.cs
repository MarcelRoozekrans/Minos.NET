using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Minos.OpenAI.Protocols;
using Minos.OpenAI.Transport;
using Minos.Transport;

namespace Minos.OpenAI.Tests;

public enum TicketTopic
{
    [Criteria("Payments and invoices")]
    Billing,

    Other,
}

public enum TicketUrgency
{
    [Level("Not urgent")]
    NotUrgent,

    [Level("Soon")]
    Soon,

    [Level("Right now")]
    RightNow,
}

/// <summary>The typed form of <see cref="TestSets.AllKinds"/>: the set <c>success-all-kinds.json</c> answers.</summary>
[Questions]
public partial record TicketCheck
{
    [Noul("Is this a refund request?")]
    public partial Noul Refund { get; }

    [Choice("Which topic is this about?")]
    public partial Choice<TicketTopic> Topic { get; }

    [Score("How urgent is this?")]
    public partial Score<TicketUrgency> Urgency { get; }
}

/// <summary>What the <see cref="OpenAIDecisionClient"/> test classes share: a client over a stub handler.</summary>
internal static class OpenAIClientTestKit
{
    public const string TestModel = "gpt-test-model";

    public static StubHandler.Captured OnlyRequest(StubHandler handler)
    {
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(handler.Requests);
#pragma warning restore HLQ005
    }

    public static string FixtureText(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Success() => Json(HttpStatusCode.OK, FixtureText("success-all-kinds.json"));

    /// <summary>The body the client sends for <paramref name="request"/> with <paramref name="model"/>, as JSON.</summary>
    public static JsonNode ExpectedBody(DecisionRequest request, string model = TestModel)
    {
        using var body = ((IDecisionProtocol)OpenAIDecisionsProtocol.Instance).WriteRequest(request.Definition, request.State, request.Images, model, ArrayPool<byte>.Shared);
        return JsonNode.Parse(Encoding.UTF8.GetString(body.Span))!;
    }

    public static OpenAIClientSettings Settings(Action<OpenAIDecisionClientOptions>? configure = null)
    {
        var options = new OpenAIDecisionClientOptions
        {
            ApiKey = "k",
            Model = TestModel,
            MaxRetries = 0,
            InitialBackoff = TimeSpan.FromMilliseconds(1),
            Jitter = false,
        };
        configure?.Invoke(options);
        return OpenAIClientSettings.Resolve(options, _ => null);
    }

    /// <summary>
    /// A client over a borrowed <see cref="HttpClient"/> on <paramref name="handler"/>; the caller disposes
    /// <paramref name="httpClients"/>. It does not retry unless <paramref name="configure"/> says so.
    /// </summary>
    public static OpenAIDecisionClient Client(
        List<HttpClient> httpClients,
        StubHandler handler,
        CountingPool? pool = null,
        ILoggerFactory? loggerFactory = null,
        Action<OpenAIDecisionClientOptions>? configure = null)
    {
        var http = new HttpClient(handler);
        httpClients.Add(http);
        return new OpenAIDecisionClient(
            Settings(configure), http, ownedHandler: null, TimeProvider.System, pool ?? new CountingPool(), loggerFactory?.CreateLogger(DecisionLog.Category));
    }

    /// <summary>A client that creates and owns its <see cref="HttpClient"/> over <paramref name="handler"/>.</summary>
    public static OpenAIDecisionClient Owned(StubHandler handler, int maxRetries = 2, CountingPool? pool = null, ILogger? logger = null)
        => new(Settings(o => o.MaxRetries = maxRetries), httpClient: null, handler, TimeProvider.System, pool ?? new CountingPool(), logger);
}
