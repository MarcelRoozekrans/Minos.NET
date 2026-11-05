using Microsoft.Extensions.Logging;

namespace Minos;

/// <summary>Adds <see cref="EscalatingDecisionClient"/> to a pipeline.</summary>
/// <remarks>
/// To name the target by the key of a client registered with <c>AddDecisionClient(name, …)</c>, use the
/// <c>UseEscalation(string targetKey, …)</c> overload in <c>Minos.NET.DependencyInjection</c>.
/// </remarks>
public static class EscalatingDecisionClientBuilderExtensions
{
    /// <summary>Adds a stage that re-asks the questions the inner client is unsure of on <paramref name="target"/>.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="target">The client the unsure questions are re-asked on. The stage does not dispose it.</param>
    /// <param name="configure">
    /// Sets the thresholds, the time settings and the client names on a new <see cref="EscalationOptions"/> for each
    /// <c>Build</c>. It must set <see cref="EscalationOptions.TargetName"/>.
    /// </param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// The stage logs through the build's <see cref="ILoggerFactory"/> and reads the deadline's time from its
    /// <see cref="TimeProvider"/>, when the services have them. Add it before <c>UseRetries</c> and <c>UseCaching</c>, so
    /// it sits outside them and the primary's calls are retried and cached on their own.
    /// </para>
    /// <para>
    /// The settings are checked when the pipeline is built, so a setting out of range throws <see cref="ArgumentException"/>
    /// from <see cref="DecisionClientBuilder.Build"/>. A pipeline holds one escalation stage: <c>Build</c> throws
    /// <see cref="InvalidOperationException"/> when the stages inside this one already hold one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/>, <paramref name="target"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseEscalation(this DecisionClientBuilder builder, IDecisionClient target, Action<EscalationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(builder, _ => target, configure);
    }

    /// <summary>
    /// Adds a stage that re-asks the questions the inner client is unsure of on a target created from the services passed
    /// to <c>Build</c>, such as a client registered in a container.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="target">
    /// Creates the client the unsure questions are re-asked on from the services passed to <c>Build</c>, once per
    /// <c>Build</c>. The stage does not dispose it.
    /// </param>
    /// <param name="configure">
    /// Sets the thresholds, the time settings and the client names on a new <see cref="EscalationOptions"/> for each
    /// <c>Build</c>. It must set <see cref="EscalationOptions.TargetName"/>.
    /// </param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// As <see cref="UseEscalation(DecisionClientBuilder, IDecisionClient, Action{EscalationOptions})"/>. <c>Build</c> also
    /// throws <see cref="InvalidOperationException"/> when <paramref name="target"/> returns <see langword="null"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/>, <paramref name="target"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseEscalation(this DecisionClientBuilder builder, Func<IServiceProvider, IDecisionClient> target, Action<EscalationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(builder, target, configure);
    }

    private static DecisionClientBuilder Add(DecisionClientBuilder builder, Func<IServiceProvider, IDecisionClient> target, Action<EscalationOptions> configure)
        => builder.Use((inner, services) =>
        {
            // The escalation stage answers GetService for its options, through any stages between the two.
            if (inner.GetService<EscalationOptions>() is not null)
            {
                throw new InvalidOperationException(
                    "UseEscalation found an escalation stage inside this one: a pipeline holds one escalation stage.");
            }

            var options = new EscalationOptions();
            configure(options);
            var client = target(services)
                ?? throw new InvalidOperationException("The UseEscalation target factory returned null: return the client to re-ask on.");
            var logger = (services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger(DecisionLog.Category);
            return new EscalatingDecisionClient(inner, client, options, logger, services.GetService(typeof(TimeProvider)) as TimeProvider);
        });
}
