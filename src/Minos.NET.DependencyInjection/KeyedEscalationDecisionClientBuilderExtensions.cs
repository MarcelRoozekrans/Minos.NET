using Microsoft.Extensions.DependencyInjection;
using Minos.DependencyInjection;

namespace Minos;

/// <summary>Adds an <see cref="EscalatingDecisionClient"/> whose target is a keyed client registered in the container.</summary>
public static class KeyedEscalationDecisionClientBuilderExtensions
{
    /// <summary>
    /// Adds a stage that re-asks the questions the inner client is unsure of on the <see cref="IDecisionClient"/> keyed
    /// <paramref name="targetKey"/>, such as one registered with <c>AddDecisionClient(targetKey, …)</c>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="targetKey">The target client's service key, which also names it in <see cref="DecisionResponse.Path"/>.</param>
    /// <param name="configure">
    /// Sets the thresholds, the time settings and the primary's name on a new <see cref="EscalationOptions"/> for each
    /// <c>Build</c>; <see langword="null"/> keeps the defaults. <see cref="EscalationOptions.TargetName"/> is set to
    /// <paramref name="targetKey"/> after it runs, so a name it sets is replaced.
    /// </param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// The target is resolved from the services passed to <c>Build</c>, which the container passes when it first resolves
    /// the client this builder registers. The container owns the target: the stage does not dispose it.
    /// </para>
    /// <para>
    /// <c>Build</c> throws <see cref="InvalidOperationException"/> when the services have no client keyed
    /// <paramref name="targetKey"/>, when the target escalates back to this client through other clients, and when the
    /// stages inside this one already hold an escalation stage; it throws
    /// <see cref="ArgumentException"/> for a setting out of range, as
    /// <see cref="EscalatingDecisionClientBuilderExtensions.UseEscalation(DecisionClientBuilder, Func{IServiceProvider, IDecisionClient}, Action{EscalationOptions})"/> does.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="targetKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetKey"/> is empty, or is the name of the registration <paramref name="builder"/> belongs to, so the
    /// client would escalate to itself.
    /// </exception>
    public static DecisionClientBuilder UseEscalation(this DecisionClientBuilder builder, string targetKey, Action<EscalationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(targetKey);
        if (builder is DecisionClientServiceBuilder { Name: { } name } && string.Equals(name, targetKey, StringComparison.Ordinal))
        {
            // Resolving the target would build this same registration again, without end.
            throw new ArgumentException($"The decision client '{targetKey}' cannot escalate to itself.", nameof(targetKey));
        }

        return builder.UseEscalation(
            services => Resolve(services, targetKey),
            options =>
            {
                configure?.Invoke(options);
                options.TargetName = targetKey;
            });
    }

    // The keys whose targets this thread is resolving. A keyed client is a factory registration, whose cycles the container
    // does not detect, and it builds its pipeline synchronously on the resolving thread, so a key met again is a cycle.
    [ThreadStatic]
    private static HashSet<string>? t_resolving;

    private static IDecisionClient Resolve(IServiceProvider services, string targetKey)
    {
        var resolving = t_resolving ??= new HashSet<string>(StringComparer.Ordinal);
        if (!resolving.Add(targetKey))
        {
            throw new InvalidOperationException(
                $"UseEscalation found a cycle: the decision client '{targetKey}' escalates back to itself through other clients.");
        }

        try
        {
            return (services is IKeyedServiceProvider keyed ? keyed.GetKeyedService(typeof(IDecisionClient), targetKey) as IDecisionClient : null)
                ?? throw new InvalidOperationException(
                    $"UseEscalation found no decision client keyed '{targetKey}': register one with AddDecisionClient(\"{targetKey}\", …) in the services passed to Build.");
        }
        finally
        {
            resolving.Remove(targetKey);
        }
    }
}
