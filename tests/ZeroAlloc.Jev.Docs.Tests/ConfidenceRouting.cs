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
            // Reading a balance changes nothing, so the documented defaults are enough and Medium is safe to act on.
            BankingIntent.CheckBalance => ConfidenceThresholds.Default.Classify(intent.Confidence) == ConfidenceTier.Low
                ? BankingAction.HandToHuman
                : BankingAction.CheckBalance,
            BankingIntent.TransferMoney => Transfers.Classify(intent.Confidence) switch
            {
                ConfidenceTier.High => BankingAction.Transfer,
                ConfidenceTier.Medium => BankingAction.ConfirmTransfer,
                _ => BankingAction.HandToHuman,
            },
            _ => BankingAction.HandToHuman,
        };
    }
}
#endregion
