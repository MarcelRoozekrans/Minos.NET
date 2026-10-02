namespace ZeroAlloc.Jev.Samples.IntentRouting;

public enum TravelIntent
{
    [Criteria("Wants to see or look up an existing booking")]
    LookUpBooking,

    [Criteria("Wants to change dates, names, seats or luggage on a booking")]
    ChangeBooking,

    [Criteria("Disputes a charge or asks for money back")]
    DisputeCharge,

    [Criteria("Anything else")]
    Other,
}

[JevQuestions]
public partial record TravelRequest
{
    [Choice("What does the traveller want?")]
    public partial Choice<TravelIntent> Intent { get; }

    [Noul("Does the request mention travelling within the next 24 hours?")]
    public partial Noul TravelsWithin24Hours { get; }
}

public enum RequestHandler
{
    /// <summary>Plain code: a database lookup, no language model.</summary>
    BookingLookup,

    /// <summary>A language model with the booking as context.</summary>
    AssistantModel,

    /// <summary>A person on the support team.</summary>
    Person,
}

public static class TravelRouting
{
    public static (RequestHandler Handler, ConfidenceTier Tier, bool Urgent) Route(TravelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tier = ConfidenceThresholds.Default.Classify(request.Intent.Confidence);
        var urgent = request.TravelsWithin24Hours.Probability >= 0.5;
        if (tier == ConfidenceTier.Low)
        {
            return (RequestHandler.Person, tier, urgent); // unsure what the traveller wants
        }

        var handler = request.Intent.Value switch
        {
            TravelIntent.LookUpBooking => RequestHandler.BookingLookup,
            TravelIntent.ChangeBooking => RequestHandler.AssistantModel,
            _ => RequestHandler.Person, // disputes, and anything we have no flow for
        };
        return (handler, tier, urgent);
    }
}
