namespace ZeroAlloc.Jev.Samples.IntentRouting;

/// <summary>Twelve authored travel requests, from a plain lookup to noise.</summary>
public static class Requests
{
    public static IReadOnlyList<(string Id, string Text)> All { get; } =
    [
        ("t01", "Can you show me my booking for Lisbon next month? The reference is QX4T7."),
        ("t02", "What time does my flight to Oslo leave tomorrow morning? Booking PL8820."),
        ("t03", "The name on my ticket is spelled wrong. Can you correct it?"),
        ("t04", "Please move my hotel check-in from the 12th to the 14th."),
        ("t05", "My flight leaves in six hours and I need to add a checked bag."),
        ("t06", "You charged me twice for the same booking. I want one payment back."),
        ("t07", "I cancelled within the free period and was still charged 80 euros."),
        ("t08", "Where can I find my booking confirmation email?"),
        ("t09", "Do you sell travel insurance?"),
        ("t10", "Is my booking to Rome still on? I fly tonight."),
        ("t11", "Could I get an aisle seat instead of the window seat?"),
        ("t12", "asdf"),
    ];
}
