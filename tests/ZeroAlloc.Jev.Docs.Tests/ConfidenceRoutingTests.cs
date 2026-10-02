using System.Globalization;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class ConfidenceRoutingTests
{
    [Theory]
    [InlineData("transfer_money", 0.92, BankingAction.Transfer)]
    [InlineData("transfer_money", 0.85, BankingAction.Transfer)]
    [InlineData("transfer_money", 0.7, BankingAction.ConfirmTransfer)]
    [InlineData("transfer_money", 0.6, BankingAction.ConfirmTransfer)]
    [InlineData("transfer_money", 0.4, BankingAction.HandToHuman)]
    [InlineData("check_balance", 0.55, BankingAction.CheckBalance)]
    [InlineData("check_balance", 0.5, BankingAction.CheckBalance)]
    [InlineData("check_balance", 0.3, BankingAction.HandToHuman)]
    [InlineData("other", 0.95, BankingAction.HandToHuman)]
    public async Task EachActionHasItsOwnGate(string intent, double confidence, BankingAction expected)
    {
        var request = await CannedJev.EvaluateAsync<BankingRequest>(Response(intent, confidence), "Send 200 to Sam.");

        Assert.Equal(expected, BankingRouting.Route(request));
    }

    private static readonly string[] IntentKeys = ["check_balance", "transfer_money", "other"];

    // Builds a one-question response: the chosen intent gets 0.8 and the others share the rest, so the
    // probabilities agree with the choice; the confidence is whatever the test passes.
    private static string Response(string intent, double confidence)
    {
        var probabilities = string.Join(
            ',',
            IntentKeys.Select(key => string.Create(CultureInfo.InvariantCulture, $"\"{key}\":{(string.Equals(key, intent, StringComparison.Ordinal) ? 0.8 : 0.1)}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"{{{intent}}}","probabilities":{{{{probabilities}}}},"confidence":{{{confidence}}}}},"usage":{"input_tokens":90,"output_tokens":12}}""");
    }
}
