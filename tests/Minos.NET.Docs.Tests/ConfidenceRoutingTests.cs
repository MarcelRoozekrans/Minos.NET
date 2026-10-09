using System.Globalization;

namespace Minos.Docs.Tests;

public sealed class ConfidenceRoutingTests
{
    // Every threshold is hit on both sides: the defaults, 0.5 and 0.9, through tracking and cancelling, and the
    // refund pair, 0.7 and 0.92, through refunds.
    [Theory]
    [InlineData("track_order", 0.5, ShopAction.ShowTracking)]
    [InlineData("track_order", 0.49, ShopAction.HandToPerson)]
    [InlineData("track_order", 0.9, ShopAction.ShowTracking)]
    [InlineData("track_order", 0.89, ShopAction.ShowTracking)]
    [InlineData("cancel_order", 0.9, ShopAction.CancelOrder)]
    [InlineData("cancel_order", 0.89, ShopAction.ConfirmCancel)]
    [InlineData("cancel_order", 0.5, ShopAction.ConfirmCancel)]
    [InlineData("cancel_order", 0.49, ShopAction.HandToPerson)]
    [InlineData("request_refund", 0.92, ShopAction.IssueRefund)]
    [InlineData("request_refund", 0.91, ShopAction.ConfirmRefund)]
    [InlineData("request_refund", 0.7, ShopAction.ConfirmRefund)]
    [InlineData("request_refund", 0.69, ShopAction.HandToPerson)]
    [InlineData("other", 0.97, ShopAction.HandToPerson)]
    public async Task EachActionHasItsOwnGate(string intent, double confidence, ShopAction expected)
    {
        var message = await CannedDecision.EvaluateAsync<ChatMessage>(Response(intent, confidence), "I want my money back for order 1042.");

        Assert.Equal(expected, ShopRouting.Route(message));
    }

    private static readonly string[] IntentKeys = ["track_order", "cancel_order", "request_refund", "other"];

    // Builds a one-question response. The chosen intent's probability equals the confidence, and the other
    // intents share the rest equally, so the probabilities sum to 1 and agree with the confidence.
    private static string Response(string intent, double confidence)
    {
        var others = (1.0 - confidence) / (IntentKeys.Length - 1);
        var probabilities = string.Join(
            ',',
            IntentKeys.Select(key => string.Create(
                CultureInfo.InvariantCulture,
                $"\"{key}\":{(string.Equals(key, intent, StringComparison.Ordinal) ? confidence : others)}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"{{{intent}}}","probabilities":{{{{probabilities}}}},"confidence":{{{confidence}}}}},"usage":{"input_tokens":90,"output_tokens":12}}""");
    }
}
