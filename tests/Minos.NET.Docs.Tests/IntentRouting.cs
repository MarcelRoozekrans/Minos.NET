namespace Minos.Docs.Tests;

#region IntentRoutingQuestions
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

[Questions]
public partial record HelpdeskTicket
{
    [Choice("What does the employee need?")]
    public partial Choice<HelpdeskIntent> Intent { get; }

    [Score("How much work will this take to resolve?")]
    public partial Score<Effort> Effort { get; }
}
#endregion

#region IntentRoutingRules
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
#endregion
