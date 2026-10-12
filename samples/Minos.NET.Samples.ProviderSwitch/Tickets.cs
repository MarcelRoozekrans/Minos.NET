namespace Minos.Samples.ProviderSwitch;

/// <summary>Eight authored support tickets, two for each team.</summary>
public static class Tickets
{
    public static IReadOnlyList<(string Id, string Text)> All { get; } =
    [
        ("s01", "I was charged twice for the March invoice. Please refund one of the two payments."),
        ("s02", "Exporting a report to CSV fails with an error 500 every time."),
        ("s03", "I forgot my password and the reset email never arrives."),
        ("s04", "We are a team of 40. Can you send us a quote for the yearly plan?"),
        ("s05", "The app closes as soon as I upload a photo larger than 10 MB."),
        ("s06", "Please remove a former colleague from our workspace. She left last week."),
        ("s07", "Do you offer a discount for non-profit organisations?"),
        ("s08", "The invoice still shows our old company address. Can you correct it?"),
    ];
}
