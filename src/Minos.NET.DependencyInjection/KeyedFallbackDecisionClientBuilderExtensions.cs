using Microsoft.Extensions.DependencyInjection;

namespace Minos;

/// <summary>Adds a <see cref="FallbackDecisionClient"/> whose steps are keyed clients registered in the container.</summary>
public static class KeyedFallbackDecisionClientBuilderExtensions
{
    /// <summary>
    /// Adds a stage that asks the <see cref="IDecisionClient"/>s keyed by <paramref name="steps"/>, in order, when the inner
    /// client fails, such as ones registered with <c>AddDecisionClient(key, …)</c>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="steps">The steps to ask, in order; at least one. Each key also names its step in <see cref="DecisionResponse.Path"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// As <see cref="UseFallback(DecisionClientBuilder, Action{FallbackOptions}, KeyedFallbackStep[])"/> with the default
    /// settings.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="steps"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseFallback(this DecisionClientBuilder builder, params KeyedFallbackStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(steps);
        return UseFallbackCore(builder, null, steps);
    }

    /// <summary>
    /// Adds a stage that asks the <see cref="IDecisionClient"/>s keyed by <paramref name="steps"/>, in order, when the inner
    /// client fails, with <paramref name="configure"/> setting the primary's name, the budget and the failure predicate.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">
    /// Sets <see cref="FallbackOptions.PrimaryName"/>, <see cref="FallbackOptions.Budget"/> and
    /// <see cref="FallbackOptions.ShouldFallBack"/> on a new <see cref="FallbackOptions"/> for each <c>Build</c>. The keyed
    /// steps are added after it runs, so steps it adds come first.
    /// </param>
    /// <param name="steps">The steps to ask, in order; at least one. Each key also names its step in <see cref="DecisionResponse.Path"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// Each step's client is resolved from the services passed to <c>Build</c>, which the container passes when it first
    /// resolves the client this builder registers. The container owns the clients: the stage does not dispose them.
    /// </para>
    /// <para>
    /// <c>Build</c> throws <see cref="InvalidOperationException"/> when the services have no client keyed by a step's key,
    /// and when a step falls back to this client through other clients; the other checks are those of
    /// <see cref="FallbackDecisionClientBuilderExtensions.UseFallback(DecisionClientBuilder, Func{IServiceProvider, FallbackOptions})"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/>, <paramref name="configure"/> or <paramref name="steps"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseFallback(this DecisionClientBuilder builder, Action<FallbackOptions> configure, params KeyedFallbackStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(steps);
        return UseFallbackCore(builder, configure, steps);
    }

    private static DecisionClientBuilder UseFallbackCore(DecisionClientBuilder builder, Action<FallbackOptions>? configure, KeyedFallbackStep[] steps)
    {
        var copy = (KeyedFallbackStep[])steps.Clone();
        foreach (var step in copy)
        {
            ArgumentException.ThrowIfNullOrEmpty(step.Key, nameof(steps));
        }

        return builder.UseFallback(services =>
        {
            var options = new FallbackOptions();
            configure?.Invoke(options);
            foreach (var step in copy)
            {
                options.Steps.Add(new FallbackStep(step.Key, Resolve(services, step.Key)) { ExpectedLatency = step.ExpectedLatency });
            }

            return options;
        });
    }

    // The keys whose clients this thread is resolving. A keyed client is a factory registration, whose cycles the container
    // does not detect, and it builds its pipeline synchronously on the resolving thread, so a key met again is a cycle.
    [ThreadStatic]
    private static HashSet<string>? t_resolving;

    private static IDecisionClient Resolve(IServiceProvider services, string key)
    {
        var resolving = t_resolving ??= new HashSet<string>(StringComparer.Ordinal);
        if (!resolving.Add(key))
        {
            throw new InvalidOperationException(
                $"UseFallback found a cycle: the decision client '{key}' falls back to itself through other clients.");
        }

        try
        {
            return (services is IKeyedServiceProvider keyed ? keyed.GetKeyedService(typeof(IDecisionClient), key) as IDecisionClient : null)
                ?? throw new InvalidOperationException(
                    $"UseFallback found no decision client keyed '{key}': register one with AddDecisionClient(\"{key}\", …) in the services passed to Build.");
        }
        finally
        {
            resolving.Remove(key);
        }
    }
}
