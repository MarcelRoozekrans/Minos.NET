namespace Minos.Docs.Tests;

#region ConfidenceRoutingQuestions
public enum ShopIntent
{
    [Criteria("Wants to know where an order is or when it arrives")]
    TrackOrder,

    [Criteria("Wants to cancel an order")]
    CancelOrder,

    [Criteria("Wants money back for an order")]
    RequestRefund,

    [Criteria("Anything else")]
    Other,
}

[Questions]
public partial record ChatMessage
{
    [Choice("What does the customer want to do?")]
    public partial Choice<ShopIntent> Intent { get; }
}
#endregion

#region ConfidenceRoutingRules
public enum ShopAction
{
    ShowTracking,
    ConfirmCancel,
    CancelOrder,
    ConfirmRefund,
    IssueRefund,
    HandToPerson,
}

public static class ShopRouting
{
    // A refund sends money out and is hard to take back: issue it only from 0.92, and ask the customer to
    // confirm from 0.7.
    private static readonly ConfidenceThresholds Refunds = new(medium: 0.7, high: 0.92);

    public static ShopAction Route(ChatMessage message)
    {
        var intent = message.Intent;
        return intent.Value switch
        {
            ShopIntent.TrackOrder => RouteTracking(intent.Confidence),
            ShopIntent.CancelOrder => RouteCancel(intent.Confidence),
            ShopIntent.RequestRefund => RouteRefund(intent.Confidence),
            _ => ShopAction.HandToPerson,
        };
    }

    // Showing where an order is changes nothing, so the default thresholds are enough and Medium is safe to act on.
    private static ShopAction RouteTracking(double confidence)
        => ConfidenceThresholds.Default.Classify(confidence) == ConfidenceTier.Low
            ? ShopAction.HandToPerson
            : ShopAction.ShowTracking;

    // A cancelled order can be placed again, so cancelling uses the default thresholds, but Medium asks first.
    private static ShopAction RouteCancel(double confidence)
        => ConfidenceThresholds.Default.Classify(confidence) switch
        {
            ConfidenceTier.High => ShopAction.CancelOrder,
            ConfidenceTier.Medium => ShopAction.ConfirmCancel,
            _ => ShopAction.HandToPerson,
        };

    private static ShopAction RouteRefund(double confidence)
        => Refunds.Classify(confidence) switch
        {
            ConfidenceTier.High => ShopAction.IssueRefund,
            ConfidenceTier.Medium => ShopAction.ConfirmRefund,
            _ => ShopAction.HandToPerson,
        };
}
#endregion
