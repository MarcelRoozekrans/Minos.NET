using System.Diagnostics.CodeAnalysis;
using MinosClient = Minos.IDecisionClient;
using UpstreamClient = Microsoft.Extensions.AI.IDecisionClient;

namespace Minos.Extensions.AI.Decisions;

/// <summary>
/// The entry points of the adapters. Each method is named after the client it returns: <see cref="AsUpstreamDecisionClient"/>
/// turns a Minos client into an upstream Microsoft.Extensions.AI client, and <see cref="AsMinosDecisionClient"/> turns an
/// upstream client into a Minos client.
/// </summary>
[Experimental("MEAI001")]
public static class DecisionClientAdapterExtensions
{
    /// <summary>
    /// Wraps a Minos client, or a whole Minos pipeline, in an upstream <see cref="UpstreamClient"/>: a
    /// <see cref="MinosDecisionClient"/>, which owns <paramref name="client"/> and disposes it.
    /// </summary>
    /// <param name="client">The Minos client.</param>
    /// <returns>An upstream client that asks <paramref name="client"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public static MinosDecisionClient AsUpstreamDecisionClient(this MinosClient client) => new(client);

    /// <summary>
    /// Wraps an upstream client in a Minos <see cref="MinosClient"/>: an <see cref="UpstreamDecisionClient"/>, which owns
    /// <paramref name="client"/> and disposes it. Minos pipeline stages can be built on top of it.
    /// </summary>
    /// <param name="client">The upstream client.</param>
    /// <returns>A Minos client that asks <paramref name="client"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public static UpstreamDecisionClient AsMinosDecisionClient(this UpstreamClient client) => new(client);
}
