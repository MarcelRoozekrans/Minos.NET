namespace Minos.Samples.ChatClient;

public enum SupportTeam
{
    [Criterion("Invoices, payments, refunds and charges")]
    Billing,

    [Criterion("Errors, crashes and how a feature works")]
    Technical,

    [Criterion("Signing in, passwords and permissions")]
    Account,
}

/// <summary>The two questions every support ticket gets.</summary>
[Questions]
public partial record TicketTriage
{
    [Noul("Does the customer ask for money back?")]
    public partial Noul WantsRefund { get; }

    [Choice("Which team should handle this support ticket?")]
    public partial Choice<SupportTeam> Team { get; }
}

public static class Tickets
{
    public static IReadOnlyList<string> All { get; } =
    [
        "I was charged twice for my March invoice and I want my money back.",
        "The export button shows an error every time I click it.",
        "I cannot sign in since I changed phones, and the reset email never arrives.",
    ];
}
