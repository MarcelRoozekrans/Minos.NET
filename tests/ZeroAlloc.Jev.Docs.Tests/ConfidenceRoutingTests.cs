using System.Globalization;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class ConfidenceRoutingTests
{
    [Theory]
    [InlineData("transfer_money", 0.92, BankingAction.Transfer)]
    [InlineData("transfer_money", 0.85, BankingAction.Transfer)]
    [InlineData("transfer_money", 0.7, BankingAction.ConfirmTransfer)]
    [InlineData("transfer_money", 0.4, BankingAction.HandToHuman)]
    [InlineData("check_balance", 0.55, BankingAction.CheckBalance)]
    [InlineData("check_balance", 0.3, BankingAction.HandToHuman)]
    [InlineData("other", 0.95, BankingAction.HandToHuman)]
    public async Task EachActionHasItsOwnGate(string intent, double confidence, BankingAction expected)
    {
        var request = await CannedJev.EvaluateAsync<BankingRequest>(Response(intent, confidence), "Send 200 to Sam.");

        Assert.Equal(expected, BankingRouting.Route(request));
    }

    private static string Response(string intent, double confidence)
        => string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"{{{intent}}}","probabilities":{"check_balance":0.1,"transfer_money":0.8,"other":0.1},"confidence":{{{confidence}}}}},"usage":{"input_tokens":90,"output_tokens":12}}""");
}
