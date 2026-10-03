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
    // The client reads TYPESAFE_API_KEY. Create it once, share it, and dispose it at shutdown.
    public static async Task<string> RunAsync(string message, CancellationToken ct)
    {
        using var jev = new JevClient();
        return await RouteAsync(jev, message, ct);
    }

    public static async Task<string> RouteAsync(IJevClient jev, string message, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync<Triage>(message, ct);
        return result.IsFailure
            ? $"{result.Error.Kind}: {result.Error.Message}"
            : $"urgent: {result.Value.IsUrgent.Value}, queue: {result.Value.Queue.Value}";
    }
}
#endregion
