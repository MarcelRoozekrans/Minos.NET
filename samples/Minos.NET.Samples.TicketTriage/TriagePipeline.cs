namespace Minos.Samples.TicketTriage;

/// <summary>The escalation pipeline of the sample: a local client, a hosted target for the questions the local one is unsure of.</summary>
public static class TriagePipeline
{
    /// <summary>The local client's name in the path.</summary>
    public const string PrimaryName = "local";

    /// <summary>The hosted target's name in the path.</summary>
    public const string TargetName = "typesafe";

    /// <summary>The time an escalation call is expected to take; with less time left than this the target is not called.</summary>
    public static readonly TimeSpan ExpectedLatency = TimeSpan.FromMilliseconds(300);

    /// <summary>The time one item has for the local call and the escalation together.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The certainty a local answer needs to stand, per question key. They come from the threshold sweep in
    /// <c>docs/reports/local/banking77/report.md</c>: the lowest threshold at which the answers the local model keeps
    /// were right about 99 percent of the time.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Thresholds { get; } = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["intent"] = 0.85,
        ["card_not_arrived"] = 0.80,
        ["about_a_payment"] = 0.80,
    };

    internal static IDecisionClient Build(IDecisionClient local, IDecisionClient target, CallSlot localSlot, CallSlot targetSlot, TimeProvider? time = null)
        => new LocalCaptureClient(local, localSlot, time).AsBuilder()
            .UseEscalation(new TimingDecisionClient(target, targetSlot, time), o =>
            {
                o.PrimaryName = PrimaryName;
                o.TargetName = TargetName;
                o.ExpectedLatency = ExpectedLatency;
                o.Budget = Budget;
                foreach (var (key, threshold) in Thresholds)
                {
                    o.Thresholds[key] = threshold;
                }
            })
            .Build(time is null ? null : new TimeServices(time));

    // Hands the escalation stage the clock it reads the deadline from.
    private sealed class TimeServices(TimeProvider time) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? time : null;
    }
}
