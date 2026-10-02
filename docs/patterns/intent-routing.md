# Intent routing

Many requests do not need a large language model, and some should not reach one at all. Let Jev make a cheap first
decision about what the request is and how hard it looks, and send it on from there: to plain code for the easy cases,
to a specialist model for the ones that need language, and to a person for the rest.

TypeSafe describes the pattern in [Intent routing](https://docs.typesafe.ai/patterns/intent-routing). This page shows
it with a typed question set and a plain `switch`.

## The questions

An IT helpdesk asks two questions about each ticket in the same request: what does the employee need, and how much
work will it take.

<!-- snippet: IntentRoutingQuestions -->
```cs
public enum HelpdeskIntent
{
    [Criteria("Locked out, or needs a password reset")]
    PasswordReset,

    [Criteria("How to install, use or configure some software")]
    SoftwareQuestion,

    [Criteria("A device or a peripheral is broken or misbehaving")]
    HardwareFault,
}

public enum Effort
{
    [Level("Quick: a few minutes, one step")]
    Quick,

    [Level("Involved: several steps, or some back and forth")]
    Involved,

    [Level("A project: needs planning or several people")]
    Project,
}

[JevQuestions]
public partial record HelpdeskTicket
{
    [Choice("What does the employee need?")]
    public partial Choice<HelpdeskIntent> Intent { get; }

    [Score("How much work will this take to resolve?")]
    public partial Score<Effort> Effort { get; }
}
```
<!-- endSnippet -->

## The routes

Two checks come first, and either one sends the ticket to a person on the service desk. Only after that does the
intent pick a handler: a self-service flow for password resets, an assistant model for software questions, and a
technician for hardware faults.

<!-- snippet: IntentRoutingRules -->
```cs
public enum HelpdeskRoute
{
    SelfServiceReset,
    SoftwareAssistant,
    Technician,
    ServiceDesk,
}

public static class HelpdeskRouting
{
    // Jev is the cheap first step: it decides who handles a ticket, so only the tickets that need a
    // language model or a person reach one.
    public static HelpdeskRoute Route(HelpdeskTicket ticket)
    {
        if (ConfidenceThresholds.Default.Classify(ticket.Intent.Confidence) == ConfidenceTier.Low)
        {
            return HelpdeskRoute.ServiceDesk; // unsure what the employee needs
        }

        if (ticket.Effort.Expected > 1.0)
        {
            return HelpdeskRoute.ServiceDesk; // more than an involved fix
        }

        return ticket.Intent.Value switch
        {
            HelpdeskIntent.PasswordReset => HelpdeskRoute.SelfServiceReset,     // a flow in plain code, no model
            HelpdeskIntent.SoftwareQuestion => HelpdeskRoute.SoftwareAssistant, // a language model with the docs
            HelpdeskIntent.HardwareFault => HelpdeskRoute.Technician,          // someone must look at the device
            _ => HelpdeskRoute.ServiceDesk,
        };
    }
}
```
<!-- endSnippet -->

## C# notes

- Both questions travel in one request, as in [fan-out](fan-out.md), so the effort answer costs no extra call.
- Low confidence on the intent and a large effort both fall back to a person: when Jev is unsure, or the ticket is
  big, the safe route is the expensive one.
- This composes with [confidence routing](confidence-routing.md): give each handler its own thresholds instead of the
  defaults when a wrong route costs more for some of them.
- The effort cut-off of 1.0, "involved", is a starting point. Tune it on your own tickets.
- The same questions can be built at run time instead of declared; the [fan-out](fan-out.md#built-at-run-time) guide
  shows how.
