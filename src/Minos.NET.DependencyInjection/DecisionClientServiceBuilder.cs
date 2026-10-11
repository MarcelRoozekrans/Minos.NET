namespace Minos;

/// <summary>The pipeline builder of one <c>AddDecisionClient</c> registration.</summary>
/// <remarks>
/// Stages added here wrap the registered client. The container builds the pipeline once, when the client is first
/// resolved, so stages read its services: <c>UseLogging()</c> its
/// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> and <c>UseRetries()</c> its <see cref="TimeProvider"/>.
/// A protocol package's registration returns a builder derived from this one, such as <c>SystemOneClientServiceBuilder</c>.
/// </remarks>
public class DecisionClientServiceBuilder : DecisionClientBuilder
{
    internal DecisionClientServiceBuilder(string? name, Func<IServiceProvider, IDecisionClient> innerClientFactory)
        : base(innerClientFactory)
    {
        Name = name;
    }

    /// <summary>Gets the registration's name, or <see langword="null"/> for the default registration.</summary>
    public string? Name { get; }
}
