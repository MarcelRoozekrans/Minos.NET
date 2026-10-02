using System.Globalization;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class IntentRoutingTests
{
    [Theory]
    [InlineData("password_reset", 0.9, 0.2, HelpdeskRoute.SelfServiceReset)]
    [InlineData("software_question", 0.8, 0.6, HelpdeskRoute.SoftwareAssistant)]
    [InlineData("hardware_fault", 0.85, 0.9, HelpdeskRoute.Technician)]
    [InlineData("hardware_fault", 0.85, 1.0, HelpdeskRoute.Technician)]
    [InlineData("software_question", 0.8, 1.1, HelpdeskRoute.ServiceDesk)]
    [InlineData("password_reset", 0.5, 0.1, HelpdeskRoute.SelfServiceReset)]
    [InlineData("password_reset", 0.49, 0.1, HelpdeskRoute.ServiceDesk)]
    public async Task EachTicketGoesToTheCheapestCapableHandler(
        string intent, double confidence, double effort, HelpdeskRoute expected)
    {
        var ticket = await CannedJev.EvaluateAsync<HelpdeskTicket>(Response(intent, confidence, effort), "I can't sign in to my laptop.");

        Assert.Equal(expected, HelpdeskRouting.Route(ticket));
    }

    private static readonly string[] IntentKeys = ["password_reset", "software_question", "hardware_fault"];

    // Builds a two-question response. The chosen intent's probability equals the confidence and the other intents
    // share the rest equally. The effort probabilities sit on the two levels around the score, so their weighted
    // mean equals it.
    private static string Response(string intent, double confidence, double effort)
    {
        var others = (1.0 - confidence) / (IntentKeys.Length - 1);
        var intentProbabilities = string.Join(
            ',',
            IntentKeys.Select(key => string.Create(
                CultureInfo.InvariantCulture,
                $"\"{key}\":{(string.Equals(key, intent, StringComparison.Ordinal) ? confidence : others)}")));

        var lower = Math.Min(Math.Floor(effort), 1.0);
        var upperShare = effort - lower;
        var effortProbabilities = lower < 1.0
            ? string.Create(CultureInfo.InvariantCulture, $"\"0\":{1.0 - upperShare},\"1\":{upperShare},\"2\":0")
            : string.Create(CultureInfo.InvariantCulture, $"\"0\":0,\"1\":{1.0 - upperShare},\"2\":{upperShare}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"{{{intent}}}","probabilities":{{{{intentProbabilities}}}},"confidence":{{{confidence}}}},"effort":{"type":"score","score":{{{effort}}},"legend":{"0":"Quick: a few minutes, one step","1":"Involved: several steps, or some back and forth","2":"A project: needs planning or several people"},"probabilities":{{{{effortProbabilities}}}},"confidence":0.7}},"usage":{"input_tokens":150,"output_tokens":20}}""");
    }
}
