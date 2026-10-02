using System.Globalization;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class IntentRoutingTests
{
    [Theory]
    [InlineData("order_status", 0.9, 0.2, RequestHandler.OrderLookup)]
    [InlineData("product_question", 0.8, 0.6, RequestHandler.ProductAssistant)]
    [InlineData("product_question", 0.8, 1.6, RequestHandler.Person)]
    [InlineData("order_status", 0.4, 0.1, RequestHandler.Person)]
    [InlineData("order_status", 0.5, 0.1, RequestHandler.OrderLookup)]
    [InlineData("order_status", 0.9, 1.0, RequestHandler.OrderLookup)]
    [InlineData("complaint", 0.95, 0.5, RequestHandler.Person)]
    public async Task EachRequestGoesToTheCheapestCapableHandler(
        string intent, double confidence, double complexity, RequestHandler expected)
    {
        var request = await CannedJev.EvaluateAsync<IncomingRequest>(Response(intent, confidence, complexity), "Where is my order?");

        Assert.Equal(expected, RequestRouting.Route(request));
    }

    private static readonly string[] IntentKeys = ["order_status", "product_question", "complaint"];

    // Builds a two-question response: the chosen intent gets 0.8 and the others share the rest, and the
    // complexity probabilities sit on the two levels around the score, so their weighted mean equals it.
    private static string Response(string intent, double confidence, double complexity)
    {
        var intentProbabilities = string.Join(
            ',',
            IntentKeys.Select(key => string.Create(CultureInfo.InvariantCulture, $"\"{key}\":{(string.Equals(key, intent, StringComparison.Ordinal) ? 0.8 : 0.1)}")));

        var lower = Math.Min(Math.Floor(complexity), 1.0);
        var upperShare = complexity - lower;
        var complexityProbabilities = lower < 1.0
            ? string.Create(CultureInfo.InvariantCulture, $"\"0\":{1.0 - upperShare},\"1\":{upperShare},\"2\":0")
            : string.Create(CultureInfo.InvariantCulture, $"\"0\":0,\"1\":{1.0 - upperShare},\"2\":{upperShare}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"{{{intent}}}","probabilities":{{{{intentProbabilities}}}},"confidence":{{{confidence}}}},"complexity":{"type":"score","score":{{{complexity}}},"legend":{"0":"Simple: one fact answers it","1":"Moderate: needs some context","2":"Complex: needs judgement"},"probabilities":{{{{complexityProbabilities}}}},"confidence":0.7}},"usage":{"input_tokens":150,"output_tokens":20}}""");
}
}
