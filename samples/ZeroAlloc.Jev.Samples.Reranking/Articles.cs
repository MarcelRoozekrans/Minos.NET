namespace ZeroAlloc.Jev.Samples.Reranking;

public sealed record Article(string Id, string Title, string Body);

/// <summary>Twenty-five authored help-centre articles for the fictional bike-sharing app Pedalo.</summary>
public static class Articles
{
    public static IReadOnlyList<Article> All { get; } =
    [
        new("a01", "Starting a ride", "Scan the QR code on the handlebar or type the bike number into the app. The bike beeps and the lock opens within a few seconds."),
        new("a02", "Ending a ride", "Park the bike inside a parking zone and push the lock shut. The trip ends once the app shows the summary screen."),
        new("a03", "Parking zones", "Bikes must be left in a marked zone, shown in blue on the map. Parking outside a zone adds a fee."),
        new("a04", "A trip that will not stop", "If the clock keeps running after you walked away, make sure the bracket clicked into place inside a zone, then tap End trip again. Support can stop the clock for you when it still refuses."),
        new("a05", "Pausing a ride", "Tap Pause to keep the bike reserved while you run an errand. Pausing is billed at a lower rate for up to thirty minutes."),
        new("a06", "The monthly pass", "The monthly pass gives unlimited rides under forty minutes for a fixed fee. Buy it from the Pass tab in the app."),
        new("a07", "Pass renewal", "Your pass renews on the same date each month and the fee is taken from your saved card. A reminder arrives three days before."),
        new("a08", "Cancelling the pass", "To stop it before the next charge, open the Pass tab and tap Cancel pass. You keep the benefits until the end of the period you already paid for."),
        new("a09", "Refund for a faulty bike", "If a bike turned out to be broken and you were billed for the trip, report it within a day and the fare is returned to your original payment method."),
        new("a10", "Reporting a damaged bike", "Tap Report a problem on the bike screen and pick what is wrong, such as a bent wheel, a loose chain or a broken light. A photo helps us repair it quickly."),
        new("a11", "A lost item on a bike", "Left something in the basket? Contact support with the bike number and the time of your trip, and we will check with the team that collects the bikes."),
        new("a12", "The bike will not unlock", "When the lock stays shut, check that your phone has a connection, then scan the code again. If it still does not open, pick another bike nearby."),
        new("a13", "The app cannot find bikes", "Turn on location sharing for Pedalo and make sure you are inside the service area. The map refreshes every few seconds."),
        new("a14", "Payment methods", "You can pay with a credit card, a debit card or a mobile wallet. Add or remove methods under Payment in the settings."),
        new("a15", "A card payment failed", "A declined payment usually means an expired card or too little balance. Update the card and the app will retry the open amount."),
        new("a16", "Invoices for business use", "Add your company name and VAT number under Billing, and a proper invoice is created for every month of riding. Download it as a PDF from the Billing page."),
        new("a17", "The student discount", "Verify your school email address once a year to get twenty percent off every ride and pass."),
        new("a18", "Riding in the rain", "Bikes work fine in wet weather, but brakes need more distance, so ride slowly and keep clear of puddles over tram tracks."),
        new("a19", "Helmet rules", "Local law decides whether a helmet is required. Pedalo does not provide helmets, so bring your own for longer rides."),
        new("a20", "Child seats", "Selected bikes carry a seat for children between one and five years. Filter the map by the child icon to find them."),
        new("a21", "Adding a second rider", "Every person needs a profile and a bike of their own, but up to four people can be grouped into one shared trip and pay together from the host account."),
        new("a22", "Deleting your account", "Go to Settings, then Privacy, and choose Delete account. Your ride history is removed after thirty days."),
        new("a23", "Changing your email address", "Open Profile and tap the email field. We send a code to the new address to confirm the change."),
        new("a24", "Low battery on an e-bike", "The screen shows the remaining range. When the battery runs out you can still pedal, but without help from the motor, and you may park at any charging point for a credit."),
        new("a25", "The daily price cap", "Rides never cost more than a set amount per day. Once the cap is reached, further trips that day are free."),
    ];
}
