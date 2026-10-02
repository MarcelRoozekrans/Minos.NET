namespace ZeroAlloc.Jev.Docs.Tests;

#region IntentRoutingQuestions
public enum RequestIntent
{
    [Criteria("Where an order is, or when it arrives")]
    OrderStatus,

    [Criteria("How a product works or what it includes")]
    ProductQuestion,

    [Criteria("A complaint or a dispute")]
    Complaint,
}

public enum Complexity
{
    [Level("Simple: one fact answers it")]
    Simple,

    [Level("Moderate: needs some context")]
    Moderate,

    [Level("Complex: needs judgement")]
    Complex,
}

[JevQuestions]
public partial record IncomingRequest
{
    [Choice("What does the customer want?")]
    public partial Choice<RequestIntent> Intent { get; }

    [Score("How complex is the request?")]
    public partial Score<Complexity> Complexity { get; }
}
#endregion

#region IntentRoutingRules
public enum RequestHandler
{
    OrderLookup,
    ProductAssistant,
    Person,
}

public static class RequestRouting
{
    // Jev is the cheap first step: it decides who handles a request, and only the requests that need a
    // language model or a person reach one.
    public static RequestHandler Route(IncomingRequest request)
    {
        if (ConfidenceThresholds.Default.Classify(request.Intent.Confidence) == ConfidenceTier.Low)
        {
            return RequestHandler.Person; // unsure what the customer wants
        }

        if (request.Complexity.Expected > 1.0)
        {
            return RequestHandler.Person; // more than moderately complex
        }

        return request.Intent.Value switch
        {
            RequestIntent.OrderStatus => RequestHandler.OrderLookup,          // plain code and a database query
            RequestIntent.ProductQuestion => RequestHandler.ProductAssistant, // a language model with product context
            _ => RequestHandler.Person,
        };
    }
}
#endregion
