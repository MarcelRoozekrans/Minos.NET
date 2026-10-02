# Speculative fan-out

Ask every question you might need in a single request, then let your code read only the answers that matter for this
input. Jev answers all of them in one call, and extra questions add little latency or cost compared with separate
requests, so there is no reason to decide up front which questions apply.

TypeSafe describes the pattern in [Speculative fan-out](https://docs.typesafe.ai/patterns/fan-out). This page shows it
with a typed question set.

## The questions

A support ticket gets five questions at once. The severity question only means something for bug reports; it is asked
for every ticket anyway.

<!-- snippet: FanOutQuestions -->
```cs
public enum TicketCategory
{
    [Criteria("Something is broken or behaves wrongly")]
    BugReport,

    [Criteria("Charges, invoices or refunds")]
    Billing,

    [Criteria("A request for something new")]
    FeatureRequest,

    [Criteria("Sign-in, profile or settings")]
    Account,
}

public enum Severity
{
    [Level("Cosmetic: nothing stops working")]
    Cosmetic,

    [Level("Degraded: it works, but badly")]
    Degraded,

    [Level("Blocking: the customer cannot work")]
    Blocking,
}

public enum Mood
{
    [Level("Calm")]
    Calm,

    [Level("Annoyed")]
    Annoyed,

    [Level("Angry")]
    Angry,
}

// Five questions, one request. Some only matter for some tickets: a feature request has no severity
// worth reading. Asking anyway costs almost nothing, and the code below ignores what it doesn't need.
[JevQuestions]
public partial record SupportTicket
{
    [Choice("What is this ticket about?")]
    public partial Choice<TicketCategory> Category { get; }

    [Score("If this reports a bug, how severe is it?")]
    public partial Score<Severity> BugSeverity { get; }

    [Noul("Does the ticket include steps to reproduce the problem?")]
    public partial Noul HasReproSteps { get; }

    [Noul("Does the customer ask for a refund?")]
    public partial Noul AsksForRefund { get; }

    [Score("How does the customer feel?")]
    public partial Score<Mood> Mood { get; }
}
```
<!-- endSnippet -->

## Reading the answers

Routing reads `Expected` on the Scores, the probability-weighted level, so 1.5 means "between degraded and blocking",
and `Probability` on the Nouls. For a billing ticket, the severity answer is present and simply not read.

<!-- snippet: FanOutRouting -->
```cs
[Flags]
public enum TicketActions
{
    None = 0,
    EscalateToEngineering = 1,
    FlagForBilling = 2,
    Prioritize = 4,
}

public static class TicketRouting
{
    public static TicketActions Route(SupportTicket ticket)
    {
        var actions = TicketActions.None;

        // Severity only matters for bugs; for any other category its answer is simply not read.
        if (ticket.Category.Value == TicketCategory.BugReport
            && ticket.BugSeverity.Expected > 1.5
            && ticket.HasReproSteps.Probability > 0.6)
        {
            actions |= TicketActions.EscalateToEngineering;
        }

        if (ticket.AsksForRefund.Probability > 0.7)
        {
            actions |= TicketActions.FlagForBilling;
        }

        if (ticket.Mood.Expected > 1.5)
        {
            actions |= TicketActions.Prioritize;
        }

        return actions;
    }
}
```
<!-- endSnippet -->

## Calling Jev

<!-- snippet: FanOutCall -->
```cs
public static async Task<TicketActions?> TriageAsync(IJevClient jev, string ticketText, CancellationToken ct)
{
    var result = await jev.EvaluateAsync<SupportTicket>(ticketText, ct);
    return result.IsSuccess ? TicketRouting.Route(result.Value) : null; // null: send it to a person
}
```
<!-- endSnippet -->

## C# notes

- Each answer is a struct over the response's shared buffer: reading `Value`, `Expected`, `Confidence` or
  `Probability` allocates nothing.
- The thresholds here (1.5, 0.6, 0.7) are starting points. Tune them on your own tickets.
- The same questions can be built at run time instead of declared; see
  [Question sets built at run time](../../README.md#question-sets-built-at-run-time).
