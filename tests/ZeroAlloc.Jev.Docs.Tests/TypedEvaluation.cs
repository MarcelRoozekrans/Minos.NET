namespace ZeroAlloc.Jev.Docs.Tests;

#region TypedEvaluation_State
using System.Text.Json;
using System.Text.Json.Serialization;
using ZeroAlloc.Jev;

// The state is what Jev reads: any type that serializes to a JSON object, array or string.
public sealed record SupportTicket(string Subject, string Body, string Plan);

// Source-generated JSON metadata for the state, so serializing it needs no reflection.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SupportTicket))]
internal sealed partial class SupportTicketJson : JsonSerializerContext;
#endregion

#region TypedEvaluation_Questions
public enum Desk
{
    [Criteria(
        "Payments, invoices and refunds",
        Examples = ["I was charged twice"],
        NotFor = ["How much is the Pro plan?"])]
    Billing,

    [Criteria("Bugs, outages and integrations", Examples = ["The API returns a 500"])]
    Technical,

    [Criteria("Pricing, upgrades and new accounts")]
    Sales,

    // A member with no [Criteria] is a valid option, sent with no description.
    Other,
}

public enum Impact
{
    [Level("Cosmetic or minor", NotFor = ["Data loss"])]
    Low,

    [Level("Slows the customer down")]
    Medium,

    [Level("Blocks the customer's work")]
    High,
}

// State = typeof(SupportTicket) links the questions to the state type. The backticked names in the instructions
// are checked against that type's members when you build.
[JevQuestions(State = typeof(SupportTicket))]
public partial record TicketReview
{
    [Noul(
        "Is the `body` urgent, for a customer on the `plan` they have?",
        True = "The customer needs help right away",
        False = "The customer can wait")]
    public partial Noul IsUrgent { get; }

    [Choice("Which desk should handle this?")]
    public partial Choice<Desk> Desk { get; }

    [Score("How much does this affect the customer?", Key = "impact")]
    public partial Score<Impact> Impact { get; }
}
#endregion

#region TypedEvaluation_Stateless
// Without State, the questions stand alone, and any text or JSON can be the state.
[JevQuestions]
public partial record UrgencyCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}
#endregion

public static class TicketReviewing
{
    #region TypedEvaluation_Evaluate
    public static async Task<(bool Urgent, Desk Desk, Impact Impact)?> ReviewAsync(
        IJevClient jev, SupportTicket ticket, CancellationToken ct)
    {
        // The state type and its JSON metadata travel together. The call serializes the ticket and sends it
        // with the questions.
        var result = await jev.EvaluateAsync<TicketReview, SupportTicket>(
            ticket, SupportTicketJson.Default.SupportTicket, ct);
        if (result.IsFailure)
        {
            return null;
        }

        var review = result.Value;
        return (review.IsUrgent.Value, review.Desk.Value, review.Impact.Value);
    }
    #endregion

    #region TypedEvaluation_OtherStates
    // A question set without a State type takes its state as text, as a JsonElement or as UTF-8 JSON.
    public static async Task<int> EvaluateEachFormAsync(
        IJevClient jev, string text, JsonElement element, ReadOnlyMemory<byte> utf8Json, CancellationToken ct)
    {
        var succeeded = 0;

        var fromText = await jev.EvaluateAsync<UrgencyCheck>(text, ct);
        succeeded += fromText.IsSuccess ? 1 : 0;

        var fromElement = await jev.EvaluateAsync<UrgencyCheck>(element, ct);
        succeeded += fromElement.IsSuccess ? 1 : 0;

        var fromUtf8 = await jev.EvaluateUtf8Async<UrgencyCheck>(utf8Json, ct);
        succeeded += fromUtf8.IsSuccess ? 1 : 0;

        return succeeded;
    }
    #endregion

    #region TypedEvaluation_Content
    // JevContent holds a state, a question's instructions or a description: text, or a JSON object or array.
    public static JevContent[] ContentForms(SupportTicket ticket)
    {
        return
        [
            JevContent.FromString("Help! My payouts have been failing for 3 days."),
            JevContent.FromValue(ticket, SupportTicketJson.Default.SupportTicket),
            JevContent.FromUtf8Json("""{"subject":"Payouts failing"}"""u8),
        ];
    }
    #endregion
}
