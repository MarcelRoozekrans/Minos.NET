using System.Threading.RateLimiting;
using Minos.RateLimiting;

namespace Minos.AotSmoke;

/// <summary>The rate-limiting stage under Native AOT: a granted call, a rejected one, and the builder extension.</summary>
internal static class RateLimitingChecks
{
    [Covers("Minos.RateLimiting.RateLimitingDecisionClient.RateLimitingDecisionClient(Minos.IDecisionClient! innerClient, System.Threading.RateLimiting.RateLimiter! limiter) -> void")]
    [Covers("override Minos.RateLimiting.RateLimitingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("static Minos.RateLimiting.RateLimitingDecisionClientBuilderExtensions.UseRateLimiting(this Minos.DecisionClientBuilder! builder, System.Threading.RateLimiting.RateLimiter! limiter) -> Minos.DecisionClientBuilder!")]
    public static async Task GrantedCallsGoAndARejectedOneIsNotSent()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
            AutoReplenishment = false,
        });
        var inner = new FixedResponseClient(PipelineChecks.TriageResponse());
        using var direct = new RateLimitingDecisionClient(inner, limiter);
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        var granted = await direct.EvaluateAsync(request).ConfigureAwait(false);
        using var built = inner.AsBuilder().UseRateLimiting(limiter).Build();
        var rejected = await built.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            granted.IsSuccess
                && rejected.IsFailure
                && rejected.Error.Kind == DecisionErrorKind.RateLimited
                && rejected.Error.StatusCode is null
                && string.Equals(rejected.Error.Message, RateLimitingDecisionClient.RejectedMessage, StringComparison.Ordinal),
            "the rate-limiting stage sends a granted call and rejects one over the limit with RateLimited under Native AOT");
    }
}
