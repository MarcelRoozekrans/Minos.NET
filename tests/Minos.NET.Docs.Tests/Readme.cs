namespace Minos.Docs.Tests;

#region Readme_Example
using Minos;

public enum Lane
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,
}

[Questions]
public partial record Triage
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which queue should handle this?")]
    public partial Choice<Lane> Queue { get; }
}

public static class ReadmeExample
{
    // A one-off run. The client reads TYPESAFE_API_KEY. A long-running app creates one client and shares it, or
    // registers it with AddDecisionClient.
    public static async Task<string> RunAsync(string message, CancellationToken cancellationToken)
    {
        using var jev = new DecisionClient();
        return await RouteAsync(jev, message, cancellationToken);
    }

    public static async Task<string> RouteAsync(IDecisionClient jev, string message, CancellationToken cancellationToken)
    {
        var result = await jev.EvaluateAsync<Triage>(message, cancellationToken);
        return result.IsFailure
            ? $"{result.Error.Kind}: {result.Error.Message}"
            : $"urgent: {result.Value.IsUrgent.Value}, queue: {result.Value.Queue.Value}";
    }
}
#endregion
