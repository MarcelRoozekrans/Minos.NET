namespace Minos.Docs.Tests;

#region ChoosingAProvider_Gates
public enum SupportTopic
{
    [Criterion("The card has not arrived, or the customer asks when it will")]
    CardArrival,

    [Criterion("The card is lost or stolen")]
    LostOrStolenCard,

    [Criterion("A card payment was declined")]
    DeclinedCardPayment,
}

public enum UrgencyLevel
{
    [Level("Routine: a question, with nothing at risk")]
    Routine,

    [Level("Needs action soon: the customer is blocked")]
    Soon,

    [Level("Urgent: money or card security at risk")]
    Urgent,
}

[Questions]
public partial record SupportMessage
{
    [Choice("Which topic is this customer message about?")]
    public partial Choice<SupportTopic> Topic { get; }

    [Score("How urgent is this message?")]
    public partial Score<UrgencyLevel> Urgency { get; }
}

public enum SupportRoute
{
    TopicQueue,
    ConfirmTopic,
    Person,
    OnCall,
}

public static class SupportGates
{
    // Choice answers: act from 0.9, the reference threshold of the Banking77 intent table, and let a person decide
    // below 0.5.
    private static readonly ConfidenceThresholds TopicGate = new(medium: 0.5, high: 0.9);

    // Score answers ran less confident than Choice answers on the urgency set, so their gate is lower: 0.5, the
    // reference threshold of the urgency table. These are starting points; sweep your own data before you copy them.
    private static readonly ConfidenceThresholds UrgencyGate = new(medium: 0.3, high: 0.5);

    public static SupportRoute Route(SupportMessage message)
    {
        if (message.Urgency.Value == UrgencyLevel.Urgent)
        {
            // A lost card that waits costs more than a needless call, so even a Medium urgent answer reaches a person.
            var urgency = UrgencyGate.Classify(message.Urgency.Confidence);
            if (urgency == ConfidenceTier.High)
            {
                return SupportRoute.OnCall;
            }

            if (urgency == ConfidenceTier.Medium)
            {
                return SupportRoute.Person;
            }
        }

        return TopicGate.Classify(message.Topic.Confidence) switch
        {
            ConfidenceTier.High => SupportRoute.TopicQueue,
            ConfidenceTier.Medium => SupportRoute.ConfirmTopic,
            _ => SupportRoute.Person,
        };
    }
}
#endregion
