using System.Globalization;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class IntentRoutingTests
{
    [Theory]
    [InlineData("order_status", 0.9, 0.2, RequestHandler.OrderLookup)]
    [InlineData("product_question", 0.8, 0.6, RequestHandler.ProductAssistant)]
    [InlineData("product_question", 0.8, 1.6, RequestHandler.Person)]
    [InlineData("order_status", 0.4, 0.1, RequestHandler.Person)]
    [InlineData("complaint", 0.95, 0.5, RequestHandler.Person)]
    public async Task EachRequestGoesToTheCheapestCapableHandler(
        string intent, double confidence, double complexity, RequestHandler expected)
    {
        var request = await CannedJev.EvaluateAsync<IncomingRequest>(Response(intent, confidence, complexity), "Where is my order?");

        Assert.Equal(expected, RequestRouting.Route(request));
    }

    private static string Response(string intent, double confidence, double complexity)
        => string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"{{{intent}}}","probabilities":{"order_status":0.4,"product_question":0.4,"complaint":0.2},"confidence":{{{confidence}}}},"complexity":{"type":"score","score":{{{complexity}}},"legend":{"0":"Simple: one fact answers it","1":"Moderate: needs some context","2":"Complex: needs judgement"},"probabilities":{"0":0.5,"1":0.3,"2":0.2},"confidence":0.7}},"usage":{"input_tokens":150,"output_tokens":20}}""");
}
