# Intent routing

Most requests do not need a large language model, and some should not reach one at all. Let Jev make a cheap first
decision about what the request is and how hard it looks, and send it on from there: to plain code for the easy cases,
to a specialist model for the ones that need language, and to a person for the rest.

TypeSafe describes the pattern in [Intent routing](https://docs.typesafe.ai/patterns/intent-routing). This page shows
it with a typed question set and a plain `switch`.

## The questions

An incoming message gets two questions in the same request: what does the customer want, and how complex is it.

<!-- snippet: IntentRoutingQuestions -->
```cs
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
```
<!-- endSnippet -->

## The routes

Two checks come first, and either one sends the request to a person. Only after that does the intent pick a handler.

<!-- snippet: IntentRoutingRules -->
```cs
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
            RequestIntent.OrderStatus => RequestHandler.OrderLookup,      // plain code and a database query
            RequestIntent.ProductQuestion => RequestHandler.ProductAssistant, // a language model with product context
            _ => RequestHandler.Person,
        };
    }
}
```
<!-- endSnippet -->

## C# notes

- Both questions travel in one request, as in [fan-out](fan-out.md), so the complexity answer costs no extra call.
- Low confidence on the intent and high complexity both fall back to a person: when Jev is unsure, or the request is
  hard, the safe route is the expensive one.
- This composes with [confidence routing](confidence-routing.md): give each handler its own thresholds instead of the
  defaults when a wrong route costs more for some of them.
- The complexity cut-off of 1.0 is a starting point. Tune it on your own requests.
