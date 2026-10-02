namespace ZeroAlloc.Jev.Docs.Tests;

#region FanOutQuestions
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
#endregion

#region FanOutRouting
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
#endregion

public static class TicketTriage
{
    #region FanOutCall
    public static async Task<TicketActions?> TriageAsync(IJevClient jev, string ticketText, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync<SupportTicket>(ticketText, ct);
        // On failure, result.Error.Kind and .Message say why: log them and send the ticket to a person.
        return result.IsSuccess ? TicketRouting.Route(result.Value) : null;
    }
    #endregion
}
