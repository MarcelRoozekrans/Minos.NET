using Microsoft.Extensions.DependencyInjection;

namespace Minos.SystemOne;

/// <summary>The pipeline builder of one <c>AddSystemOneClient</c> registration, with the builder of its <see cref="System.Net.Http.HttpClient"/>.</summary>
/// <remarks>
/// Stages added here wrap the registered <see cref="SystemOneClient"/>, which runs its own standard pipeline unless
/// <see cref="SystemOneClientOptions.UseStandardPipeline"/> is <see langword="false"/>. The container builds the pipeline
/// once, when the client is first resolved, so stages read its services: <c>UseLogging()</c> its
/// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> and <c>UseRetries()</c> its <see cref="TimeProvider"/>.
/// </remarks>
public sealed class SystemOneClientServiceBuilder : DecisionClientServiceBuilder
{
    internal SystemOneClientServiceBuilder(IHttpClientBuilder httpClient, string? name, Func<IServiceProvider, IDecisionClient> factory)
        : base(name, factory)
    {
        HttpClient = httpClient;
    }

    /// <summary>Gets the builder of the registration's named <see cref="System.Net.Http.HttpClient"/>, for adding handlers.</summary>
    public IHttpClientBuilder HttpClient { get; }
}
