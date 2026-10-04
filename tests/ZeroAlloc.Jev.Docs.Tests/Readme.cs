namespace ZeroAlloc.Jev.Docs.Tests;

#region Readme_Example
using ZeroAlloc.Jev;

public enum Lane
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,
}

[JevQuestions]
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
    // registers it with AddJevClient.
    public static async Task<string> RunAsync(string message, CancellationToken cancellationToken)
    {
        using var jev = new JevClient();
        return await RouteAsync(jev, message, cancellationToken);
    }

    public static async Task<string> RouteAsync(IJevClient jev, string message, CancellationToken cancellationToken)
    {
        var result = await jev.EvaluateAsync<Triage>(message, cancellationToken);
        return result.IsFailure
            ? $"{result.Error.Kind}: {result.Error.Message}"
            : $"urgent: {result.Value.IsUrgent.Value}, queue: {result.Value.Queue.Value}";
    }
}
#endregion
