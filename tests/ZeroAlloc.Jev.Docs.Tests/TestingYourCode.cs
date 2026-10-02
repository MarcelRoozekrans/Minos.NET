namespace ZeroAlloc.Jev.Docs.Tests;

#region TestingYourCode_Triager
using ZeroAlloc.Jev;

public enum TriageDesk
{
    Billing,
    Technical,
}

// Two questions about a ticket's text. The wire keys are the property names in snake_case: is_urgent and desk.
[JevQuestions]
public partial record TriageQuestions
{
    [Noul("Does this ticket need help right away?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which desk should handle this ticket?")]
    public partial Choice<TriageDesk> Desk { get; }
}

public enum TriageRoute
{
    Escalate,
    Queue,
    Review,
}

// The class under test. It asks for an IJevClient, so a test can hand it any implementation.
public sealed class TicketTriager(IJevClient jev)
{
    public async Task<TriageRoute> RouteAsync(string ticketText, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync<TriageQuestions>(ticketText, ct);
        if (result.IsFailure)
        {
            // Jev could not answer, so a person looks at the ticket.
            return TriageRoute.Review;
        }

        var answers = result.Value;
        if (answers.IsUrgent.Value)
        {
            return TriageRoute.Escalate;
        }

        // An unsure desk pick is not acted on.
        return answers.Desk.Confidence < 0.6 ? TriageRoute.Review : TriageRoute.Queue;
    }
}
#endregion

public sealed class TestingYourCodeFakeTests
{
    #region TestingYourCode_FakeTests
    [Fact]
    public async Task AnUrgentTicket_IsEscalated_AndTheTicketTextIsWhatWasAsked()
    {
        var jev = FakeJev.Answering(urgent: 0.92, TriageDesk.Billing, deskConfidence: 0.8);
        var triager = new TicketTriager(jev);

        var route = await triager.RouteAsync("Payouts have been failing for 3 days.", CancellationToken.None);

        Assert.Equal(TriageRoute.Escalate, route);
        Assert.Collection(jev.Requests, request =>
        {
            Assert.True(request.State.TryGetString(out var state));
            Assert.Equal("Payouts have been failing for 3 days.", state);
        });
    }

    [Fact]
    public async Task WhenJevFails_ThePersonReviews()
    {
        var triager = new TicketTriager(FakeJev.Failing(JevErrorKind.Network));

        var route = await triager.RouteAsync("Any ticket.", CancellationToken.None);

        Assert.Equal(TriageRoute.Review, route);
    }
    #endregion

    #region TestingYourCode_Pinning
    // Each row is one canned answer and the decision the code must make from it, including the cut-offs themselves.
    [Theory]
    [InlineData(0.50, 0.9, TriageRoute.Escalate)] // a Noul is true at 0.5 or more
    [InlineData(0.49, 0.9, TriageRoute.Queue)]
    [InlineData(0.10, 0.60, TriageRoute.Queue)] // the desk is trusted at 0.6 or more
    [InlineData(0.10, 0.59, TriageRoute.Review)]
    public async Task EachAnswer_PinsOneDecision(double urgent, double deskConfidence, TriageRoute expected)
    {
        var triager = new TicketTriager(FakeJev.Answering(urgent, TriageDesk.Technical, deskConfidence));

        Assert.Equal(expected, await triager.RouteAsync("A ticket.", CancellationToken.None));
    }
    #endregion
}
