namespace ZeroAlloc.Jev.Docs.Tests;

#region ConfidenceRoutingQuestions
public enum BankingIntent
{
    [Criteria("Asks for an account balance")]
    CheckBalance,

    [Criteria("Asks to move money to another account")]
    TransferMoney,

    [Criteria("Anything else")]
    Other,
}

[JevQuestions]
public partial record BankingRequest
{
    [Choice("What does the caller want to do?")]
    public partial Choice<BankingIntent> Intent { get; }
}
#endregion

#region ConfidenceRoutingRules
public enum BankingAction
{
    CheckBalance,
    ConfirmTransfer,
    Transfer,
    HandToHuman,
}

public static class BankingRouting
{
    // Moving money is expensive to get wrong: act only at 0.85 or above, and ask the caller to confirm from 0.6.
    private static readonly ConfidenceThresholds Transfers = new(medium: 0.6, high: 0.85);

    public static BankingAction Route(BankingRequest request)
    {
        var intent = request.Intent;
        return intent.Value switch
        {
            BankingIntent.CheckBalance => RouteBalanceCheck(intent.Confidence),
            BankingIntent.TransferMoney => RouteTransfer(intent.Confidence),
            _ => BankingAction.HandToHuman,
        };
    }

    // Reading a balance changes nothing, so the documented defaults are enough and Medium is safe to act on.
    private static BankingAction RouteBalanceCheck(double confidence)
        => ConfidenceThresholds.Default.Classify(confidence) == ConfidenceTier.Low
            ? BankingAction.HandToHuman
            : BankingAction.CheckBalance;

    private static BankingAction RouteTransfer(double confidence)
        => Transfers.Classify(confidence) switch
        {
            ConfidenceTier.High => BankingAction.Transfer,
            ConfidenceTier.Medium => BankingAction.ConfirmTransfer,
            _ => BankingAction.HandToHuman,
        };
}
#endregion
