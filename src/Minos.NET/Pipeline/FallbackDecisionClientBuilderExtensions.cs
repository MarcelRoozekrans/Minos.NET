using Microsoft.Extensions.Logging;

namespace Minos;

/// <summary>Adds <see cref="FallbackDecisionClient"/> to a pipeline.</summary>
public static class FallbackDecisionClientBuilderExtensions
{
    /// <summary>Adds a stage that asks each of the configured steps in order when the inner client fails.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">
    /// Adds the steps and sets the names and time settings on a new <see cref="FallbackOptions"/> for each <c>Build</c>.
    /// It must add at least one step.
    /// </param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// The stage logs through the build's <see cref="ILoggerFactory"/> and reads the deadline's time from its
    /// <see cref="TimeProvider"/>, when the services have them. Add it before <c>UseRetries</c> and <c>UseCaching</c>, so
    /// it sits outside them and the inner client's transient failures are retried before the chain moves on to the next step.
    /// </para>
    /// <para>
    /// The settings are checked when the pipeline is built, so a setting out of range throws <see cref="ArgumentException"/>
    /// from <see cref="DecisionClientBuilder.Build"/>. A pipeline holds one fallback stage: <c>Build</c> throws
    /// <see cref="InvalidOperationException"/> when the stages inside this one already hold one. The stage does not dispose
    /// a step's client.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseFallback(this DecisionClientBuilder builder, Action<FallbackOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(builder, _ =>
        {
            var options = new FallbackOptions();
            configure(options);
            return options;
        });
    }

    /// <summary>
    /// Adds a stage that asks each step of the options created from the services passed to <c>Build</c> in order when the
    /// inner client fails, such as steps whose clients are registered in a container.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">
    /// Creates the options, with at least one step, from the services passed to <c>Build</c>, once per <c>Build</c>.
    /// The stage copies them.
    /// </param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// As <see cref="UseFallback(DecisionClientBuilder, Action{FallbackOptions})"/>. <c>Build</c> also throws
    /// <see cref="InvalidOperationException"/> when <paramref name="configure"/> returns <see langword="null"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseFallback(this DecisionClientBuilder builder, Func<IServiceProvider, FallbackOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(builder, services => configure(services)
            ?? throw new InvalidOperationException("The UseFallback options factory returned null: return the options with the steps to ask."));
    }

    private static DecisionClientBuilder Add(DecisionClientBuilder builder, Func<IServiceProvider, FallbackOptions> options)
        => builder.Use((inner, services) =>
        {
            // The fallback stage answers GetService for its options, through any stages between the two.
            if (inner.GetService<FallbackOptions>() is not null)
            {
                throw new InvalidOperationException(
                    "UseFallback found a fallback stage inside this one: a pipeline holds one fallback stage.");
            }

            var created = options(services);
            var logger = (services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger(DecisionLog.Category);
            return new FallbackDecisionClient(inner, created, logger, services.GetService(typeof(TimeProvider)) as TimeProvider);
        });
}
